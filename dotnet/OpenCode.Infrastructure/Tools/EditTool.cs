using System.Text.Json.Nodes;
using OpenCode.Core.Contracts;
using OpenCode.Core.Utilities;
using OpenCode.Core.Services;

namespace OpenCode.Infrastructure.Tools;

public class EditTool : ITool
{
    private readonly BusService _bus;

    public EditTool(BusService bus)
    {
        _bus = bus;
    }
    public string Name => "edit";
    public string Description => "Replace a string in a file with a new string. You must read the file first to ensure you have the exact text to replace.";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {
        "filePath": { "type": "string", "description": "The absolute path to the file to modify" },
        "oldString": { "type": "string", "description": "The text to replace" },
        "newString": { "type": "string", "description": "The text to replace it with (must be different from oldString)" },
        "replaceAll": { "type": "boolean", "description": "Replace all occurrences of oldString (default false)" }
      },
      "required": ["filePath", "oldString", "newString"]
    }
    """;

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken cancellationToken = default)
    {
        string filePath = args["filePath"]?.ToString() ?? "";
        string oldString = args["oldString"]?.ToString() ?? "";
        string newString = args["newString"]?.ToString() ?? "";
        bool replaceAll = args["replaceAll"]?.GetValue<bool>() ?? false;

        if (string.IsNullOrEmpty(filePath)) return "Error: filePath is required";
        if (oldString == newString) return "Error: oldString and newString must be different";

        // 权限校验
        if (!await context.RequestPermissionAsync("edit", filePath))
        {
            return $"Error: Permission denied for edit on {filePath}";
        }

        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath)) return $"Error: File '{filePath}' not found.";

        try
        {
            string originalContent = await File.ReadAllTextAsync(fullPath, cancellationToken);
            string newContent = TextReplacer.Replace(originalContent, oldString, newString, replaceAll);
            
            await File.WriteAllTextAsync(fullPath, newContent, cancellationToken);
            
            await _bus.PublishAsync("file.edited", new { file = fullPath });
            await _bus.PublishAsync("file.watcher.updated", new { file = fullPath, @event = "change" });

            var output = "Edit applied successfully.";

            // 报告 LSP 错误
            var diagnostics = await context.Lsp.GetAllDiagnosticsAsync(cancellationToken);
            var diagOutput = DiagnosticFormatter.Format(diagnostics, fullPath);
            if (!string.IsNullOrEmpty(diagOutput))
            {
                output += $"\n\n{diagOutput}";
            }

            return output;
        }
        catch (Exception ex)
        {
            return $"Error applying edit: {ex.Message}";
        }
    }
}
