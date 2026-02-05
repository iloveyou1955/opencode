using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.AI;
using OpenCode.AgentFramework.Abstractions;
using OpenCode.AgentFramework.Executors;
using OpenCode.Core.Contracts;
using OpenCode.Core.Lsp;
using OpenCode.Core.Services;
using OpenCode.Core.Utilities;
using OpenCode.Infrastructure.Mock;
using OpenCode.Infrastructure.Tools;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCode.IntegrationTests.Workflows;

public class MockProjectContext : IProjectContext
{
    public string Directory => Path.GetTempPath();
    public string Worktree => Directory;
    public bool ContainsPath(string path) => true;
    public string GetRelativePath(string path) => path;
    public string ResolvePath(string path) => path;
}

public class MockAgentProvider : IAgentConfigurationProvider
{
    public Task<AgentMetadata?> GetAgentAsync(string name)
    {
        return Task.FromResult<AgentMetadata?>(new AgentMetadata
        {
            Name = name,
            Prompt = "You are a test agent."
        });
    }

    public Task<IEnumerable<AgentMetadata>> GetAllAgentsAsync()
    {
        return Task.FromResult<IEnumerable<AgentMetadata>>(new[] { new AgentMetadata { Name = "test" } });
    }

    public Task SaveAgentAsync(AgentMetadata agent) => Task.CompletedTask;
}

public class MockInstructionService : IInstructionService
{
    public Task<string> GetAggregatedInstructionsAsync(string agentName)
    {
        return Task.FromResult("You are a test agent with dynamic instructions.");
    }

    public Task<string> GetAggregatedInstructionsAsync(string agentName, string modelId)
    {
        return Task.FromResult($"You are a test agent ({agentName}) using model {modelId}.");
    }
}

public class MockPermissionService : IPermissionService
{
    public Task<PermissionAction> CheckPermissionAsync(string sessionId, string tool, string? pattern = null)
    {
        return Task.FromResult(PermissionAction.Allow);
    }

    public Task<PermissionResponse> AskAsync(PermissionInfo info)
    {
        return Task.FromResult(PermissionResponse.Once);
    }

    public void Respond(string permissionId, PermissionResponse response) { }

    public List<PermissionInfo> GetPending() => new();
}

public class MockLspManager : ILspManager
{
    public Task<ILspClient?> GetClientForFileAsync(string filePath, CancellationToken ct = default) => Task.FromResult<ILspClient?>(null);
    public Task<Dictionary<string, JsonElement>> GetAllDiagnosticsAsync(CancellationToken ct = default) => Task.FromResult(new Dictionary<string, JsonElement>());
    public Task<List<JsonElement>> SearchSymbolsAsync(string query, CancellationToken ct = default) => Task.FromResult(new List<JsonElement>());
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// 基础工作流集成测试
/// 验证完整的 Agent 思考-行动循环
/// </summary>
public class BasicWorkflowTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Workflow _workflow;
    private readonly MockChatClient _llmProvider;

    public BasicWorkflowTests()
    {
        var services = new ServiceCollection();

        // 1. 注册基础服务
        services.AddLogging(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Trace));
        services.AddSingleton<BusService>();
        services.AddSingleton<IProjectContext, MockProjectContext>();
        services.AddSingleton<ShellService>();
        services.AddSingleton<TruncationService>();
        services.AddSingleton<SnapshotService>();

        // 2. 注册工具
        services.AddSingleton<ITool, ReadTool>();
        services.AddSingleton<ITool, WriteTool>();
        services.AddSingleton<ITool, ListTool>();
        services.AddSingleton<ITool, BashTool>();
        services.AddSingleton<ITool, EditTool>();
        services.AddSingleton<ITool, SearchTool>();
        services.AddSingleton<ITool, GitTool>();

        // 3. 注册 Mock LLM (使用 IChatClient)
        _llmProvider = new MockChatClient();
        services.AddSingleton<IChatClient>(_llmProvider);

        // 注册 Mock Agent Provider
        var mockAgentProvider = new MockAgentProvider();
        services.AddSingleton<IAgentConfigurationProvider>(mockAgentProvider);

        // 注册 Mock Instruction Service
        services.AddSingleton<IInstructionService>(new MockInstructionService());

        // 注册 Mock Permission Service
        services.AddSingleton<IPermissionService>(new MockPermissionService());

        // 注册 Mock LSP Manager
        services.AddSingleton<ILspManager>(new MockLspManager());

        // 注册 Session Service
        var sessionDir = Path.Combine(Path.GetTempPath(), "OpenCodeTestSessions_" + Guid.NewGuid());
        services.AddSingleton<SessionService>(new SessionService(sessionDir));

        // 注册 Config Service
        services.AddSingleton<ConfigService>(sp => new ConfigService(Path.GetTempPath(), sp.GetRequiredService<ILogger<ConfigService>>()));
        
        // 注册 Compaction Service
        services.AddSingleton<CompactionService>(sp => new CompactionService(sp.GetRequiredService<IChatClient>(), "Compact"));

        // 注册 MCP Service
        services.AddSingleton<McpService>();

        // 注册 Plugin Service
        services.AddSingleton<PluginService>();

        // 4. 注册 Executors
        services.AddSingleton<ThinkingExecutor>(sp => 
            new ThinkingExecutor(
                "ThinkingExecutor", 
                sp.GetRequiredService<IChatClient>(),
                sp.GetRequiredService<IInstructionService>(),
                sp.GetRequiredService<SessionService>(),
                sp.GetRequiredService<IAgentConfigurationProvider>(),
                sp.GetRequiredService<ConfigService>(),
                sp.GetRequiredService<CompactionService>(),
                sp.GetRequiredService<SnapshotService>(),
                sp.GetRequiredService<IPermissionService>()));
        services.AddSingleton<ToolExecutor>(sp => 
            new ToolExecutor(
                "ToolRouter", 
                sp.GetServices<ITool>(), 
                sp.GetRequiredService<IPermissionService>(), 
                sp.GetRequiredService<ILspManager>(), 
                sp.GetRequiredService<IAgentConfigurationProvider>(), 
                sp.GetRequiredService<McpService>(), 
                sp.GetRequiredService<PluginService>(), 
                sp.GetRequiredService<TruncationService>(),
                sp.GetRequiredService<SnapshotService>()));

        services.AddSingleton<Workflow>();

        _serviceProvider = services.BuildServiceProvider();
        
        // 5. 构建工作流
        _workflow = _serviceProvider.GetRequiredService<Workflow>();
        _workflow.AddExecutor(_serviceProvider.GetRequiredService<ThinkingExecutor>());
        _workflow.AddExecutor(_serviceProvider.GetRequiredService<ToolExecutor>());
    }

    [Fact]
    public async Task Workflow_ShouldExecuteTool_WhenLLMRequestsIt()
    {
        // Arrange
        // 模拟 LLM 返回一个 Shell 工具调用
        _llmProvider.SetNextResponse("""
        我将执行 echo 命令来验证工具调用。
        ```json
        {
            "tool": "bash",
            "args": {
                "command": "echo Integration Test Success",
                "description": "echo for test"
            }
        }
        ```
        """);

        // 准备 CancellationToken
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        // 启动工作流
        var runTask = _workflow.RunAsync("ThinkingExecutor", "Start Test", cts.Token);
        
        // 监听输出
        bool toolExecuted = false;

        try 
        {
            await foreach (var output in _workflow.Output.WithCancellation(cts.Token))
            {
                var json = JsonSerializer.Serialize(output);
                var node = JsonNode.Parse(json);
                var status = node?["status"]?.ToString();

                if (status == "acting")
                {
                    // 验证是否尝试执行 Bash 工具
                    Assert.Equal("bash", node?["tool"]?.ToString());
                }
                else if (status == "completed")
                {
                    break;
                }
                
                if (status == "acting" && node?["tool"]?.ToString() == "bash")
                {
                    toolExecuted = true;
                    await cts.CancelAsync(); // 提前结束
                }
            }
        }
        catch (OperationCanceledException) { }

        try { await runTask; } catch (OperationCanceledException) { }

        // Assert
        Assert.True(toolExecuted, "Should have attempted to execute bash tool");
    }

    [Fact]
    public async Task Workflow_ShouldHandleFileSystemOperations()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var content = "Test Content " + Guid.NewGuid();
        File.WriteAllText(tempFile, content);

        try
        {
            // 模拟 LLM 请求读取文件
            _llmProvider.SetNextResponse($$"""
            ```json
            {
                "tool": "read",
                "args": {
                    "filePath": "{{tempFile.Replace("\\", "\\\\")}}"
                }
            }
            ```
            """);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var runTask = _workflow.RunAsync("ThinkingExecutor", "Read File", cts.Token);
            
            bool actingDetected = false;

            try
            {
                await foreach (var output in _workflow.Output.WithCancellation(cts.Token))
                {
                    var json = JsonSerializer.Serialize(output);
                    var node = JsonNode.Parse(json);
                    
                    if (node?["status"]?.ToString() == "acting" && 
                        node?["tool"]?.ToString() == "read")
                    {
                        actingDetected = true;
                        await cts.CancelAsync();
                    }
                }
            }
            catch (OperationCanceledException) { }

            try { await runTask; } catch (OperationCanceledException) { }

            Assert.True(actingDetected, "Should have routed to read tool");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
