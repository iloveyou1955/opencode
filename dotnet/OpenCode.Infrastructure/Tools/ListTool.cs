using System.Text.Json.Nodes;
using System.Text;
using OpenCode.Core.Contracts;
using OpenCode.Core.Utilities;

namespace OpenCode.Infrastructure.Tools;

public class ListTool : ITool
{
    public string Name => "list";
    public string Description => "List files and directories in a given path.";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {
        "path": { "type": "string", "description": "The absolute path to the directory to list (must be absolute, not relative). Defaults to current directory." },
        "ignore": { "type": "array", "items": { "type": "string" }, "description": "List of glob patterns to ignore" }
      }
    }
    """;
    
    // Default ignore patterns aligned with opencode TS
    private static readonly string[] DefaultIgnorePatterns = 
    {
        "node_modules", "__pycache__", ".git", "dist", "build", "target", "vendor", "bin", "obj", 
        ".idea", ".vscode", ".zig-cache", "zig-out", ".coverage", "coverage", "tmp", "temp", 
        ".cache", "cache", "logs", ".venv", "venv", "env"
    };

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken cancellationToken = default)
    {
        string? pathArg = args["path"]?.ToString();
        string searchPath = string.IsNullOrEmpty(pathArg) ? Directory.GetCurrentDirectory() : Path.GetFullPath(pathArg);

        // 权限校验
        if (!await context.RequestPermissionAsync("list", searchPath))
        {
            return $"Error: Permission denied for list on {searchPath}";
        }

        if (!Directory.Exists(searchPath)) return $"Error: Directory '{searchPath}' not found.";

        try
        {
            var ignorePatterns = new List<string>(DefaultIgnorePatterns);
            
            // Load .gitignore
            string gitIgnorePath = Path.Combine(searchPath, ".gitignore");
            if (File.Exists(gitIgnorePath))
            {
                var lines = await File.ReadAllLinesAsync(gitIgnorePath, cancellationToken);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
                    ignorePatterns.Add(trimmed);
                }
            }

            // Load user ignores
            if (args["ignore"] is JsonArray ignoreArray)
            {
                foreach (var item in ignoreArray)
                {
                    if (item != null) ignorePatterns.Add(item.ToString());
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{searchPath}/");
            
            int totalFiles = 0;
            const int MAX_FILES = 100;
            
            sb.Append(RenderDir(searchPath, 0, ignorePatterns, searchPath, ref totalFiles, MAX_FILES));
            
            if (totalFiles >= MAX_FILES)
            {
                sb.AppendLine("... (truncated, too many files)");
            }
            
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error listing directory: {ex.Message}";
        }
    }

    private string RenderDir(string dirPath, int depth, List<string> ignorePatterns, string rootPath, ref int totalFiles, int maxFiles)
    {
        if (depth > 2 || totalFiles >= maxFiles) return "";

        var sb = new StringBuilder();
        string indent = new string(' ', (depth + 1) * 2);

        try
        {
            var entries = Directory.GetFileSystemEntries(dirPath);
            var sortedEntries = entries.OrderBy(e => Path.GetFileName(e)).ToArray();

            foreach (var entry in sortedEntries)
            {
                if (totalFiles >= maxFiles) break;

                string relativePath = Path.GetRelativePath(rootPath, entry);
                string name = Path.GetFileName(entry);
                bool isDir = Directory.Exists(entry);
                
                // Check if ignored
                bool isIgnored = false;
                foreach (var pattern in ignorePatterns)
                {
                    if (WildcardMatcher.Match(relativePath, pattern))
                    {
                        isIgnored = true;
                        break;
                    }
                }

                if (isIgnored) continue;

                if (isDir)
                {
                    sb.AppendLine($"{indent}{name}/");
                    totalFiles++;
                    sb.Append(RenderDir(entry, depth + 1, ignorePatterns, rootPath, ref totalFiles, maxFiles));
                }
                else
                {
                    sb.AppendLine($"{indent}{name}");
                    totalFiles++;
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            sb.AppendLine($"{indent}<Access Denied>");
        }

        return sb.ToString();
    }
}
