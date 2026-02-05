using OpenCode.AgentFramework.Abstractions;
using OpenCode.Core.Contracts;
using OpenCode.Core.Lsp;
using OpenCode.Core.Services;
using OpenCode.Core.Utilities;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCode.AgentFramework.Executors;
/// <summary>
/// 工具执行器，负责执行具体工具
/// </summary>
public class ToolExecutor : Executor<JsonNode>
{
    private readonly IEnumerable<ITool> _tools;
    private readonly IPermissionService _permissionService;
    private readonly ILspManager _lspManager;
    private readonly IAgentConfigurationProvider _agentProvider;
    private readonly McpService _mcpService;
    private readonly PluginService? _pluginService;
    private readonly TruncationService? _truncationService;
    private readonly SnapshotService? _snapshotService;

    public ToolExecutor(string id, IEnumerable<ITool> tools, IPermissionService permissionService, ILspManager lspManager, IAgentConfigurationProvider agentProvider, McpService mcpService, PluginService? pluginService = null, TruncationService? truncationService = null, SnapshotService? snapshotService = null) : base(id)
    {
        _tools = tools;
        _permissionService = permissionService;
        _lspManager = lspManager;
        _agentProvider = agentProvider;
        _mcpService = mcpService;
        _pluginService = pluginService;
        _truncationService = truncationService;
        _snapshotService = snapshotService;
    }

    protected override async Task ProcessAsync(JsonNode input, IWorkflowContext context, CancellationToken cancellationToken)
    {
        string? toolName = input["tool"]?.ToString();
        var args = input["args"] as JsonObject;
        string? agentName = input["agent"]?.ToString();
        string? callId = input["callId"]?.ToString();

        if (string.IsNullOrEmpty(toolName))
        {
            await context.YieldOutputAsync(new { status = "error", message = "Tool name missing" }, cancellationToken);
            return;
        }

        var tool = _tools.FirstOrDefault(t => t.Name == toolName);
        
        bool isMcp = false;
        if (tool == null && _mcpService.IsMcpTool(toolName))
        {
            isMcp = true;
        }

        if (tool == null && !isMcp)
        {
            var invalidTool = _tools.FirstOrDefault(t => t.Name == "invalid");
            if (invalidTool != null)
            {
                var invalidArgs = new JsonObject { ["message"] = $"Tool '{toolName}' not found" };
                var toolContext = new DefaultToolContext(context.WorkflowId, Guid.NewGuid().ToString(), _permissionService, _lspManager);
                var invalidResult = await invalidTool.ExecuteAsync(invalidArgs, toolContext, cancellationToken);
                await context.SendMessageAsync("ThinkingExecutor", new JsonObject { ["tool"] = toolName, ["result"] = invalidResult }, cancellationToken);
                return;
            }

            await context.YieldOutputAsync(new { status = "error", message = $"Tool '{toolName}' not found" }, cancellationToken);
            return;
        }

        await context.YieldOutputAsync(new { status = "acting", tool = toolName, args = args }, cancellationToken);

        if (_pluginService != null)
        {
            await _pluginService.TriggerToolCallAsync(toolName, args ?? new JsonObject());
        }

        try
        {
            // 创建上下文
            var toolContext = new DefaultToolContext(
                context.WorkflowId, // 使用 WorkflowId 作为 SessionId
                Guid.NewGuid().ToString(), 
                _permissionService,
                _lspManager);

            string result;
            if (isMcp)
            {
                result = await _mcpService.CallToolAsync(toolName, args ?? new JsonObject());
            }
            else
            {
                result = await tool!.ExecuteAsync(args ?? new JsonObject(), toolContext, cancellationToken);
            }
            
            // 获取 Agent 元数据以进行截断提示
            AgentMetadata? agent = null;
            if (!string.IsNullOrEmpty(agentName))
            {
                agent = await _agentProvider.GetAgentAsync(agentName);
            }

            // 截断长输出
            string finalResult = result;
            if (_truncationService != null)
            {
                var (truncated, _, _) = await _truncationService.TruncateAsync(result, toolName, agent);
                finalResult = truncated;
            }

            // 获取执行后的快照
            string? snapshotHash = null;
            if (_snapshotService != null)
            {
                snapshotHash = await _snapshotService.TrackAsync();
            }

            // 将结果发送回 ThinkingExecutor
            var observation = new JsonObject
            {
                ["tool"] = toolName,
                ["result"] = finalResult,
                ["snapshot"] = snapshotHash,
                ["callId"] = callId
            };

            // 发送给 ThinkingExecutor 继续下一轮思考
            await context.SendMessageAsync("ThinkingExecutor", observation, cancellationToken);
        }
        catch (Exception ex)
        {
            await context.YieldOutputAsync(new { status = "error", message = ex.Message }, cancellationToken);
            
            var errorObservation = new JsonObject
            {
                ["tool"] = toolName,
                ["result"] = $"Error: {ex.Message}",
                ["callId"] = callId
            };
             await context.SendMessageAsync("ThinkingExecutor", errorObservation, cancellationToken);
        }
    }
}