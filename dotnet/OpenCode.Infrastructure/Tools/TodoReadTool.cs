using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCode.Core.Contracts;
using OpenCode.Core.Utilities;

namespace OpenCode.Infrastructure.Tools;

/// <summary>
/// 读取待办事项列表的工具。
/// </summary>
public class TodoReadTool : ITool
{
    private readonly TodoService _todoService;

    public TodoReadTool(TodoService todoService)
    {
        _todoService = todoService;
    }

    public string Name => "todoread";
    public string Description => "查看当前的待办事项列表及任务进度。";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {}
    }
    """;

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken ct = default)
    {
        try
        {
            var todos = await _todoService.GetTodosAsync(context.SessionId);
            if (todos.Count == 0) return "当前没有任何待办事项。";

            return JsonSerializer.Serialize(todos, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"读取待办事项时出错: {ex.Message}";
        }
    }
}
