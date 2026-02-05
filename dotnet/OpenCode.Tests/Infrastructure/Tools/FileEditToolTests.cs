using OpenCode.Infrastructure.Tools;
using OpenCode.Core.Services;
using System.Text.Json.Nodes;

namespace OpenCode.Tests.Infrastructure.Tools;

public class EditToolTests
{
    private readonly BusService _bus = new();

    [Fact]
    public async Task WriteFile_ShouldCreateNewFile()
    {
        // Arrange
        var tool = new WriteTool(_bus);
        var context = new MockToolContext();
        var tempFile = Path.GetTempFileName();
        File.Delete(tempFile); // Ensure it doesn't exist
        var content = "Hello World";

        var args = new JsonObject
        {
            ["filePath"] = tempFile,
            ["content"] = content
        };

        try
        {
            // Act
            var result = await tool.ExecuteAsync(args, context);

            // Assert
            Assert.Contains("successfully", result);
            Assert.True(File.Exists(tempFile));
            Assert.Equal(content, await File.ReadAllTextAsync(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task EditFile_ShouldReplaceContent_ExactMatch()
    {
        // Arrange
        var tool = new EditTool(_bus);
        var context = new MockToolContext();
        var tempFile = Path.GetTempFileName();
        var originalContent = "Line 1\nLine 2\nLine 3";
        await File.WriteAllTextAsync(tempFile, originalContent);

        var args = new JsonObject
        {
            ["filePath"] = tempFile,
            ["oldString"] = "Line 2",
            ["newString"] = "Line Two"
        };

        try
        {
            // Act
            var result = await tool.ExecuteAsync(args, context);

            // Assert
            Assert.Contains("Edit applied successfully", result);
            var newContent = await File.ReadAllTextAsync(tempFile);
            Assert.Contains("Line Two", newContent);
            Assert.DoesNotContain("Line 2", newContent);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task EditFile_ShouldReplaceContent_LineTrimmedMatch()
    {
        // Arrange
        var tool = new EditTool(_bus);
        var context = new MockToolContext();
        var tempFile = Path.GetTempFileName();
        var originalContent = "    Line 1\n    Line 2\n    Line 3";
        await File.WriteAllTextAsync(tempFile, originalContent);

        // Intentionally missing indentation in oldString
        var args = new JsonObject
        {
            ["filePath"] = tempFile,
            ["oldString"] = "Line 2", 
            ["newString"] = "    Line Two"
        };

        try
        {
            // Act
            var result = await tool.ExecuteAsync(args, context);

            // Assert
            Assert.Contains("Edit applied successfully", result);
            var newContent = await File.ReadAllTextAsync(tempFile);
            Assert.Contains("    Line Two", newContent);
            Assert.DoesNotContain("    Line 2", newContent);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task EditFile_ShouldFail_WhenOldContentNotFound()
    {
        // Arrange
        var tool = new EditTool(_bus);
        var context = new MockToolContext();
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "Content");

        var args = new JsonObject
        {
            ["filePath"] = tempFile,
            ["oldString"] = "NonExistent",
            ["newString"] = "New"
        };

        try
        {
            // Act
            var result = await tool.ExecuteAsync(args, context);

            // Assert
            Assert.Contains("Error", result);
            Assert.Contains("not found", result);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
