using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using OpenCode.Core.Models;
using OpenCode.Core.Utilities;
using OpenCode.Core.Attributes;

namespace OpenCode.Core.Services;

/// <summary>
/// 会话 Fork 服务。
/// </summary>
[ServiceRegistration(ServiceLifetime.Singleton)]
public class ForkService
{
    private readonly SessionService _sessionService;

    public ForkService(SessionService sessionService)
    {
        _sessionService = sessionService;
    }

    /// <summary>
    /// Fork 一个会话，创建一个新的子会话。
    /// </summary>
    public async Task<string> ForkSessionAsync(string sessionId, string? fromMessageId = null)
    {
        var parentMetadata = await _sessionService.GetMetadataAsync(sessionId);
        if (parentMetadata == null)
        {
            throw new Exception($"Session {sessionId} not found.");
        }

        var newSessionId = Guid.NewGuid().ToString("N");
        var newTitle = GetForkedTitle(parentMetadata.Title ?? "Untitled", await GetForkNumberAsync(parentMetadata.Title ?? "Untitled"));
        var createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var newMetadata = new SessionMetadata(
            Id: newSessionId,
            Title: newTitle,
            AgentName: parentMetadata.AgentName,
            CreatedAt: createdAt,
            UpdatedAt: createdAt,
            ForkedFrom: sessionId,
            ForkPoint: fromMessageId
        );

        await _sessionService.SaveMetadataAsync(newSessionId, newMetadata);

        var messages = await _sessionService.LoadHistoryAsync(sessionId);
        if (fromMessageId != null)
        {
            messages = messages.TakeWhile(m => m.Id != fromMessageId).ToList();
        }

        foreach (var msg in messages)
        {
            await _sessionService.SaveMessageAsync(newSessionId, msg);
        }

        var timeline = await _sessionService.GetTimelineAsync(sessionId);
        if (fromMessageId != null)
        {
            var forkMessage = messages.FirstOrDefault(m => m.Id == fromMessageId);
            if (forkMessage != null)
            {
                var forkTime = forkMessage.Metadata.Created;
                timeline = timeline.Where(t => NormalizeTimestamp(t.Timestamp) <= forkTime).ToList();
            }
        }

        foreach (var ev in timeline)
        {
            await _sessionService.AddTimelineEventAsync(newSessionId, ev);
        }

        await _sessionService.AddTimelineEventAsync(newSessionId, new TimelineEvent(
            Id: Guid.NewGuid().ToString(),
            Type: "fork",
            MessageId: fromMessageId,
            Timestamp: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Description: $"Forked from {parentMetadata.Title ?? sessionId}",
            Metadata: new Dictionary<string, object>
            {
                ["parentSessionId"] = sessionId,
                ["parentTitle"] = parentMetadata.Title ?? string.Empty
            }
        ));

        return newSessionId;
    }

    /// <summary>
    /// 获取 Fork 链（从根会话到当前会话的路径）。
    /// </summary>
    public async Task<List<ForkChainItem>> GetForkChainAsync(string sessionId)
    {
        var chain = new List<ForkChainItem>();
        string? currentId = sessionId;

        while (currentId != null)
        {
            var metadata = await _sessionService.GetMetadataAsync(currentId);
            if (metadata == null) break;

            chain.Add(new ForkChainItem
            {
                Id = metadata.Id,
                Title = metadata.Title,
                CreatedAt = metadata.CreatedAt,
                ForkedFrom = metadata.ForkedFrom,
                ForkPoint = metadata.ForkPoint
            });

            currentId = metadata.ForkedFrom;

            if (chain.Count > 100)
            {
                break;
            }
        }

        return chain;
    }

    /// <summary>
    /// 获取父会话信息。
    /// </summary>
    public async Task<SessionMetadata?> GetParentAsync(string sessionId)
    {
        var metadata = await _sessionService.GetMetadataAsync(sessionId);
        if (string.IsNullOrEmpty(metadata?.ForkedFrom)) return null;
        return await _sessionService.GetMetadataAsync(metadata.ForkedFrom);
    }

    /// <summary>
    /// 获取所有子会话列表。
    /// </summary>
    public async Task<List<SessionMetadata>> GetChildrenAsync(string sessionId)
    {
        var allSessions = await _sessionService.ListSessionMetadataAsync();
        return allSessions.Where(s => s.ForkedFrom == sessionId).ToList();
    }

    /// <summary>
    /// 获取指定标题的 Fork 编号。
    /// </summary>
    private async Task<int> GetForkNumberAsync(string baseTitle)
    {
        var allSessions = await _sessionService.ListSessionMetadataAsync();
        var pattern = new Regex($@"^{Regex.Escape(baseTitle)} \(fork #(\d+)\)$", RegexOptions.IgnoreCase);

        var maxForkNumber = 0;
        foreach (var session in allSessions)
        {
            if (session.Title != null)
            {
                var match = pattern.Match(session.Title);
                if (match.Success)
                {
                    if (int.TryParse(match.Groups[1].Value, out var forkNumber))
                    {
                        maxForkNumber = Math.Max(maxForkNumber, forkNumber);
                    }
                }
            }
        }

        return maxForkNumber + 1;
    }

    /// <summary>
    /// 生成 Fork 标题。
    /// </summary>
    public static string GetForkedTitle(string originalTitle, int forkNumber)
    {
        return $"{originalTitle} (fork #{forkNumber})";
    }

    private static long NormalizeTimestamp(long timestamp)
    {
        const long threshold = 1_000_000_000_000;
        return timestamp > threshold ? timestamp / 1000 : timestamp;
    }

    public record ForkChainItem
    {
        public string Id { get; init; } = string.Empty;
        public string? Title { get; init; }
        public long CreatedAt { get; init; }
        public string? ForkedFrom { get; init; }
        public string? ForkPoint { get; init; }
    }
}
