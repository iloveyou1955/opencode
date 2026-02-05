using System.Text.Json.Nodes;
using OpenCode.Core.Contracts;
using OpenCode.Core.Services;

using OpenCode.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.Infrastructure.Tools;

[ServiceRegistration(ServiceLifetime.Singleton, typeof(ITool))]
public class WriteTool : ITool
{
    private readonly BusService _bus;

    public WriteTool(BusService bus)
    {
        _bus = bus;
    }
    public string Name => "write";
    public string Description => "Write content to a file. Overwrites the file if it exists. Creates the file and directories if they don't exist.";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {
        "content": { "type": "string", "description": "The content to write to the file" },
        "filePath": { "type": "string", "description": "The absolute path to the file to write (must be absolute, not relative)" }
      },
      "required": ["content", "filePath"]
    }
    """;

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken cancellationToken = default)
    {
        string content = args["content"]?.ToString() ?? "";
        string filePath = args["filePath"]?.ToString() ?? "";

        if (string.IsNullOrEmpty(filePath)) return "Error: filePath is required";
        if (!Path.IsPathRooted(filePath)) return "Error: filePath must be absolute";

        // 权限校验
        if (!await context.RequestPermissionAsync("edit", filePath))
        {
            return $"Error: Permission denied for write on {filePath}";
        }

        string fullPath = Path.GetFullPath(filePath);
        
        // 确保目录存在
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        try
        {
            bool exists = File.Exists(fullPath);
            await File.WriteAllTextAsync(fullPath, content, cancellationToken);
            
            await _bus.PublishAsync("file.edited", new { file = fullPath });
            await _bus.PublishAsync("file.watcher.updated", new { file = fullPath, @event = exists ? "change" : "add" });

            return "Wrote file successfully.";
        }
        catch (Exception ex)
        {
            return $"Error writing file: {ex.Message}";
        }
    }
}
