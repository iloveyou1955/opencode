using System.Text.Json.Nodes;
using OpenCode.Core.Parsing;
using Xunit;

namespace OpenCode.Tests.Core.Parsing;

public class RobustStreamingJsonParserChunkedTests
{
    private readonly RobustStreamingJsonParser _parser = new();

    /// <summary>
    /// 测试分块流式JSON解析，模拟LLM流式输出
    /// </summary>
    [Fact]
    public async Task ParseStreamAsync_ShouldHandleChunkedToolCall()
    {
        // 模拟LLM流式输出工具调用JSON
        var tokens = new[]
        {
            "```json\n{",
            "\"tool\": \"file_",
            "system\",\n",
            "\"args\": {\n",
            "\"command\": \"list_dir\",\n",
            "\"path\": \".\"\n",
            "}\n",
            "}\n",
            "```"
        };

        var stream = ToAsyncEnumerableAsync(tokens);
        var nodes = new List<JsonNode>();

        await foreach (var node in _parser.ParseStreamAsync(stream))
        {
            if (node != null) nodes.Add(node);
        }

        Assert.NotEmpty(nodes);
        var lastNode = nodes.Last();
        
        // 验证工具名称完整解析
        Assert.Equal("file_system", lastNode["tool"]?.ToString());
        Assert.Equal("list_dir", lastNode["args"]?["command"]?.ToString());
        Assert.Equal(".", lastNode["args"]?["path"]?.ToString());
    }

    /// <summary>
    /// 测试Markdown代码块内的分块JSON
    /// </summary>
    [Fact]
    public async Task ParseStreamAsync_ShouldHandleChunkedMarkdownJson()
    {
        var tokens = new[]
        {
            "让我查看文件内容：\n",
            "```json\n",
            "{\n",
            "  \"tool\": \"file_",
            "system\",\n",
            "  \"args\": {\n",
            "    \"command\": \"read_",
            "file\",\n",
            "    \"path\": \"test.txt\"\n",
            "  }\n",
            "}\n",
            "```\n",
            "文件内容已获取。"
        };

        var stream = ToAsyncEnumerableAsync(tokens);
        var nodes = new List<JsonNode>();

        await foreach (var node in _parser.ParseStreamAsync(stream))
        {
            if (node != null) nodes.Add(node);
        }

        Assert.NotEmpty(nodes);
        var lastNode = nodes.Last();
        
        Assert.Equal("file_system", lastNode["tool"]?.ToString());
        Assert.Equal("read_file", lastNode["args"]?["command"]?.ToString());
        Assert.Equal("test.txt", lastNode["args"]?["path"]?.ToString());
    }

    [Fact]
    public async Task ParseStreamAsync_ShouldHandleDeeplyNestedJson()
    {
        // 构造一个深度嵌套的 JSON
        int depth = 50;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < depth; i++) sb.Append("{\"a\":");
        sb.Append("1");
        for (int i = 0; i < depth; i++) sb.Append("}");
        
        var json = sb.ToString();
        
        // 将 JSON 分割成字符流
        var tokens = json.Select(c => c.ToString()).ToArray();
        var stream = ToAsyncEnumerableAsync(tokens);
        
        var nodes = new List<JsonNode>();
        await foreach (var node in _parser.ParseStreamAsync(stream))
        {
            if (node != null) nodes.Add(node);
        }
        
        Assert.Single(nodes);
        // 验证结构（简单验证）
        Assert.NotNull(nodes[0]);
    }

    private static async IAsyncEnumerable<string> ToAsyncEnumerableAsync(IEnumerable<string> tokens)
    {
        foreach (var token in tokens)
        {
            await Task.Yield();
            yield return token;
        }
    }
}