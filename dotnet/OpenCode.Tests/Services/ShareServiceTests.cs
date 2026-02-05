using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;
using Moq.Protected;
using OpenCode.Core.Models;
using OpenCode.Core.Services;
using OpenCode.Core.Utilities;
using Xunit;

namespace OpenCode.Tests.Services;

public class ShareServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SessionService _sessionService;
    private readonly Mock<BusService> _busServiceMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly ShareService _shareService;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;

    public ShareServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        
        _sessionService = new SessionService(_tempDir);
        _busServiceMock = new Mock<BusService>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();

        var client = new HttpClient(_httpMessageHandlerMock.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        _shareService = new ShareService(
            _sessionService,
            _busServiceMock.Object,
            _httpClientFactoryMock.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task UpdateShareAsync_SessionExists_ReturnsUpdatedInfo()
    {
        // Arrange
        var sessionId = "test-session";
        var metadata = new SessionMetadata(sessionId, "Test Title");
        await _sessionService.SaveMetadataAsync(sessionId, metadata);
        
        // Prepare .shares.json
        var sharesPath = Path.Combine(_tempDir, ".shares.json");
        var shareInfo = new ShareService.ShareInfo 
        { 
            SessionId = sessionId, 
            Secret = "secret",
            Url = "https://opencode.ai/share/test"
        };
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var shares = new Dictionary<string, ShareService.ShareInfo> { [sessionId] = shareInfo };
        await File.WriteAllTextAsync(sharesPath, JsonSerializer.Serialize(shares, options));

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(new { success = true })
            });

        // Act
        var result = await _shareService.UpdateShareAsync(sessionId);
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(sessionId, result.SessionId);
    }

    [Fact]
    public async Task CreateShareAsync_ValidSession_ReturnsNewShare()
    {
        // Arrange
        var sessionId = "new-session";
        var metadata = new SessionMetadata(sessionId, "New Title");
        await _sessionService.SaveMetadataAsync(sessionId, metadata);

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(m => m.RequestUri!.AbsolutePath.Contains("share_create")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(new 
                { 
                    url = "https://opencode.ai/share/new-slug", 
                    secret = "new-secret" 
                })
            });

        // Act
        var result = await _shareService.CreateShareAsync(sessionId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal("new-slug", result.Slug);
        Assert.Equal("new-secret", result.Secret);
    }

    [Fact]
    public async Task CreateShareAsync_SessionNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var sessionId = "non-existent";

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _shareService.CreateShareAsync(sessionId));
    }

    [Fact]
    public async Task Archive_ShouldMarkAsArchived()
    {
        // Arrange
        var sessionId = "archive-test";
        var metadata = new SessionMetadata(sessionId, "Archive Test");
        await _sessionService.SaveMetadataAsync(sessionId, metadata);

        // Act
        await _sessionService.ArchiveSessionAsync(sessionId);
        var updated = await _sessionService.GetMetadataAsync(sessionId);

        // Assert
        Assert.NotNull(updated);
        Assert.True(updated.IsArchived);
    }

    [Fact]
    public async Task List_ShouldExcludeArchivedByDefault()
    {
        // Arrange
        var id1 = "active";
        var id2 = "archived";
        
        // 创建 .jsonl 文件以确保 ListSessionsAsync 能够识别它们
        await File.WriteAllTextAsync(Path.Combine(_tempDir, $"{id1}.jsonl"), "");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, $"{id2}.jsonl"), "");
        
        await _sessionService.SaveMetadataAsync(id1, new SessionMetadata(id1, "Active"));
        await _sessionService.SaveMetadataAsync(id2, new SessionMetadata(id2, "Archived", IsArchived: true));

        // Act
        var list = await _sessionService.ListSessionMetadataAsync();

        // Assert
        Assert.Single(list);
        Assert.Equal(id1, list[0].Id);
    }

    [Fact]
    public async Task SyncAsync_SharingEnabled_WritesToChannel()
    {
        // Arrange
        var sessionId = "sync-session";
        var shareInfo = new ShareService.ShareInfo { SessionId = sessionId, Secret = "secret" };
        var sharesPath = Path.Combine(_tempDir, ".shares.json");
        var shares = new Dictionary<string, ShareService.ShareInfo> { [sessionId] = shareInfo };
        await File.WriteAllTextAsync(sharesPath, JsonSerializer.Serialize(shares, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(new { success = true })
            });

        // Act
        // 触发同步，由于使用了 Channel，这会是异步的后台处理
        await _shareService.SyncAsync($"session/info/{sessionId}", new { title = "updated" });

        // 等待一小段时间让后台任务处理
        await Task.Delay(100);

        // Assert
        // 验证 HttpMessageHandler 是否被调用，证明任务已从 Channel 中读取并执行
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.AtLeastOnce(),
            ItExpr.Is<HttpRequestMessage>(m => m.RequestUri!.AbsolutePath.Contains("share_sync")),
            ItExpr.IsAny<CancellationToken>());
    }
}
