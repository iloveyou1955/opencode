using System.Text.Json;
using OpenCode.Core.Models.Todo;

namespace OpenCode.Core.Utilities;

/// <summary>
/// 管理待办事项 (Todo) 的服务，支持按会话持久化。
/// </summary>
public class TodoService
{
    private readonly string _storageDir;
    private readonly Dictionary<string, List<TodoItem>> _cache = new();

    public TodoService(string storageDir)
    {
        _storageDir = storageDir;
        if (!Directory.Exists(_storageDir))
        {
            Directory.CreateDirectory(_storageDir);
        }
    }

    /// <summary>
    /// 获取指定会话的待办事项列表。
    /// </summary>
    public async Task<List<TodoItem>> GetTodosAsync(string sessionId)
    {
        if (_cache.TryGetValue(sessionId, out var cached))
        {
            return cached;
        }

        var path = GetPath(sessionId);
        if (!File.Exists(path))
        {
            return new List<TodoItem>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(path);
            var todos = JsonSerializer.Deserialize<List<TodoItem>>(json) ?? new List<TodoItem>();
            _cache[sessionId] = todos;
            return todos;
        }
        catch
        {
            return new List<TodoItem>();
        }
    }

    /// <summary>
    /// 更新指定会话的待办事项列表。
    /// </summary>
    public async Task UpdateTodosAsync(string sessionId, List<TodoItem> todos)
    {
        _cache[sessionId] = todos;
        var path = GetPath(sessionId);
        var json = JsonSerializer.Serialize(todos, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }

    private string GetPath(string sessionId) => Path.Combine(_storageDir, $"{sessionId}_todos.json");
}
