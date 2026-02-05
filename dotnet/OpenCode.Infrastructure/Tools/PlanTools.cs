using System.Text.Json.Nodes;
using OpenCode.Core.Contracts;

namespace OpenCode.Infrastructure.Tools;

/// <summary>
/// 进入规划模式的工具。
/// </summary>
public class PlanEnterTool : ITool
{
    public string Name => "plan_enter";
    public string Description => "进入规划模式。在此模式下，Agent 将专注于研究、分析和制定详细的执行计划，而不是直接修改代码。";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {}
    }
    """;

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken ct = default)
    {
        // 在 TUI 中询问用户
        Console.WriteLine();
        Console.WriteLine("[规划模式] Agent 请求进入规划模式。");
        Console.Write("是否切换到规划模式? (y/N): ");
        
        var input = Console.ReadLine()?.Trim().ToLower();
        if (input == "y" || input == "yes")
        {
            // 这里我们需要一种机制来通知 ThinkingExecutor 切换 Agent
            // 暂时通过返回特定标记让 Executor 处理
            return "SUCCESS: User approved plan mode. Switch to 'plan' agent and begin research.";
        }

        return "REJECTED: User declined switching to plan mode. Stay in 'build' mode.";
    }
}

/// <summary>
/// 退出规划模式并执行计划的工具。
/// </summary>
public class PlanExitTool : ITool
{
    public string Name => "plan_exit";
    public string Description => "退出规划模式。Agent 将根据已批准的计划切换到执行模式。";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {}
    }
    """;

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken ct = default)
    {
        Console.WriteLine();
        Console.WriteLine("[执行模式] Agent 请求完成规划并开始执行。");
        Console.Write("是否批准计划并开始执行? (y/N): ");
        
        var input = Console.ReadLine()?.Trim().ToLower();
        if (input == "y" || input == "yes")
        {
            return "SUCCESS: Plan approved. Switch back to 'build' agent and execute the plan.";
        }

        return "REJECTED: User requested to continue refining the plan.";
    }
}
