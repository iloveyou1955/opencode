using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using OpenCode.Core.Contracts;
using OpenCode.Core.Utilities;

namespace OpenCode.Infrastructure.Tools;

public class SearchTool : ITool
{
    public string Name => "search";
    public string Description => "Search files using Glob patterns or Regex. Commands: glob, grep";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {
        "command": { "type": "string", "enum": ["glob", "grep"] },
        "pattern": { "type": "string", "description": "Glob pattern (for glob) or Regex pattern (for grep)" },
        "path": { "type": "string", "description": "Root directory to search in (default: .)" },
        "include": { "type": "string", "description": "Glob pattern to filter files for grep (default: **/*)" },
        "case_sensitive": { "type": "boolean", "description": "Case sensitive search (default: false)" }
      },
      "required": ["command", "pattern"]
    }
    """;

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken cancellationToken = default)
    {
        string command = args["command"]?.ToString() ?? "";
        string pattern = args["pattern"]?.ToString() ?? "";
        string rootPath = args["path"]?.ToString() ?? ".";
        string includePattern = args["include"]?.ToString() ?? "**/*";
        bool caseSensitive = args["case_sensitive"]?.GetValue<bool>() ?? false;

        if (string.IsNullOrEmpty(pattern)) return "Error: Pattern is required.";
        
        // Normalize root path
        rootPath = Path.GetFullPath(rootPath);
        if (!Directory.Exists(rootPath)) return $"Error: Directory '{rootPath}' not found.";

        // 权限校验
        if (!await context.RequestPermissionAsync(command, rootPath))
        {
            return $"Error: Permission denied for {command} on {rootPath}";
        }

        try
        {
            switch (command)
            {
                case "glob":
                    return await ExecuteGlobAsync(rootPath, pattern);
                
                case "grep":
                    return await ExecuteGrepAsync(rootPath, pattern, includePattern, caseSensitive, cancellationToken);
                
                default:
                    return $"Error: Unknown command '{command}'";
            }
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private async Task<string> ExecuteGlobAsync(string rootPath, string pattern)
    {
        var matcher = new Matcher();
        matcher.AddInclude(pattern);
        
        // Exclude common binary/temp folders
        matcher.AddExclude("**/bin/**");
        matcher.AddExclude("**/obj/**");
        matcher.AddExclude("**/.git/**");
        matcher.AddExclude("**/node_modules/**");

        var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(rootPath)));
        
        if (!result.HasMatches) return "No files found matching the pattern.";

        var sb = new StringBuilder();
        foreach (var file in result.Files)
        {
            sb.AppendLine(file.Path);
        }

        var output = sb.ToString();
        var truncResult = await Truncator.TruncateAsync(output);
        return truncResult.Content;
    }

    private async Task<string> ExecuteGrepAsync(string rootPath, string regexPattern, string filePattern, bool caseSensitive, CancellationToken ct)
    {
        // 1. Find files to search
        var matcher = new Matcher();
        matcher.AddInclude(filePattern);
        matcher.AddExclude("**/bin/**");
        matcher.AddExclude("**/obj/**");
        matcher.AddExclude("**/.git/**");
        matcher.AddExclude("**/node_modules/**");

        var fileResult = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(rootPath)));
        if (!fileResult.HasMatches) return "No files found to search in.";

        // 2. Prepare Regex
        var options = RegexOptions.Compiled;
        if (!caseSensitive) options |= RegexOptions.IgnoreCase;
        
        Regex regex;
        try
        {
            regex = new Regex(regexPattern, options);
        }
        catch (ArgumentException ex)
        {
            return $"Error: Invalid Regex pattern. {ex.Message}";
        }

        // 3. Search sequentially (Parallel.ForEachAsync caused crashes in test runner)
        var results = new List<string>();
        var files = fileResult.Files.Select(f => Path.Combine(rootPath, f.Path)).ToList();

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) break;
            
            try
            {
                using var reader = new StreamReader(file);
                int lineNum = 0;
                while (await reader.ReadLineAsync(ct) is string line)
                {
                    lineNum++;
                    if (regex.IsMatch(line))
                    {
                        // Format: relative_path:line_num: content
                        string relativePath = Path.GetRelativePath(rootPath, file);
                        results.Add($"{relativePath}:{lineNum}: {line.Trim()}");
                    }
                }
            }
            catch
            {
                // Ignore file read errors
            }
        }

        if (results.Count == 0) return "No matches found.";

        // Sort results for consistent output
        var sortedResults = results.OrderBy(x => x).ToList();
        var output = string.Join("\n", sortedResults);

        var truncResult = await Truncator.TruncateAsync(output);
        return truncResult.Content;
    }
}
