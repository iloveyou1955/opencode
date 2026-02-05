using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using OpenCode.Core.Contracts;
using OpenCode.Core.Utilities;

namespace OpenCode.Infrastructure.Tools;

/// <summary>
/// 代码搜索工具，对标 TS 版的 codesearch。
/// 提供基于模式匹配的文件内容搜索。
/// </summary>
public class CodeSearchTool : ITool
{
    private readonly SearchCache? _cache;
    private readonly IProjectContext _projectContext;

    public CodeSearchTool(IProjectContext projectContext, SearchCache? cache = null)
    {
        _projectContext = projectContext;
        _cache = cache;
    }

    public string Name => "codesearch";
    public string Description => "在整个代码库中搜索代码模式。高度优化，适用于查找定义和用法。";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {
        "query": { "type": "string", "description": "搜索查询或正则表达式模式" },
        "include": { "type": "string", "description": "包含的文件 Glob 模式 (如 **/*.cs)" },
        "exclude": { "type": "string", "description": "排除的文件 Glob 模式" },
        "caseSensitive": { "type": "boolean", "description": "是否区分大小写" }
      },
      "required": ["query"]
    }
    """;

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken cancellationToken = default)
    {
        string query = args["query"]?.ToString() ?? "";
        string include = args["include"]?.ToString() ?? "**/*";
        string? exclude = args["exclude"]?.ToString();
        bool caseSensitive = args["caseSensitive"]?.GetValue<bool>() ?? false;

        if (string.IsNullOrEmpty(query)) return "错误: query 是必需的";

        // 尝试从缓存获取
        string cacheKey = _cache?.GenerateKey("codesearch", query, include, exclude + caseSensitive) ?? "";
        if (_cache != null)
        {
            var cached = _cache.Get<string>(cacheKey);
            if (cached != null) return cached;
        }

        var projectRoot = _projectContext.Directory;
        
        // 权限校验
        if (!await context.RequestPermissionAsync("codesearch", projectRoot))
        {
            return $"错误: 在 {projectRoot} 执行 codesearch 的权限被拒绝";
        }

        try
        {
            var matcher = new Matcher();
            matcher.AddInclude(include);
            if (!string.IsNullOrEmpty(exclude)) matcher.AddExclude(exclude);
            
            matcher.AddExclude("**/bin/**");
            matcher.AddExclude("**/obj/**");
            matcher.AddExclude("**/.git/**");
            matcher.AddExclude("**/node_modules/**");

            var fileResult = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(projectRoot)));
            if (!fileResult.HasMatches) return "没有匹配包含/排除模式的文件。";

            var regexOptions = RegexOptions.Compiled;
            if (!caseSensitive) regexOptions |= RegexOptions.IgnoreCase;
            var regex = new Regex(query, regexOptions);

            var results = new List<string>();
            const int MAX_RESULTS = 50;

            foreach (var match in fileResult.Files)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (results.Count >= MAX_RESULTS) break;

                string fullPath = Path.Combine(projectRoot, match.Path);
                
                try
                {
                    using var reader = new StreamReader(fullPath);
                    int lineNum = 0;
                    while (await reader.ReadLineAsync(cancellationToken) is string line)
                    {
                        lineNum++;
                        if (regex.IsMatch(line))
                        {
                            results.Add($"{match.Path}:{lineNum}: {line.Trim()}");
                            if (results.Count >= MAX_RESULTS) break;
                        }
                    }
                }
                catch { }
            }

            string finalResult;
            if (results.Count == 0)
            {
                finalResult = "未找到匹配项。";
            }
            else
            {
                var sb = new StringBuilder();
                sb.AppendLine($"找到 {results.Count} 处匹配:");
                foreach (var res in results) sb.AppendLine(res);
                if (results.Count >= MAX_RESULTS) sb.AppendLine("... (更多结果已截断)");
                finalResult = sb.ToString();
            }

            // 存入缓存
            _cache?.Set(cacheKey, finalResult);

            return finalResult;
        }
        catch (Exception ex)
        {
            return $"执行 codesearch 时出错: {ex.Message}";
        }
    }
}
