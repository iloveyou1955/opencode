using Moq;
using OpenCode.Core.Models;
using OpenCode.Core.Services;
using OpenCode.Core.Utilities;
using Xunit;

namespace OpenCode.Tests.Services;

public class ForkServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SessionService _sessionService;
    private readonly ForkService _forkService;

    public ForkServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        
        _sessionService = new SessionService(_tempDir);
        _forkService = new ForkService(_sessionService);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task ForkSessionAsync_FullHistory_CreatesNewSessionWithAllMessages()
    {
        // Arrange
        var parentId = "parent";
        var parentMeta = new SessionMetadata(parentId, "Original Title");
        await _sessionService.SaveMetadataAsync(parentId, parentMeta);
        
        var msg1 = new MessageInfo("msg1", "user", [new TextPart("Hello")], new MessageMetadata(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), parentId));
        var msg2 = new MessageInfo("msg2", "assistant", [new TextPart("Hi")], new MessageMetadata(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), parentId));
        await _sessionService.SaveMessageAsync(parentId, msg1);
        await _sessionService.SaveMessageAsync(parentId, msg2);

        // Act
        var newSessionId = await _forkService.ForkSessionAsync(parentId);

        // Assert
        Assert.NotNull(newSessionId);
        Assert.NotEqual(parentId, newSessionId);

        var newMeta = await _sessionService.GetMetadataAsync(newSessionId);
        Assert.NotNull(newMeta);
        Assert.Equal("Original Title (fork #1)", newMeta.Title);
        Assert.Equal(parentId, newMeta.ForkedFrom);

        var history = await _sessionService.LoadHistoryAsync(newSessionId);
        Assert.Equal(2, history.Count);
        Assert.Equal("Hello", ((TextPart)history[0].Parts[0]).Text);
        Assert.Equal("Hi", ((TextPart)history[1].Parts[0]).Text);
    }

    [Fact]
    public async Task ForkSessionAsync_PartialHistory_CreatesNewSessionWithSubsetOfMessages()
    {
        // Arrange
        var parentId = "parent";
        var parentMeta = new SessionMetadata(parentId, "Original Title");
        await _sessionService.SaveMetadataAsync(parentId, parentMeta);
        
        var msg1 = new MessageInfo("msg1", "user", [new TextPart("Msg 1")], new MessageMetadata(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), parentId));
        var msg2 = new MessageInfo("msg2", "assistant", [new TextPart("Msg 2")], new MessageMetadata(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), parentId));
        var msg3 = new MessageInfo("msg3", "user", [new TextPart("Msg 3")], new MessageMetadata(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), parentId));
        await _sessionService.SaveMessageAsync(parentId, msg1);
        await _sessionService.SaveMessageAsync(parentId, msg2);
        await _sessionService.SaveMessageAsync(parentId, msg3);

        // Act - Fork from msg2 (so only msg1 should be included if TakeWhile is used)
        // Wait, looking at ForkService.cs: messages = messages.TakeWhile(m => m.Id != fromMessageId).ToList();
        // So msg1 is included, msg2 is the fork point (excluded).
        var newSessionId = await _forkService.ForkSessionAsync(parentId, "msg2");

        // Assert
        var history = await _sessionService.LoadHistoryAsync(newSessionId);
        Assert.Single(history);
        Assert.Equal("Msg 1", ((TextPart)history[0].Parts[0]).Text);
        
        var newMeta = await _sessionService.GetMetadataAsync(newSessionId);
        Assert.Equal("msg2", newMeta?.ForkPoint);
    }

    [Fact]
    public async Task GetForkChainAsync_MultipleForks_ReturnsCorrectChain()
    {
        // Arrange
        var rootId = "root";
        await _sessionService.SaveMetadataAsync(rootId, new SessionMetadata(rootId, "Root"));
        
        var fork1Id = await _forkService.ForkSessionAsync(rootId);
        var fork2Id = await _forkService.ForkSessionAsync(fork1Id);

        // Act
        var chain = await _forkService.GetForkChainAsync(fork2Id);

        // Assert
        Assert.Equal(3, chain.Count);
        Assert.Equal(fork2Id, chain[0].Id);
        Assert.Equal(fork1Id, chain[1].Id);
        Assert.Equal(rootId, chain[2].Id);
    }
}
