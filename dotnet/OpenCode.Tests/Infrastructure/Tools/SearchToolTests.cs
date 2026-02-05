using System.Text.Json.Nodes;
using OpenCode.Infrastructure.Tools;

namespace OpenCode.Tests.Infrastructure.Tools;

public class SearchToolTests : IDisposable
{
    private readonly string _testRoot;

    public SearchToolTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "OpenCodeSearchTest_" + Guid.NewGuid());
        Directory.CreateDirectory(_testRoot);
        
        // Setup test files
        File.WriteAllText(Path.Combine(_testRoot, "file1.txt"), "Hello World\nLine 2");
        File.WriteAllText(Path.Combine(_testRoot, "file2.cs"), "public class Test { }");
        
        var subDir = Path.Combine(_testRoot, "subdir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "file3.txt"), "Another World");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot)) Directory.Delete(_testRoot, true);
    }

    [Fact]
    public async Task Glob_ShouldReturnMatchingFiles()
    {
        // Arrange
        var tool = new SearchTool();
        var context = new MockToolContext();
        var args = new JsonObject
        {
            ["command"] = "glob",
            ["path"] = _testRoot,
            ["pattern"] = "**/*.txt"
        };

        // Act
        var result = await tool.ExecuteAsync(args, context);

        // Assert
        Assert.Contains("file1.txt", result);
        Assert.Contains("file3.txt", result);
        Assert.DoesNotContain("file2.cs", result);
    }

    [Fact]
    public async Task Grep_ShouldReturnMatchingLines()
    {
        // Arrange
        var tool = new SearchTool();
        var context = new MockToolContext();
        var args = new JsonObject
        {
            ["command"] = "grep",
            ["path"] = _testRoot,
            ["pattern"] = "World"
        };

        // Act
        var result = await tool.ExecuteAsync(args, context);

        // Assert
        Assert.Contains("file1.txt:1: Hello World", result);
        Assert.Contains("file3.txt:1: Another World", result);
    }

    [Fact]
    public async Task Grep_ShouldRespectIncludePattern()
    {
        // Arrange
        var tool = new SearchTool();
        var context = new MockToolContext();
        var args = new JsonObject
        {
            ["command"] = "grep",
            ["path"] = _testRoot,
            ["pattern"] = "World",
            ["include"] = "subdir/*.txt" // Only search in subdir
        };

        // Act
        var result = await tool.ExecuteAsync(args, context);

        // Assert
        Assert.Contains("file3.txt", result);
        Assert.DoesNotContain("file1.txt", result);
    }
}
