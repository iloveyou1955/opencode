namespace OpenCode.Core.Models;

public record SessionMetadata(
    string Id,
    string? Title = null,
    string? AgentName = null,
    long CreatedAt = 0,
    long UpdatedAt = 0,
    string? ForkedFrom = null,
    string? ForkPoint = null,
    bool IsArchived = false,
    long? ArchivedAt = null,
    string? VersionPointer = null, // 非破坏性回滚：指向当前活跃的 TimelineEvent ID
    string? ProjectId = null // 所属项目 ID (通常为目录名或仓库名)
);

public record TimelineEvent(
    string Id,
    string Type, // "message", "checkpoint", "revert"
    string? MessageId = null,
    string? SnapshotHash = null,
    long Timestamp = 0,
    string? Description = null,
    Dictionary<string, object>? Metadata = null
);
