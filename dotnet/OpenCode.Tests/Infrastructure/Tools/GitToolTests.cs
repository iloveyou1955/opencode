using System.Text.Json.Nodes;
using OpenCode.Infrastructure.Tools;

namespace OpenCode.Tests.Infrastructure.Tools;

public class GitToolTests : IDisposable
{
    private readonly string _testRepo;

    public GitToolTests()
    {
        _testRepo = Path.Combine(Path.GetTempPath(), "OpenCodeGitTest_" + Guid.NewGuid());
        Directory.CreateDirectory(_testRepo);
    }

    public void Dispose()
    {
        // Try to delete repo, might fail if git processes are holding locks
        try 
        {
            if (Directory.Exists(_testRepo)) 
            {
                // Remove read-only attributes
                var dir = new DirectoryInfo(_testRepo);
                foreach (var file in dir.GetFiles("*", SearchOption.AllDirectories)) file.Attributes = FileAttributes.Normal;
                Directory.Delete(_testRepo, true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task GitInit_ShouldInitializeRepo()
    {
        // Arrange
        var tool = new GitTool();
        var context = new MockToolContext();
        var args = new JsonObject 
        { 
            ["command"] = "init",
            ["working_directory"] = _testRepo
        };
        
        // Act
        var result = await tool.ExecuteAsync(args, context);

        // Assert
        Assert.True(Directory.Exists(Path.Combine(_testRepo, ".git")));
    }
}
