using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCode.Core.Contracts;

namespace OpenCode.Infrastructure.Tools;

/// <summary>
/// 子智能体编排工具，允许 Agent 启动另一个专门的 Agent 来处理子任务。
/// </summary>
public class TaskTool : ITool
{
    private readonly IWorkflowFactory _workflowFactory;

    public TaskTool(IWorkflowFactory workflowFactory)
    {
        _workflowFactory = workflowFactory;
    }

    public string Name => "task";
    public string Description => "启动一个专门的子智能体来处理特定的子任务。适用于需要深入研究或独立执行的复杂任务。";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {
        "description": { "type": "string", "description": "子任务的简短描述" },
        "prompt": { "type": "string", "description": "子智能体需要执行的具体指令" },
        "subagent_type": { "type": "string", "description": "子智能体的类型 (如 search, engineer, code-reviewer)" }
      },
      "required": ["description", "prompt", "subagent_type"]
    }
    """;

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken ct = default)
    {
        string description = args["description"]?.ToString() ?? "";
        string prompt = args["prompt"]?.ToString() ?? "";
        string subagentType = args["subagent_type"]?.ToString() ?? "";

        if (string.IsNullOrEmpty(prompt) || string.IsNullOrEmpty(subagentType))
        {
            return "错误: prompt 和 subagent_type 是必需的。";
        }

        // 权限校验
        if (!await context.RequestPermissionAsync("task", subagentType))
        {
            return $"错误: 启动类型为 {subagentType} 的子任务权限被拒绝。";
        }

        await context.Lsp.GetClientForFileAsync("dummy.txt", ct); // 仅触发 LSP 初始化 (可选)

        try
        {
            // 使用工厂运行子任务
            return await _workflowFactory.RunSubtaskAsync(prompt, subagentType, ct);
        }
        catch (Exception ex)
        {
            return $"启动子任务时出错: {ex.Message}";
        }
    }
}
