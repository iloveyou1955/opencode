using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCode.Core.Contracts;
using OpenCode.Core.Models.Todo;
using OpenCode.Core.Utilities;

namespace OpenCode.Infrastructure.Tools;

/// <summary>
/// 写入待办事项列表的工具。
/// </summary>
public class TodoWriteTool : ITool
{
    private readonly TodoService _todoService;

    public TodoWriteTool(TodoService todoService)
    {
        _todoService = todoService;
    }

    public string Name => "todowrite";
    public string Description => "创建或更新待办事项列表。在处理复杂的多步骤任务时，应主动使用此工具记录进度。";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {
        "todos": {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "id": { "type": "string" },
              "content": { "type": "string" },
              "status": { "type": "string", "enum": ["pending", "in_progress", "completed"] },
              "priority": { "type": "string", "enum": ["low", "medium", "high"] }
            },
            "required": ["content", "status", "priority"]
          },
          "description": "更新后的待办事项列表"
        }
      },
      "required": ["todos"]
    }
    """;

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken ct = default)
    {
        var todosNode = args["todos"] as JsonArray;
        if (todosNode == null) return "错误: todos 必须是一个数组。";

        try
        {
            var todos = JsonSerializer.Deserialize<List<TodoItem>>(todosNode.ToJsonString()) ?? new List<TodoItem>();
            
            // 为没有 ID 的项生成 ID
            foreach (var item in todos)
            {
                if (string.IsNullOrEmpty(item.Id)) item.Id = Guid.NewGuid().ToString();
            }

            await _todoService.UpdateTodosAsync(context.SessionId, todos);
            
            int pending = todos.Count(t => t.Status != "completed");
            return $"成功更新待办事项列表。当前共有 {todos.Count} 个事项，其中 {pending} 个未完成。";
        }
        catch (Exception ex)
        {
            return $"更新待办事项时出错: {ex.Message}";
        }
    }
}
