using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenCode.Core.Models;
using OpenCode.Core.Utilities;
using OpenCode.Core.Attributes;

namespace OpenCode.Core.Services;

/// <summary>
/// 消息部分信息，包含会话 ID 和消息 ID。
/// </summary>
public record MessagePartInfo(string SessionId, string MessageId, string Id, object Content);

/// <summary>
/// 会话分享服务，支持与 opencode.ai 云端同步。
/// </summary>
[ServiceRegistration(ServiceLifetime.Singleton)]
public class ShareService : IDisposable
{
    private readonly SessionService _sessionService;
    private readonly BusService _busService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
    private const string ApiBaseUrl = "https://api.opencode.ai";
    private readonly bool _sharingDisabled;
    private readonly Channel<SyncTask> _syncChannel;
    private readonly CancellationTokenSource _cts = new();

    public ShareService(
        SessionService sessionService,
        BusService busService,
        IHttpClientFactory httpClientFactory)
    {
        _sessionService = sessionService;
        _busService = busService;
        _httpClientFactory = httpClientFactory;
        _sharingDisabled = Environment.GetEnvironmentVariable("OPENCODE_DISABLE_SHARE") == "1";
        
        _syncChannel = Channel.CreateUnbounded<SyncTask>(new UnboundedChannelOptions
        {
            SingleReader = true
        });

        if (!_sharingDisabled)
        {
            Task.Run(() => ProcessSyncQueueAsync(_cts.Token));
        }
    }

    private record SyncTask(string Secret, string Key, object Content);

    private async Task ProcessSyncQueueAsync(CancellationToken ct)
    {
        await foreach (var task in _syncChannel.Reader.ReadAllAsync(ct))
        {
            try
            {
                await SyncToCloudInternalAsync(task.Secret, task.Key, task.Content);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ShareService] Background sync failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 初始化分享服务，订阅事件以自动同步。
    /// </summary>
    public void Initialize()
    {
        if (_sharingDisabled) return;

        _busService.Subscribe("Session.Updated", async @event =>
        {
            if (@event.Properties is SessionMetadata info)
            {
                await SyncAsync($"session/info/{info.Id}", info);
            }
        });

        _busService.Subscribe("Message.Updated", async @event =>
        {
            if (@event.Properties is MessageInfo info)
            {
                await SyncAsync($"session/message/{info.Metadata.SessionId}/{info.Id}", info);
            }
        });

        _busService.Subscribe("Message.PartUpdated", async @event =>
        {
            if (@event.Properties is MessagePartInfo part)
            {
                await SyncAsync($"session/part/{part.SessionId}/{part.MessageId}/{part.Id}", part.Content);
            }
        });
    }

    /// <summary>
    /// 创建会话分享。
    /// </summary>
    public async Task<ShareInfo> CreateShareAsync(string sessionId)
    {
        if (_sharingDisabled)
        {
            throw new InvalidOperationException("Sharing is disabled via OPENCODE_DISABLE_SHARE environment variable.");
        }

        var metadata = await _sessionService.GetMetadataAsync(sessionId);
        if (metadata == null)
        {
            throw new KeyNotFoundException($"Session {sessionId} not found.");
        }

        var messages = await _sessionService.LoadHistoryAsync(sessionId);

        var payload = new CreateSharePayload
        {
            SessionId = sessionId,
            Title = metadata.Title ?? "Untitled",
            Messages = messages
        };

        var client = _httpClientFactory.CreateClient();
        var response = await client.PostAsJsonAsync($"{ApiBaseUrl}/share_create", payload, _jsonOptions);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CreateShareResponse>(_jsonOptions);
        if (result == null)
        {
            throw new InvalidOperationException("Failed to create share: server returned empty response.");
        }

        var shareInfo = new ShareInfo
        {
            SessionId = sessionId,
            Slug = result.Url.Split('/').Last(), // 假设 URL 末尾是 Slug
            Secret = result.Secret,
            Url = result.Url,
            ShareUrl = result.Url.Replace("/share/", "/s/"),
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        await SaveShareInfoAsync(shareInfo);
        return shareInfo;
    }

    /// <summary>
    /// 获取会话分享信息。
    /// </summary>
    public async Task<ShareInfo?> GetShareAsync(string sessionId)
    {
        var sharesPath = Path.Combine(_sessionService.GetSessionDirectory(), ".shares.json");
        
        if (!File.Exists(sharesPath))
        {
            return null;
        }

        var sharesJson = await File.ReadAllTextAsync(sharesPath);
        var shares = JsonSerializer.Deserialize<Dictionary<string, ShareInfo>>(sharesJson, _jsonOptions);
        
        return shares?.GetValueOrDefault(sessionId);
    }

    /// <summary>
    /// 同步会话内容到分享。
    /// </summary>
    public async Task SyncAsync(string key, object content)
    {
        if (_sharingDisabled) return;

        var keyParts = key.Split('/');
        if (keyParts.Length < 3 || keyParts[0] != "session" || keyParts[1] == "share")
        {
            return;
        }

        var sessionId = keyParts[2];
        var share = await GetShareAsync(sessionId);
        if (share == null) return;

        await _syncChannel.Writer.WriteAsync(new SyncTask(share.Secret, key, content));
    }

    /// <summary>
    /// 同步到云端。
    /// </summary>
    private async Task SyncToCloudInternalAsync(string secret, string key, object content)
    {
        var client = _httpClientFactory.CreateClient();
        var payload = new SyncPayload
        {
            SessionId = key.Split('/')[2],
            Secret = secret,
            Key = key,
            Content = JsonSerializer.Serialize(content, _jsonOptions)
        };
        var response = await client.PostAsJsonAsync($"{ApiBaseUrl}/share_sync", payload, _jsonOptions);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// 获取分享的会话数据。
    /// </summary>
    public async Task<SharedSessionData> GetSharedSessionAsync(string slug)
    {
        var client = _httpClientFactory.CreateClient();
        var response = await client.GetAsync($"{ApiBaseUrl}/share/{slug}");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SharedSessionData>(_jsonOptions);
        if (result == null)
        {
            throw new InvalidOperationException("Failed to fetch shared session.");
        }

        return result;
    }

    /// <summary>
    /// 删除分享。
    /// </summary>
    public async Task DeleteShareAsync(string sessionId)
    {
        var shareInfo = await GetShareAsync(sessionId);
        if (shareInfo == null) return;

        var client = _httpClientFactory.CreateClient();
        var response = await client.PostAsJsonAsync($"{ApiBaseUrl}/share_delete", 
            new { sessionId, secret = shareInfo.Secret }, _jsonOptions);
        response.EnsureSuccessStatusCode();

        var sharesPath = Path.Combine(_sessionService.GetSessionDirectory(), ".shares.json");
        if (File.Exists(sharesPath))
        {
            var sharesJson = await File.ReadAllTextAsync(sharesPath);
            var shares = JsonSerializer.Deserialize<Dictionary<string, ShareInfo>>(sharesJson, _jsonOptions) ?? new();
            if (shares.Remove(sessionId))
            {
                await File.WriteAllTextAsync(sharesPath, JsonSerializer.Serialize(shares, _jsonOptions));
            }
        }
    }

    /// <summary>
    /// 保存分享信息到本地。
    /// </summary>
    private async Task SaveShareInfoAsync(ShareInfo shareInfo)
    {
        var sharesPath = Path.Combine(_sessionService.GetSessionDirectory(), ".shares.json");
        var shares = new Dictionary<string, ShareInfo>();

        if (File.Exists(sharesPath))
        {
            var sharesJson = await File.ReadAllTextAsync(sharesPath);
            shares = JsonSerializer.Deserialize<Dictionary<string, ShareInfo>>(sharesJson, _jsonOptions) ?? new();
        }

        shares[shareInfo.SessionId] = shareInfo;
        await File.WriteAllTextAsync(sharesPath, JsonSerializer.Serialize(shares, _jsonOptions));
    }

    /// <summary>
    /// 更新现有的分享。
    /// </summary>
    public async Task<ShareInfo> UpdateShareAsync(string sessionId)
    {
        if (_sharingDisabled)
        {
            throw new InvalidOperationException("Sharing is disabled via OPENCODE_DISABLE_SHARE environment variable.");
        }

        var share = await GetShareAsync(sessionId);
        if (share == null)
        {
            throw new KeyNotFoundException($"No existing share found for session {sessionId}.");
        }

        var metadata = await _sessionService.GetMetadataAsync(sessionId);
        if (metadata == null)
        {
            throw new KeyNotFoundException($"Session {sessionId} not found.");
        }

        var messages = await _sessionService.LoadHistoryAsync(sessionId);

        var payload = new CreateSharePayload
        {
            SessionId = sessionId,
            Title = metadata.Title ?? "Untitled",
            Messages = messages
        };

        var client = _httpClientFactory.CreateClient();
        // 使用 POST 到 share_update 或类似的端点。假设 API 支持覆盖。
        // 根据现有代码，CreateShareAsync 使用 share_create。
        // 如果云端支持幂等创建或有专门的 update 端点：
        var response = await client.PostAsJsonAsync($"{ApiBaseUrl}/share_update", new 
        { 
            sessionId, 
            secret = share.Secret,
            payload 
        }, _jsonOptions);
        
        response.EnsureSuccessStatusCode();

        return share;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    public record ShareInfo
    {
        [JsonPropertyName("sessionId")]
        public string SessionId { get; init; } = string.Empty;

        [JsonPropertyName("slug")]
        public string Slug { get; init; } = string.Empty;

        [JsonPropertyName("secret")]
        public string Secret { get; init; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; init; } = string.Empty;

        [JsonPropertyName("shareUrl")]
        public string ShareUrl { get; init; } = string.Empty;

        [JsonPropertyName("createdAt")]
        public long CreatedAt { get; init; }
    }

    public record SharedSessionData
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; init; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<MessageInfo> Messages { get; init; } = new();

        [JsonPropertyName("createdAt")]
        public long CreatedAt { get; init; }
    }

    private record CreateSharePayload
    {
        [JsonPropertyName("sessionId")]
        public string SessionId { get; init; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; init; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<MessageInfo> Messages { get; init; } = new();
    }

    private record CreateShareResponse
    {
        [JsonPropertyName("url")]
        public string Url { get; init; } = string.Empty;

        [JsonPropertyName("secret")]
        public string Secret { get; init; } = string.Empty;
    }

    private record SyncPayload
    {
        [JsonPropertyName("sessionId")]
        public string SessionId { get; init; } = string.Empty;

        [JsonPropertyName("secret")]
        public string Secret { get; init; } = string.Empty;

        [JsonPropertyName("key")]
        public string Key { get; init; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; init; } = string.Empty;
    }
}
