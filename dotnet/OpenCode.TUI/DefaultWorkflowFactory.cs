using Microsoft.Extensions.DependencyInjection;
using OpenCode.AgentFramework.Abstractions;
using OpenCode.Core.Contracts;
using OpenCode.AgentFramework.Executors;
using System.Text.Json.Nodes;

using OpenCode.Core.Attributes;

namespace OpenCode.TUI;

[ServiceRegistration(ServiceLifetime.Singleton, typeof(IWorkflowFactory))]
public class DefaultWorkflowFactory : IWorkflowFactory
{
    private readonly IServiceProvider _serviceProvider;

    public DefaultWorkflowFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<string> RunSubtaskAsync(string prompt, string agentType, CancellationToken ct = default)
    {
        // 每次创建一个全新的 Workflow 实例
        var wf = new Workflow();
        
        // 注入基础 Executors
        wf.AddExecutor(_serviceProvider.GetRequiredService<ThinkingExecutor>());
        wf.AddExecutor(_serviceProvider.GetRequiredService<ToolExecutor>());
        
        // 启动子任务
        var initialInput = new { prompt, agent = agentType };
        var runTask = wf.RunAsync("ThinkingExecutor", initialInput, ct);

        string finalAnswer = "子任务未返回明确结果。";

        // 监听输出，直到找到最终答案
        await foreach (var output in wf.Output.WithCancellation(ct))
        {
            if (output is JsonNode node && node["status"]?.ToString() == "completed")
            {
                finalAnswer = node["answer"]?.ToString() ?? finalAnswer;
                break;
            }
            
            // 兼容匿名对象
            try 
            {
                dynamic dyn = output;
                if (dyn.status == "completed")
                {
                    finalAnswer = dyn.answer;
                    break;
                }
            }
            catch { }
        }

        return finalAnswer;
    }
}
