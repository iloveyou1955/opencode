using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OpenCode.Core.Contracts;

namespace OpenCode.Infrastructure.Tools;

/// <summary>
/// 获取指定 URL 内容的工具。
/// </summary>
public class WebFetchTool : ITool
{
    private readonly HttpClient _httpClient;

    public WebFetchTool(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    public string Name => "webfetch";
    public string Description => "从指定的 URL 获取内容。支持 markdown、text 和 html 格式。";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {
        "url": { "type": "string", "description": "要获取内容的 URL" },
        "format": { "type": "string", "enum": ["text", "markdown", "html"], "default": "markdown", "description": "返回内容的格式" }
      },
      "required": ["url"]
    }
    """;

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken ct = default)
    {
        string url = args["url"]?.ToString() ?? "";
        string format = args["format"]?.ToString() ?? "markdown";

        if (string.IsNullOrEmpty(url)) return "错误: url 是必需的";
        if (!url.StartsWith("http://") && !url.StartsWith("https://")) return "错误: URL 必须以 http:// 或 https:// 开头";

        if (!await context.RequestPermissionAsync("webfetch", url))
        {
            return $"错误: 获取 {url} 内容的权限被拒绝";
        }

        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            string content = await response.Content.ReadAsStringAsync(ct);
            string contentType = response.Content.Headers.ContentType?.MediaType ?? "text/plain";

            if (format == "html") return content;

            // 简单的 HTML 到 文本/Markdown 转换 (如果需要更复杂的可以使用库)
            if (contentType.Contains("text/html"))
            {
                return ConvertHtmlToText(content, format == "markdown");
            }

            return content;
        }
        catch (Exception ex)
        {
            return $"获取 URL 内容时出错: {ex.Message}";
        }
    }

    private string ConvertHtmlToText(string html, bool asMarkdown)
    {
        // 移除 script 和 style
        html = Regex.Replace(html, "<script.*?>.*?</script>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "<style.*?>.*?</style>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        if (asMarkdown)
        {
            // 极其简单的 Markdown 转换逻辑 (示例)
            html = Regex.Replace(html, "<h[1-6].*?>(.*?)</h[1-6]>", m => $"\n# {m.Groups[1].Value}\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            html = Regex.Replace(html, "<p.*?>(.*?)</p>", m => $"\n{m.Groups[1].Value}\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            html = Regex.Replace(html, "<a.*?href=\"(.*?)\".*?>(.*?)</a>", m => $"[{m.Groups[2].Value}]({m.Groups[1].Value})", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        }

        // 移除所有其他标签
        string text = Regex.Replace(html, "<.*?>", "", RegexOptions.Singleline);
        
        // 解码 HTML 实体
        text = System.Net.WebUtility.HtmlDecode(text);
        
        // 清理空白
        return Regex.Replace(text, @"\n\s*\n", "\n\n").Trim();
    }
}
