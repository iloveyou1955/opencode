using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCode.Core.Contracts;

namespace OpenCode.Infrastructure.Tools;

/// <summary>
/// 网页搜索工具，基于 Exa MCP 实现。
/// </summary>
public class WebSearchTool : ITool
{
    public string Name => "websearch";
    public string Description => "Search the web for information using a query. Provides high-quality, LLM-optimized results.";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {
        "query": { "type": "string", "description": "Websearch query" },
        "numResults": { "type": "number", "description": "Number of search results to return (default: 8)" },
        "livecrawl": { 
          "type": "string", 
          "enum": ["fallback", "preferred"],
          "description": "Live crawl mode (default: fallback)"
        },
        "type": {
          "type": "string",
          "enum": ["auto", "fast", "deep"],
          "description": "Search type (default: auto)"
        }
      },
      "required": ["query"]
    }
    """;

    private static readonly HttpClient _httpClient = new HttpClient();
    private const string SEARCH_URL = "https://mcp.exa.ai/mcp";

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken cancellationToken = default)
    {
        string query = args["query"]?.ToString() ?? "";
        int numResults = args["numResults"]?.GetValue<int>() ?? 8;
        string livecrawl = args["livecrawl"]?.ToString() ?? "fallback";
        string searchType = args["type"]?.ToString() ?? "auto";

        if (string.IsNullOrEmpty(query)) return "Error: query is required";

        // 权限校验
        if (!await context.RequestPermissionAsync("websearch", query))
        {
            return $"Error: Permission denied for websearch on query: {query}";
        }

        var searchRequest = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new
            {
                name = "web_search_exa",
                arguments = new
                {
                    query = query,
                    type = searchType,
                    numResults = numResults,
                    livecrawl = livecrawl
                }
            }
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(25));

        try
        {
            var response = await _httpClient.PostAsJsonAsync(SEARCH_URL, searchRequest, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cts.Token);
                return $"Error: Search failed with status {response.StatusCode}. {errorBody}";
            }

            var responseBody = await response.Content.ReadAsStringAsync(cts.Token);
            
            // Exa returns SSE-like format sometimes, but often it's just a JSON with 'data: ' prefix if it's SSE
            // The TS version parses SSE. Let's handle both.
            if (responseBody.StartsWith("data: "))
            {
                var json = responseBody.Substring(6).Trim();
                var data = JsonSerializer.Deserialize<JsonElement>(json);
                return ExtractText(data);
            }
            else
            {
                var data = JsonSerializer.Deserialize<JsonElement>(responseBody);
                return ExtractText(data);
            }
        }
        catch (OperationCanceledException)
        {
            return "Error: Search request timed out.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private string ExtractText(JsonElement data)
    {
        try
        {
            if (data.TryGetProperty("result", out var result) && 
                result.TryGetProperty("content", out var contentArray) && 
                contentArray.ValueKind == JsonValueKind.Array && 
                contentArray.GetArrayLength() > 0)
            {
                return contentArray[0].GetProperty("text").GetString() ?? "No content found.";
            }
        }
        catch { }
        return "No search results found or failed to parse response.";
    }
}
