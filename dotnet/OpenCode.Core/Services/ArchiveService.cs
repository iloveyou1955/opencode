using Microsoft.Extensions.DependencyInjection;
using OpenCode.Core.Models;
using OpenCode.Core.Utilities;
using OpenCode.Core.Attributes;
using System.Collections.Concurrent;

namespace OpenCode.Core.Services;

/// <summary>
/// 会话归档服务，负责自动归档不活跃的会话。
/// </summary>
[ServiceRegistration(ServiceLifetime.Singleton)]
public class ArchiveService
{
    private readonly SessionService _sessionService;
    private readonly ConfigService _configService;
    private readonly ConcurrentDictionary<string, Timer> _timers = new();

    /// <summary>
    /// 初始化 ArchiveService。
    /// </summary>
    /// <param name="sessionService">会话服务</param>
    /// <param name="configService">配置服务</param>
    public ArchiveService(SessionService sessionService, ConfigService configService)
    {
        _sessionService = sessionService;
        _configService = configService;
    }

    /// <summary>
    /// 自动归档不活跃的会话。
    /// </summary>
    /// <param name="daysThreshold">不活跃天数阈值，默认为 30 天</param>
    /// <returns>已归档的会话数量</returns>
    public async Task<int> AutoArchiveAsync(int daysThreshold = 30)
    {
        var sessions = await _sessionService.ListSessionMetadataAsync();
        var thresholdTime = DateTimeOffset.UtcNow.AddDays(-daysThreshold).ToUnixTimeSeconds();
        int archivedCount = 0;

        foreach (var session in sessions)
        {
            if (!session.IsArchived && session.UpdatedAt < thresholdTime)
            {
                await _sessionService.ArchiveSessionAsync(session.Id);
                archivedCount++;
            }
        }

        return archivedCount;
    }

    /// <summary>
    /// 获取已归档的会话列表。
    /// </summary>
    /// <returns>已归档会话的元数据列表</returns>
    public async Task<List<SessionMetadata>> ListArchivedSessionsAsync()
    {
        var sessions = await _sessionService.ListSessionMetadataAsync();
        return sessions.Where(s => s.IsArchived).ToList();
    }

    /// <summary>
    /// 恢复已归档的会话。
    /// </summary>
    /// <param name="sessionId">会话 ID</param>
    public async Task RestoreSessionAsync(string sessionId)
    {
        await _sessionService.UnarchiveSessionAsync(sessionId);
    }
}
