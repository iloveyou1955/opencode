using System.Text.Json.Nodes;
using OpenCode.Core.Parsing;
using Xunit;

namespace OpenCode.Tests.Core.Parsing;

public class RobustStreamingJsonParserTests
{
    private readonly RobustStreamingJsonParser _parser = new();

    [Fact]
    public void FixJson_ShouldFixMissingBraces()
    {
        string input = "{\"key\": \"value\"";
        string expected = "{\"key\": \"value\"}";
        string actual = RobustStreamingJsonParser.FixJson(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FixJson_ShouldFixMissingQuotesAndBraces()
    {
        string input = "{\"key\": \"val";
        string expected = "{\"key\": \"val\"}";
        string actual = RobustStreamingJsonParser.FixJson(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FixJson_ShouldFixNestedStructures()
    {
        string input = "{\"a\": [1, 2, {\"b\": 3";
        string expected = "{\"a\": [1, 2, {\"b\": 3}]}";
        string actual = RobustStreamingJsonParser.FixJson(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ParseStreamAsync_ShouldHandleMarkdownBlocks()
    {
        var tokens = new[]
        {
            "Here is the json:\n",
            "```json\n",
            "{\n",
            "  \"tool\": \"read_file\",\n",
            "  \"args\": {\n",
            "    \"path\": \"test.txt\"\n",
            "  }\n",
            "}\n",
            "```\n"
        };

        var stream = ToAsyncEnumerableAsync(tokens);
        var nodes = new List<JsonNode>();

        await foreach (var node in _parser.ParseStreamAsync(stream))
        {
            if (node != null) nodes.Add(node);
        }

        Assert.NotEmpty(nodes);
        var lastNode = nodes.Last();
        Assert.Equal("read_file", lastNode["tool"]?.ToString());
        Assert.Equal("test.txt", lastNode["args"]?["path"]?.ToString());
    }

    [Fact]
    public async Task ParseStreamAsync_ShouldHandleIncompleteStream()
    {
        var tokens = new[]
        {
            "{\"tool\": \"",
            "read_",
            "file\"}"
        };

        var stream = ToAsyncEnumerableAsync(tokens);
        var nodes = new List<JsonNode>();

        await foreach (var node in _parser.ParseStreamAsync(stream))
        {
            if (node != null) nodes.Add(node);
        }

        // 我们期望在流的过程中至少能解析出部分结果，或者在最后解析出完整结果
        Assert.NotEmpty(nodes);
        var lastNode = nodes.Last();
        Assert.Equal("read_file", lastNode["tool"]?.ToString());
    }

    [Fact]
    public void FixJson_ShouldHandleDeeplyNestedObjects()
    {
        string input = "{\"l1\": {\"l2\": {\"l3\": {\"l4\": \"val";
        string expected = "{\"l1\": {\"l2\": {\"l3\": {\"l4\": \"val\"}}}}";
        string actual = RobustStreamingJsonParser.FixJson(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FixJson_ShouldHandleDeeplyNestedArrays()
    {
        string input = "{\"arr\": [[[[1, 2, 3";
        string expected = "{\"arr\": [[[[1, 2, 3]]]]}";
        string actual = RobustStreamingJsonParser.FixJson(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FixJson_ShouldHandleMixedComplexStructure()
    {
        string input = "{\"data\": [{\"id\": 1, \"info\": {\"tags\": [\"a\", \"b";
        string expected = "{\"data\": [{\"id\": 1, \"info\": {\"tags\": [\"a\", \"b\"]}}]}";
        string actual = RobustStreamingJsonParser.FixJson(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ParseStreamAsync_ShouldHandleComplexIncompleteStream()
    {
        var tokens = new[]
        {
            "{\"tool\": \"complex_tool\", ",
            "\"args\": {\"query\": \"SELECT * FROM ",
            "users WHERE id IN (1, 2, 3)\", \"fil",
            "ters\": [{\"field\": \"age\", \"op\": \">\", \"val\": 18",
            "}]}}"
        };

        var stream = ToAsyncEnumerableAsync(tokens);
        var nodes = new List<JsonNode>();

        await foreach (var node in _parser.ParseStreamAsync(stream))
        {
            if (node != null) nodes.Add(node);
        }

        Assert.NotEmpty(nodes);
        var lastNode = nodes.Last();
        Assert.Equal("complex_tool", lastNode["tool"]?.ToString());
        Assert.Equal("age", lastNode["args"]?["filters"]?[0]?["field"]?.ToString());
        Assert.Equal(18, lastNode["args"]?["filters"]?[0]?["val"]?.GetValue<int>());
    }

    [Fact]
    public void FixJson_ShouldHandleExtremeNesting()
    {
        int depth = 50;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < depth; i++) sb.Append("{\"a\": ");
        sb.Append("\"val\"");
        
        string input = sb.ToString();
        // 期望补全 50 个 }
        string expectedSuffix = new string('}', depth);
        string expected = input + expectedSuffix;
        
        string actual = RobustStreamingJsonParser.FixJson(input);
        Assert.Equal(expected, actual);
        
        // 确保修复后的 JSON 是有效的
        var node = JsonNode.Parse(actual);
        Assert.NotNull(node);
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