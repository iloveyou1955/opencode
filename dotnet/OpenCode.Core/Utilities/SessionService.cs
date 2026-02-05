using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCode.Core.Models;
using OpenCode.Core.Services;

namespace OpenCode.Core.Utilities;

public class SessionService
{
    private readonly string _sessionDir;
    private readonly SnapshotService? _snapshotService;
    private readonly BusService? _busService;
    private readonly VcsService? _vcsService;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public SessionService(string sessionDir, SnapshotService? snapshotService = null, BusService? busService = null, VcsService? vcsService = null)
    {
        _sessionDir = sessionDir;
        _snapshotService = snapshotService;
        _busService = busService;
        _vcsService = vcsService;
        if (!Directory.Exists(_sessionDir)) Directory.CreateDirectory(_sessionDir);
    }

    public string GetSessionDirectory() => _sessionDir;

    public async Task SaveMetadataAsync(string sessionId, SessionMetadata metadata)
    {
        var path = Path.Combine(_sessionDir, $"{sessionId}.meta.json");
        var tempPath = path + ".tmp";
        
        // 增强原子性：先写临时文件再重命名
        var json = JsonSerializer.Serialize(metadata, Options);
        await File.WriteAllTextAsync(tempPath, json);
        
        if (File.Exists(path)) File.Delete(path);
        File.Move(tempPath, path);

        // 发布事件通知
        _busService?.Publish("Session.Updated", metadata);
    }

    public async Task<SessionMetadata?> GetMetadataAsync(string sessionId)
    {
        var path = Path.Combine(_sessionDir, $"{sessionId}.meta.json");
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<SessionMetadata>(json, Options);
    }

    public async Task AddTimelineEventAsync(string sessionId, TimelineEvent @event)
    {
        var path = Path.Combine(_sessionDir, $"{sessionId}.timeline.jsonl");
        var json = JsonSerializer.Serialize(@event, Options);
        await File.AppendAllTextAsync(path, json + "\n");
    }

    public async Task<List<TimelineEvent>> GetTimelineAsync(string sessionId)
    {
        var path = Path.Combine(_sessionDir, $"{sessionId}.timeline.jsonl");
        if (!File.Exists(path)) return new List<TimelineEvent>();

        var timeline = new List<TimelineEvent>();
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var e = JsonSerializer.Deserialize<TimelineEvent>(line, Options);
            if (e != null) timeline.Add(e);
        }
        return timeline;
    }

    public async Task SaveMessageAsync(string sessionId, MessageInfo message)
    {
        var path = Path.Combine(_sessionDir, $"{sessionId}.jsonl");
        var json = JsonSerializer.Serialize(message, Options);
        await File.AppendAllTextAsync(path, json + "\n");

        // 发布事件通知
        _busService?.Publish("Message.Updated", message);
    }

    public async Task<List<MessageInfo>> LoadHistoryAsync(string sessionId)
    {
        var path = Path.Combine(_sessionDir, $"{sessionId}.jsonl");
        if (!File.Exists(path)) return new List<MessageInfo>();

        var history = new List<MessageInfo>();
        var meta = await GetMetadataAsync(sessionId);
        var timeline = await GetTimelineAsync(sessionId);
        
        // 确定截止时间点（非破坏性回滚逻辑）
        long? cutoffTime = null;
        if (meta?.VersionPointer != null)
        {
            // 特殊处理：如果 VersionPointer 为空字符串，表示回滚到了会话最开始，不加载任何消息
            if (string.IsNullOrEmpty(meta.VersionPointer))
            {
                return history;
            }

            var targetEvent = timeline.FirstOrDefault(e => e.Id == meta.VersionPointer);
            if (targetEvent != null)
            {
                cutoffTime = targetEvent.Timestamp;
            }
        }

        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);
        
        while (await reader.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var msg = JsonSerializer.Deserialize<MessageInfo>(line, Options);
            if (msg != null)
            {
                // 如果设置了版本指针，则只加载该时间点之前的消息
                if (cutoffTime.HasValue && msg.Metadata.Created > cutoffTime.Value / 1000)
                {
                    continue;
                }
                history.Add(msg);
            }
        }
        
        return history;
    }

    public async Task<string> ForkAsync(string sessionId, string eventId, string? newTitle = null)
    {
        var timeline = await GetTimelineAsync(sessionId);
        var index = timeline.FindIndex(e => e.Id == eventId);
        if (index == -1) throw new ArgumentException("Event not found in timeline");

        var newSessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
        var oldMetadata = await GetMetadataAsync(sessionId);
        
        var newMetadata = new SessionMetadata(
            Id: newSessionId,
            Title: newTitle ?? (oldMetadata?.Title != null ? $"Fork of {oldMetadata.Title}" : "New Fork"),
            AgentName: oldMetadata?.AgentName,
            CreatedAt: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            UpdatedAt: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ForkedFrom: sessionId,
            ForkPoint: eventId
        );
        await SaveMetadataAsync(newSessionId, newMetadata);

        // Copy history up to the fork point
        var messages = await LoadHistoryAsync(sessionId);
        var forkEvent = timeline[index];
        var forkTime = forkEvent.Timestamp;

        foreach (var msg in messages)
        {
            if (msg.Metadata.Created <= forkTime)
            {
                await SaveMessageAsync(newSessionId, msg);
            }
        }

        // Copy timeline up to fork point
        for (int i = 0; i <= index; i++)
        {
            await AddTimelineEventAsync(newSessionId, timeline[i]);
        }

        return newSessionId;
    }

    public async Task RevertAsync(string sessionId, string eventId, bool force = false)
    {
        // 0. 脏检查：如果未强制回滚，且 VCS 检测到未提交的修改，则抛出异常
        if (!force && _vcsService != null)
        {
            if (await _vcsService.IsDirtyAsync())
            {
                throw new InvalidOperationException("当前工作区有未提交的修改，请先 commit 或 stash 您的修改，或使用 --force 强制回滚。");
            }
        }

        var timeline = await GetTimelineAsync(sessionId);
        
        TimelineEvent? targetEvent = null;
        if (!string.IsNullOrEmpty(eventId))
        {
            var index = timeline.FindIndex(e => e.Id == eventId);
            if (index == -1) throw new ArgumentException("Event not found in timeline");
            targetEvent = timeline[index];
        }

        // 1. 物理代码回滚 (如果 SnapshotService 可用且事件有快照)
        if (_snapshotService != null && !string.IsNullOrEmpty(targetEvent?.SnapshotHash))
        {
            await _snapshotService.RestoreAsync(targetEvent!.SnapshotHash);
        }

        // 2. 备份当前历史文件，用于 Unrevert
        var historyPath = Path.Combine(_sessionDir, $"{sessionId}.jsonl");
        var timelinePath = Path.Combine(_sessionDir, $"{sessionId}.timeline.jsonl");
        var backupSuffix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        
        if (File.Exists(historyPath))
            File.Copy(historyPath, historyPath + "." + backupSuffix + ".bak");
        if (File.Exists(timelinePath))
            File.Copy(timelinePath, timelinePath + "." + backupSuffix + ".bak");

        // 2. Revert messages
        var messages = await LoadHistoryAsync(sessionId);
        
        // 获取回滚前的最新快照哈希，用于 Unrevert 恢复
        string? currentSnapshotHash = null;
        if (_snapshotService != null)
        {
            currentSnapshotHash = await _snapshotService.TrackAsync();
        }

        // 创建 revert 事件
        var revertMetadata = new Dictionary<string, object>
        {
            { "revertedFromEventId", timeline.Last().Id },
            { "targetEventId", eventId },
            { "backupSuffix", backupSuffix }
        };

        if (!string.IsNullOrEmpty(currentSnapshotHash))
        {
            revertMetadata["revertedFromSnapshotHash"] = currentSnapshotHash;
        }

        // 记录 Diff 信息
        if (_snapshotService != null && targetEvent != null && !string.IsNullOrEmpty(targetEvent.SnapshotHash) && !string.IsNullOrEmpty(currentSnapshotHash))
        {
            var diffs = await _snapshotService.DiffFullAsync(targetEvent.SnapshotHash, currentSnapshotHash);
            if (diffs.Any())
            {
                revertMetadata["diffSummary"] = new
                {
                    FilesChanged = diffs.Count,
                    Additions = diffs.Sum(d => d.Additions),
                    Deletions = diffs.Sum(d => d.Deletions)
                };
            }
        }

        var revertEvent = new TimelineEvent(
            Id: "revert_" + Guid.NewGuid().ToString("N")[..8],
            Type: "revert",
            Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Description: $"Reverted to {eventId}",
            Metadata: revertMetadata
        );

        // 3. 非破坏性回滚：仅更新版本指针，不再截断历史文件
        var meta = await GetMetadataAsync(sessionId);
        if (meta != null)
        {
            await SaveMetadataAsync(sessionId, meta with 
            { 
                VersionPointer = eventId, 
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() 
            });
        }

        // 添加回滚事件到 Timeline
        await AddTimelineEventAsync(sessionId, revertEvent);
    }

    public async Task UnrevertAsync(string sessionId)
    {
        var timeline = await GetTimelineAsync(sessionId);
        var lastRevert = timeline.FindLast(e => e.Type == "revert");
        if (lastRevert == null) throw new InvalidOperationException("No revert event found to undo");

        if (lastRevert.Metadata != null && lastRevert.Metadata.TryGetValue("targetEventId", out var targetIdObj))
        {
            // 1. 物理代码恢复 (如果 SnapshotService 可用且有记录 revertedFromSnapshotHash)
            if (_snapshotService != null && lastRevert.Metadata.TryGetValue("revertedFromSnapshotHash", out var hashObj))
            {
                await _snapshotService.RestoreAsync(hashObj.ToString()!);
            }

            // 2. 非破坏性恢复：重置版本指针
            var meta = await GetMetadataAsync(sessionId);
            if (meta != null)
            {
                await SaveMetadataAsync(sessionId, meta with 
                { 
                    VersionPointer = null, // 清除指针，恢复到最新状态
                    UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() 
                });
            }

            // 添加 unrevert 事件
            var unrevertEvent = new TimelineEvent(
                Id: "unrevert_" + Guid.NewGuid().ToString("N")[..8],
                Type: "unrevert",
                Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Description: $"Undid revert: {lastRevert.Description}",
                Metadata: new Dictionary<string, object> { { "originalRevertId", lastRevert.Id } }
            );
            await AddTimelineEventAsync(sessionId, unrevertEvent);
        }
    }

    public async Task<string> CreateSessionAsync(string title, string? agentName = null)
    {
        var sessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
        var metadata = new SessionMetadata(
            Id: sessionId,
            Title: title,
            AgentName: agentName,
            CreatedAt: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            UpdatedAt: DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        );
        await SaveMetadataAsync(sessionId, metadata);
        return sessionId;
    }

    public async Task<List<string>> ListSessionsAsync()
    {
        if (!Directory.Exists(_sessionDir)) return new List<string>();
        var files = Directory.GetFiles(_sessionDir, "*.jsonl");
        return await Task.FromResult(files
            .Where(f => !f.EndsWith(".timeline.jsonl") && !f.EndsWith(".meta.json"))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(x => x != null)
            .Cast<string>()
            .ToList());
    }

    public async Task ArchiveSessionAsync(string sessionId)
    {
        var meta = await GetMetadataAsync(sessionId);
        if (meta != null && !meta.IsArchived)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var updatedMeta = meta with 
            { 
                IsArchived = true, 
                UpdatedAt = now,
                ArchivedAt = now
            };
            await SaveMetadataAsync(sessionId, updatedMeta);
        }
    }

    public async Task UnarchiveSessionAsync(string sessionId)
    {
        var meta = await GetMetadataAsync(sessionId);
        if (meta != null && meta.IsArchived)
        {
            var updatedMeta = meta with 
            { 
                IsArchived = false, 
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ArchivedAt = null
            };
            await SaveMetadataAsync(sessionId, updatedMeta);
        }
    }

    public async Task RenameSessionAsync(string sessionId, string newTitle)
    {
        var meta = await GetMetadataAsync(sessionId);
        if (meta != null)
        {
            var updatedMeta = meta with { Title = newTitle, UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            await SaveMetadataAsync(sessionId, updatedMeta);
        }
    }

    /// <summary>
    /// Deletes a session and all its associated data files.
    /// </summary>
    public async Task DeleteSessionAsync(string sessionId)
    {
        var filesToDelete = new[]
        {
            Path.Combine(_sessionDir, $"{sessionId}.meta.json"),
            Path.Combine(_sessionDir, $"{sessionId}.jsonl"),
            Path.Combine(_sessionDir, $"{sessionId}.timeline.json")
        };

        foreach (var file in filesToDelete)
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        // Notify subscribers
        _busService?.Publish("Session.Deleted", sessionId);
    }

    public async Task<int> GetMessageCountAsync(string sessionId)
    {
        var history = await LoadHistoryAsync(sessionId);
        return history.Count;
    }

    public async Task<SessionStats?> GetStatsAsync(string sessionId)
    {
        var timeline = await GetTimelineAsync(sessionId);
        var stats = new SessionStats
        {
            TotalTokens = new TokenStats(),
            TotalSessions = 0,
            TotalMessages = 0
        };

        foreach (var @event in timeline)
        {
            if (@event.Type == "tool" && @event.Metadata?.TryGetValue("stats", out var statsObj) == true)
            {
                if (statsObj is JsonElement element)
                {
                    if (element.TryGetProperty("totalTokens", out var tokensProp))
                        stats.TotalTokens.Input += tokensProp.GetInt64();
                    if (element.TryGetProperty("inputTokens", out var inputProp))
                        stats.TotalTokens.Input += inputProp.GetInt64();
                    if (element.TryGetProperty("outputTokens", out var outputProp))
                        stats.TotalTokens.Output += outputProp.GetInt64();
                    if (element.TryGetProperty("cost", out var costProp))
                        stats.TotalCost += costProp.GetDouble();
                }
            }
        }

        return stats.TotalTokens.Input > 0 ? stats : null;
    }

    public async Task<List<SessionMetadata>> ListSessionMetadataAsync(bool includeArchived = false)
    {
        var sessionIds = await ListSessionsAsync();
        var metadatas = new List<SessionMetadata>();

        foreach (var sessionId in sessionIds)
        {
            var meta = await GetMetadataAsync(sessionId);
            if (meta != null)
            {
                if (!includeArchived && meta.IsArchived) continue;
                metadatas.Add(meta);
            }
        }

        return metadatas.OrderByDescending(m => m.UpdatedAt).ToList();
    }
}
