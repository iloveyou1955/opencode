using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OpenCode.Core.Contracts;

namespace OpenCode.Infrastructure.Tools;

/// <summary>
/// 基于正则表达式的全局搜索工具。
/// </summary>
public class GrepTool : ITool
{
    public string Name => "grep";
    public string Description => "强大的文本搜索工具，支持正则表达式。可在指定文件或目录中查找匹配行。";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {
        "pattern": { "type": "string", "description": "正则表达式模式" },
        "path": { "type": "string", "description": "搜索的文件或目录路径" },
        "include": { "type": "string", "description": "包含的文件模式，如 '*.cs'" },
        "caseSensitive": { "type": "boolean", "description": "是否区分大小写，默认为 false" }
      },
      "required": ["pattern", "path"]
    }
    """;

    private class SearchState
    {
        public int MatchCount;
        public StringBuilder Results = new();
    }

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken ct = default)
    {
        string pattern = args["pattern"]?.ToString() ?? "";
        string path = args["path"]?.ToString() ?? "";
        string include = args["include"]?.ToString() ?? "*";
        bool caseSensitive = args["caseSensitive"]?.GetValue<bool>() ?? false;

        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(path))
        {
            return "错误: pattern 和 path 都是必需的";
        }

        if (!await context.RequestPermissionAsync("grep", path))
        {
            return $"错误: 对 {path} 的 grep 操作权限被拒绝";
        }

        try
        {
            var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            var regex = new Regex(pattern, options | RegexOptions.Compiled);
            var state = new SearchState();
            const int maxMatches = 500;

            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                await SearchInFileAsync(fullPath, regex, state, ct);
            }
            else if (Directory.Exists(fullPath))
            {
                var files = Directory.GetFiles(fullPath, include, SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    if (ct.IsCancellationRequested) break;
                    await SearchInFileAsync(file, regex, state, ct);
                    if (state.MatchCount >= maxMatches)
                    {
                        state.Results.AppendLine($"\n(由于匹配数超过 {maxMatches}，搜索已停止)");
                        break;
                    }
                }
            }
            else
            {
                return $"错误: 路径 '{path}' 不存在。";
            }

            return state.MatchCount > 0 ? state.Results.ToString() : "未找到匹配项。";
        }
        catch (Exception ex)
        {
            return $"执行 Grep 搜索时出错: {ex.Message}";
        }
    }

    private async Task SearchInFileAsync(string filePath, Regex regex, SearchState state, CancellationToken ct)
    {
        using var reader = new StreamReader(filePath);
        int lineNumber = 0;
        while (await reader.ReadLineAsync(ct) is string line)
        {
            lineNumber++;
            if (regex.IsMatch(line))
            {
                state.MatchCount++;
                state.Results.AppendLine($"{filePath}:{lineNumber}: {line.Trim()}");
                if (state.MatchCount >= 500) return;
            }
        }
    }
}
