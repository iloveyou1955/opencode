using System.Text.Json.Nodes;
using OpenCode.Core.Contracts;
using OpenCode.Core.Utilities;
using OpenCode.Core.Services;

namespace OpenCode.Infrastructure.Tools;

/// <summary>
/// 对单个文件执行多个编辑操作的工具。
/// </summary>
public class MultiEditTool : ITool
{
    private readonly BusService _bus;

    public MultiEditTool(BusService bus)
    {
        _bus = bus;
    }
    public string Name => "multiedit";
    public string Description => "在单个文件中顺序执行多个编辑操作。比多次调用 edit 更高效。";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {
        "filePath": { "type": "string", "description": "要修改的文件的绝对路径" },
        "edits": {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "oldString": { "type": "string", "description": "要替换的文本" },
              "newString": { "type": "string", "description": "替换后的文本" },
              "replaceAll": { "type": "boolean", "description": "是否替换所有匹配项 (默认 false)" }
            },
            "required": ["oldString", "newString"]
          },
          "description": "要执行的编辑操作列表"
        }
      },
      "required": ["filePath", "edits"]
    }
    """;

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken cancellationToken = default)
    {
        string filePath = args["filePath"]?.ToString() ?? "";
        var editsNode = args["edits"]?.AsArray();

        if (string.IsNullOrEmpty(filePath)) return "错误: filePath 是必需的";
        if (editsNode == null || editsNode.Count == 0) return "错误: edits 列表不能为空";

        // 权限校验
        if (!await context.RequestPermissionAsync("edit", filePath))
        {
            return $"错误: 对 {filePath} 的编辑权限被拒绝";
        }

        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath)) return $"错误: 文件 '{filePath}' 不存在。";

        try
        {
            string content = await File.ReadAllTextAsync(fullPath, cancellationToken);
            int appliedCount = 0;

            foreach (var editNode in editsNode)
            {
                if (editNode is JsonObject editObj)
                {
                    string oldString = editObj["oldString"]?.ToString() ?? "";
                    string newString = editObj["newString"]?.ToString() ?? "";
                    bool replaceAll = editObj["replaceAll"]?.GetValue<bool>() ?? false;

                    if (oldString == newString) continue;

                    string newContent = TextReplacer.Replace(content, oldString, newString, replaceAll);
                    if (newContent != content)
                    {
                        content = newContent;
                        appliedCount++;
                    }
                }
            }

            if (appliedCount > 0)
            {
                await File.WriteAllTextAsync(fullPath, content, cancellationToken);
                
                await _bus.PublishAsync("file.edited", new { file = fullPath });
                await _bus.PublishAsync("file.watcher.updated", new { file = fullPath, @event = "change" });

                return $"成功应用了 {appliedCount} 处编辑。";
            }

            return "未应用任何编辑 (可能未找到匹配项)。";
        }
        catch (Exception ex)
        {
            return $"执行多重编辑时出错: {ex.Message}";
        }
    }
}
