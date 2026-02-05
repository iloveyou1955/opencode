using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using OpenCode.Core.Contracts;
using OpenCode.Core.Services;
using OpenCode.Infrastructure.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace OpenCode.Tests.Infrastructure.Tools;

public class MockProjectContext : IProjectContext
{
    public string Directory => System.IO.Directory.GetCurrentDirectory();
    public string Worktree => Directory;
    public bool ContainsPath(string path) => path.StartsWith(Directory, StringComparison.OrdinalIgnoreCase);
    public string GetRelativePath(string path) => Path.GetRelativePath(Directory, path);
    public string ResolvePath(string path) => Path.Combine(Directory, path);
}

public class BashToolTests
{
    private readonly ShellService _shellService;

    public BashToolTests()
    {
        _shellService = new ShellService(new MockProjectContext(), NullLogger<ShellService>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRunSimpleCommand()
    {
        // Arrange
        var projectContext = new MockProjectContext();
        var tool = new BashTool(projectContext, _shellService);
        var context = new MockToolContext();
        var message = "Hello World";
        string command;
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            command = $"echo {message}";
        }
        else
        {
            command = $"echo \"{message}\"";
        }

        var args = new JsonObject
        {
            ["command"] = command,
            ["description"] = "echo test"
        };

        // Act
        var result = await tool.ExecuteAsync(args, context);

        // Assert
        Assert.Contains(message, result);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnErrorOutput()
    {
        // Arrange
        var projectContext = new MockProjectContext();
        var tool = new BashTool(projectContext, _shellService);
        var context = new MockToolContext();
        string command;
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows specific error command
            command = "type non_existent_file.txt";
        }
        else
        {
            command = "cat non_existent_file.txt";
        }

        var args = new JsonObject
        {
            ["command"] = command,
            ["description"] = "error test"
        };

        // Act
        var result = await tool.ExecuteAsync(args, context);

        // Assert
        Assert.True(result.Contains("STDERR:") || result.Contains("cannot find") || result.Contains("No such file") || result.Contains("Exit Code:"), 
            $"Result should indicate error. Actual: {result}");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRespectWorkingDirectory()
    {
        // Arrange
        var projectContext = new MockProjectContext();
        var tool = new BashTool(projectContext, _shellService);
        var context = new MockToolContext();
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        
        try 
        {
            string command;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                command = "cd"; // cmd prints current dir
            }
            else
            {
                command = "pwd";
            }

            var args = new JsonObject
            {
                ["command"] = command,
                ["workdir"] = tempDir,
                ["description"] = "pwd test"
            };

            // Act
            var result = await tool.ExecuteAsync(args, context);

            // Assert
            var normalizedResult = result.Trim().Replace("\\", "/");
            var normalizedExpected = tempDir.Trim().Replace("\\", "/");
            
            Assert.Contains(normalizedExpected, normalizedResult, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir);
        }
    }
}
