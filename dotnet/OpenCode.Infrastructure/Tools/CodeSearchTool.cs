using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Net.Http.Json;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using OpenCode.Core.Contracts;
using OpenCode.Core.Utilities;
using OpenCode.Core.Services;

namespace OpenCode.Infrastructure.Tools;

/// <summary>
/// 代码搜索工具，对标 TS 版的 codesearch。
/// 提供基于模式匹配的文件内容搜索。
/// </summary>
public class CodeSearchTool : ITool
{
    private readonly SearchCache? _cache;
    private readonly IProjectContext _projectContext;
    private readonly HttpClient _httpClient;

    public CodeSearchTool(IProjectContext projectContext, IHttpClientFactory httpClientFactory, SearchCache? cache = null)
    {
        _projectContext = projectContext;
        _httpClient = httpClientFactory.CreateClient();
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
        "caseSensitive": { "type": "boolean", "description": "是否区分大小写" },
        "tokensNum": { "type": "number", "description": "语义搜索返回的 token 数" }
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
        int tokensNum = args["tokensNum"]?.GetValue<int>() ?? 5000;

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
            if (Flag.EnableExa)
            {
                var exaResult = await ExecuteExaAsync(query, tokensNum, cancellationToken);
                if (!string.IsNullOrEmpty(exaResult))
                {
                    _cache?.Set(cacheKey, exaResult);
                    return exaResult;
                }
            }

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

    private async Task<string?> ExecuteExaAsync(string query, int tokensNum, CancellationToken cancellationToken)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new
            {
                name = "get_code_context_exa",
                arguments = new
                {
                    query,
                    tokensNum
                }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "https://mcp.exa.ai/mcp")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("accept", "application/json, text/event-stream");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (!line.StartsWith("data: ")) continue;
            var json = line.Substring(6);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("result", out var result) &&
                result.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.Array &&
                content.GetArrayLength() > 0)
            {
                var item = content[0];
                if (item.TryGetProperty("text", out var textProp))
                {
                    return textProp.GetString();
                }
            }
        }

        return null;
    }
}
