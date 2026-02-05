using System.Text.Json.Nodes;
using System.Text;
using OpenCode.Core.Contracts;
using OpenCode.Core.Utilities;

namespace OpenCode.Infrastructure.Tools;

/// <summary>
/// External Directory Tool, allows listing directories outside the project root for research.
/// </summary>
public class ExternalDirectoryTool : ITool
{
    public string Name => "external_directory";
    public string Description => "List directories outside the project root. Useful for checking system configs or other projects.";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {
        "path": { "type": "string", "description": "The absolute path to the directory to list." }
      },
      "required": ["path"]
    }
    """;

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken cancellationToken = default)
    {
        string? path = args["path"]?.ToString();
        if (string.IsNullOrEmpty(path)) return "Error: Path is required.";

        string absolutePath = Path.GetFullPath(path);

        // Permission check
        if (!await context.RequestPermissionAsync("external_directory", absolutePath))
        {
            return $"Error: Permission denied for external_directory on {absolutePath}";
        }

        if (!Directory.Exists(absolutePath)) return $"Error: Directory '{absolutePath}' not found.";

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{absolutePath}/");
            
            var entries = Directory.GetFileSystemEntries(absolutePath);
            var sortedEntries = entries.OrderBy(e => Path.GetFileName(e)).Take(100);

            foreach (var entry in sortedEntries)
            {
                string name = Path.GetFileName(entry);
                bool isDir = Directory.Exists(entry);
                sb.AppendLine(isDir ? $"  {name}/" : $"  {name}");
            }

            if (entries.Length > 100)
            {
                sb.AppendLine("  ... (truncated)");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error listing external directory: {ex.Message}";
        }
    }
}
