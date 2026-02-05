using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace OpenCode.AgentFramework.Workflows;

public class MafWorkflow
{
    public static Workflow BuildParallelWorkflow(IChatClient client)
    {
        // 1. 定义多个专业 Agent
        var coder = client.AsAIAgent(name: "Coder", instructions: "You write code.");
        var reviewer = client.AsAIAgent(name: "Reviewer", instructions: "You review code.");
        var tester = client.AsAIAgent(name: "Tester", instructions: "You write tests.");

        // 2. 使用 WorkflowBuilder 构建图
        // 这里演示并行处理：Coder 生成后，Reviewer 和 Tester 同时工作 (假设这是支持的图结构)
        // 或者简单的顺序流
        var workflow = new WorkflowBuilder(coder)
            .AddEdge(coder, reviewer)
            .AddEdge(coder, tester)
            .Build();

        return workflow;
    }

    public static async Task ExecuteAsync(Workflow workflow, string input)
    {
        // 3. 执行工作流
        // MAF 的 InProcessExecution 会自动处理消息分发和状态同步
        await using var run = await InProcessExecution.StreamAsync(workflow, new ChatMessage(ChatRole.User, input));
        
        // 发送 TurnToken 触发执行
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        await foreach (var evt in run.WatchStreamAsync())
        {
            if (evt is AgentResponseUpdateEvent response)
            {
                Console.WriteLine($"[{response.ExecutorId}]: {response.Data}");
            }
        }
    }
}
