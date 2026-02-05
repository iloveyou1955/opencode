using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.AI;
using OpenCode.Core.Contracts;
using OpenCode.Core.Models;
using OpenCode.Core.Services;
using OpenCode.Core.Utilities;
using OpenCode.Core.Lsp;
using OpenCode.Infrastructure.AI;
using OpenCode.Infrastructure.Extensions;
using OpenCode.Infrastructure.Plugins;
using OpenCode.Infrastructure.Services;
using OpenCode.Infrastructure.Tools;
using OpenCode.Infrastructure.Lsp;
using OpenCode.AgentFramework;
using OpenCode.AgentFramework.Abstractions;
using OpenCode.AgentFramework.Executors;
using OpenCode.TUI.Services;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO;
using System.Linq;
using System;

namespace OpenCode.TUI
{
    public static class HostExtensions
    {
        public static IHostBuilder CreateOpenCodeHost(string[] args, string projectRoot)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    // 0. 注册 IServiceCollection 供后续使用（如 Server 路由）
                    services.AddSingleton(services);

                    // 1. 注册基础上下文
                    services.AddSingleton<IProjectContext>(new ProjectContext(projectRoot));
                    
                    // 2. 自动注册所有带有特性的服务
                    services.AddAutoRegisteredServices(
                        typeof(BusService).Assembly,       // OpenCode.Core
                        typeof(ProjectContext).Assembly,   // OpenCode.Infrastructure
                        typeof(Program).Assembly           // OpenCode.TUI
                    );

                    // 3. 注册需要特殊初始化的服务
                    var todoDir = Path.Combine(projectRoot, ".opencode", "todos");
                    services.AddSingleton(new TodoService(todoDir));
                    
                    var skillDir = Path.Combine(projectRoot, ".opencode", "skills");
                    services.AddSingleton(new SkillService(skillDir));
                    
                    services.AddSingleton<ICodeMapService, CodeMapService>();
                    services.AddSingleton<ConfigService>(sp => new ConfigService(projectRoot, sp.GetRequiredService<ILogger<ConfigService>>()));
                    
                    services.AddSingleton<TruncationService>(sp => new TruncationService(
                        projectRoot, 
                        sp.GetRequiredService<Scheduler>(),
                        sp.GetRequiredService<ILogger<TruncationService>>()));
                    
                    services.AddSingleton<ThemeService>(new ThemeService(projectRoot));
                    services.AddSingleton<FrecencyService>(new FrecencyService(projectRoot));
                    services.AddSingleton<StashService>(new StashService(projectRoot));
                    
                    services.AddHttpClient<ModelDiscoveryService>();
                    
                    // 4. 注册工具 (尚未全部打上特性的手动注册)
                    services.AddSingleton<ITool, EditTool>();
                    services.AddSingleton<ITool, ReadTool>();
                    services.AddSingleton<ITool, ListTool>();
                    services.AddSingleton<ITool, BashTool>();
                    services.AddSingleton<ITool, GitTool>();
                    services.AddSingleton<ITool, MultiEditTool>();
                    services.AddSingleton<ITool, WebSearchTool>();
                    services.AddSingleton<ITool, QuestionTool>();
                    services.AddSingleton<ITool, LspTool>();
                    services.AddSingleton<ITool, TodoWriteTool>();
                    services.AddSingleton<ITool, TodoReadTool>();
                    services.AddSingleton<ITool, GlobTool>();
                    services.AddSingleton<ITool, GrepTool>();
                    services.AddSingleton<ITool, PlanEnterTool>();
                    services.AddSingleton<ITool, PlanExitTool>();
                    services.AddSingleton<ITool, TaskTool>();
                    services.AddSingleton<ITool, SkillTool>();
                    services.AddSingleton<ITool, WebFetchTool>();
                    services.AddSingleton<ITool, ApplyPatchTool>();
                    services.AddSingleton<ITool, ExternalDirectoryTool>();
                    services.AddSingleton<ITool, InvalidTool>();
                    
                    services.AddSingleton<ITool>(sp => new CodeSearchTool(sp.GetRequiredService<IProjectContext>(), sp.GetRequiredService<SearchCache>()));
                    services.AddSingleton<ITool>(sp => new BatchTool(sp.GetServices<ITool>()));

                    // 5. 核心逻辑服务
                    services.AddSingleton<FeatureManager>(sp => {
                        var config = sp.GetRequiredService<ConfigService>().Config;
                        return new FeatureManager(config.Tools?.ToDictionary(x => x.Key, x => x.Value));
                    });

                    services.AddSingleton<ILspManager>(sp => {
                        var config = sp.GetRequiredService<ConfigService>().Config;
                        return new LspManager(projectRoot, config.Lsp);
                    });

                    services.AddSingleton<IChatClient>(sp => {
                        var configService = sp.GetRequiredService<ConfigService>();
                        var config = configService.Config;
                        var defaultModel = config.Model ?? Environment.GetEnvironmentVariable("OPENAI_MODEL");
                        
                        if (string.IsNullOrEmpty(defaultModel))
                        {
                            // 如果没有配置模型，返回一个 NullChatClient 或者在调用时报错
                            // 这里我们选择返回一个特殊的客户端，或者让 Factory 处理
                            return AIClientFactory.CreateClient("none/none", config, sp);
                        }
                        
                        return AIClientFactory.CreateClient(defaultModel, config, sp);
                    });

                    var sessionDir = Path.Combine(projectRoot, ".opencode", "sessions");
                    services.AddSingleton(sp => new SessionService(
                        sessionDir, 
                        sp.GetService<SnapshotService>(),
                        sp.GetService<BusService>(),
                        sp.GetService<VcsService>()));

                    var agentDir = Path.Combine(projectRoot, ".opencode", "agent");
                    services.AddSingleton<IAgentConfigurationProvider>(new FileSystemAgentProvider(agentDir));
                    
                    services.AddSingleton<IInstructionService>(sp => new InstructionService(
                        projectRoot, 
                        sp.GetRequiredService<IAgentConfigurationProvider>(), 
                        sp.GetRequiredService<PromptService>(),
                        sp.GetRequiredService<McpService>(),
                        sp.GetRequiredService<ShellService>(),
                        sp.GetRequiredService<ContextService>(),
                        sp.GetRequiredService<ILspManager>()));

                    services.AddSingleton<IPermissionService>(sp => {
                        var config = sp.GetRequiredService<ConfigService>().Config;
                        var permissionNode = config.Permissions != null 
                            ? JsonSerializer.SerializeToNode(config.Permissions) as JsonObject 
                            : new JsonObject();
                        return new PermissionService(
                            new JsonObject { ["permission"] = permissionNode }, 
                            sp.GetRequiredService<IProjectContext>(),
                            sp.GetRequiredService<BusService>(),
                            sp.GetRequiredService<ILogger<PermissionService>>());
                    });

                    // 6. Workflow 组件
                    services.AddTransient<ThinkingExecutor>(sp => 
                        new ThinkingExecutor(
                            "ThinkingExecutor", 
                            sp.GetRequiredService<IChatClient>(),
                            sp.GetRequiredService<IInstructionService>(),
                            sp.GetRequiredService<SessionService>(),
                            sp.GetRequiredService<IAgentConfigurationProvider>(),
                            sp.GetRequiredService<ConfigService>(),
                            sp.GetRequiredService<CompactionService>(),
                            sp.GetRequiredService<SnapshotService>(),
                            sp.GetRequiredService<IPermissionService>(),
                            sp.GetRequiredService<PluginService>(),
                            sp.GetRequiredService<SessionRetryService>(),
                            "build"
                        ));
                    
                    services.AddTransient<ToolExecutor>(sp => 
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

                    services.AddTransient<Workflow>(sp => {
                        var wf = new Workflow(sp.GetService<ILogger<Workflow>>());
                        wf.AddExecutor(sp.GetRequiredService<ThinkingExecutor>());
                        wf.AddExecutor(sp.GetRequiredService<ToolExecutor>());
                        return wf;
                    });
                });
        }
    }
}
