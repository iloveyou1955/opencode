using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCode.Core.Contracts;

namespace OpenCode.Infrastructure.Tools;

/// <summary>
/// 批量执行多个工具调用的工具。
/// </summary>
public class BatchTool : ITool
{
    private readonly IEnumerable<ITool> _tools;

    public BatchTool(IEnumerable<ITool> tools)
    {
        _tools = tools;
    }

    public string Name => "batch";
    public string Description => "在单个响应中并行执行多个工具调用。显著减少复杂任务的往返次数。";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {
        "tool_calls": {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "tool": { "type": "string", "description": "工具名称" },
              "parameters": { "type": "object", "description": "工具参数" }
            },
            "required": ["tool", "parameters"]
          }
        }
      },
      "required": ["tool_calls"]
    }
    """;

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken ct = default)
    {
        var toolCallsNode = args["tool_calls"]?.AsArray();
        if (toolCallsNode == null || toolCallsNode.Count == 0) return "错误: tool_calls 列表不能为空";

        var results = new List<string>();
        var tasks = new List<Task<string>>();

        foreach (var callNode in toolCallsNode)
        {
            if (callNode is JsonObject callObj)
            {
                string toolName = callObj["tool"]?.ToString() ?? "";
                var parameters = callObj["parameters"]?.AsObject() ?? new JsonObject();

                if (toolName == "batch") continue; // 防止无限递归

                var tool = _tools.FirstOrDefault(t => t.Name == toolName);
                if (tool == null)
                {
                    results.Add($"错误: 未找到工具 '{toolName}'。");
                    continue;
                }

                // 并行执行
                tasks.Add(ExecuteToolAsync(tool, parameters, context, ct));
            }
        }

        var executedResults = await Task.WhenAll(tasks);
        results.AddRange(executedResults);

        return string.Join("\n\n---\n\n", results);
    }

    private async Task<string> ExecuteToolAsync(ITool tool, JsonObject parameters, IToolContext context, CancellationToken ct)
    {
        try
        {
            var result = await tool.ExecuteAsync(parameters, context, ct);
            return $"工具 '{tool.Name}' 执行结果:\n{result}";
        }
        catch (Exception ex)
        {
            return $"工具 '{tool.Name}' 执行失败: {ex.Message}";
        }
    }
}
