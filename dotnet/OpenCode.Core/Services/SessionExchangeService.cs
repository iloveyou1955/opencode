using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using OpenCode.Core.Models;
using OpenCode.Core.Utilities;
using OpenCode.Core.Attributes;

namespace OpenCode.Core.Services;

/// <summary>
/// 会话导入导出服务。
/// </summary>
[ServiceRegistration(ServiceLifetime.Singleton)]
public class SessionExchangeService
{
    private readonly SessionService _sessionService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public SessionExchangeService(
        SessionService sessionService,
        IHttpClientFactory httpClientFactory)
    {
        _sessionService = sessionService;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// 导出指定会话的数据为 JSON 格式。
    /// </summary>
    public async Task<string> ExportSessionAsync(string sessionId)
    {
        var metadata = await _sessionService.GetMetadataAsync(sessionId);
        if (metadata == null)
        {
            throw new Exception($"Session {sessionId} not found.");
        }

        var messages = await _sessionService.LoadHistoryAsync(sessionId);
        var timeline = await _sessionService.GetTimelineAsync(sessionId);

        var exportData = new SessionExportData(
            ExportVersion: "1.0",
            ExportedAt: DateTime.UtcNow,
            Info: metadata,
            Messages: messages,
            Timeline: timeline
        );

        return JsonSerializer.Serialize(exportData, _jsonOptions);
    }

    /// <summary>
    /// 导出会话到文件。
    /// </summary>
    public async Task<string> ExportToFileAsync(string sessionId, string outputPath)
    {
        var jsonContent = await ExportSessionAsync(sessionId);
        await File.WriteAllTextAsync(outputPath, jsonContent);
        return outputPath;
    }

    /// <summary>
    /// 导出最新会话到文件。
    /// </summary>
    public async Task<string?> ExportLatestSessionAsync(string? outputPath = null)
    {
        var sessions = await _sessionService.ListSessionMetadataAsync();
        if (sessions.Count == 0)
        {
            throw new Exception("No sessions found.");
        }

        var latestSession = sessions.OrderByDescending(s => s.UpdatedAt).FirstOrDefault();
        if (latestSession == null)
        {
            throw new Exception("No active sessions found.");
        }

        if (string.IsNullOrEmpty(outputPath))
        {
            var title = latestSession.Title ?? "Untitled";
            var safeTitle = string.Join("_", title.Split(Path.GetInvalidFileNameChars()));
            outputPath = $"{safeTitle}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
        }

        return await ExportToFileAsync(latestSession.Id, outputPath);
    }

    /// <summary>
    /// 从 JSON 数据导入会话。
    /// </summary>
    public async Task<string> ImportSessionAsync(string jsonContent)
    {
        var exportData = JsonSerializer.Deserialize<SessionExportData>(jsonContent, _jsonOptions);
        if (exportData == null || exportData.Info == null)
        {
            throw new Exception("Invalid session data format.");
        }

        var sessionId = exportData.Info.Id;

        if (string.IsNullOrEmpty(sessionId))
        {
            sessionId = Guid.NewGuid().ToString("N");
        }

        await _sessionService.SaveMetadataAsync(sessionId, exportData.Info);

        var historyPath = Path.Combine(_sessionService.GetSessionDirectory(), $"{sessionId}.jsonl");
        var timelinePath = Path.Combine(_sessionService.GetSessionDirectory(), $"{sessionId}.timeline.jsonl");
        
        if (File.Exists(historyPath)) File.Delete(historyPath);
        if (File.Exists(timelinePath)) File.Delete(timelinePath);

        if (exportData.Messages != null)
        {
            foreach (var msg in exportData.Messages)
            {
                await _sessionService.SaveMessageAsync(sessionId, msg);
            }
        }

        if (exportData.Timeline != null)
        {
            foreach (var ev in exportData.Timeline)
            {
                await _sessionService.AddTimelineEventAsync(sessionId, ev);
            }
        }

        return sessionId;
    }

    /// <summary>
    /// 从文件导入会话。
    /// </summary>
    public async Task<string> ImportFromFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new Exception($"File not found: {filePath}");
        }

        var jsonContent = await File.ReadAllTextAsync(filePath);
        return await ImportSessionAsync(jsonContent);
    }

    /// <summary>
    /// 从 URL 导入会话。
    /// </summary>
    public async Task<string> ImportFromUrlAsync(string url)
    {
        var client = _httpClientFactory.CreateClient();
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var jsonContent = await response.Content.ReadAsStringAsync();
        return await ImportSessionAsync(jsonContent);
    }

    /// <summary>
    /// 验证导出数据格式。
    /// </summary>
    public ValidationResult ValidateExportData(string jsonContent)
    {
        try
        {
            var exportData = JsonSerializer.Deserialize<SessionExportData>(jsonContent, _jsonOptions);
            
            if (exportData == null)
            {
                return new ValidationResult(false, "Invalid JSON: cannot deserialize.");
            }

            if (exportData.Info == null)
            {
                return new ValidationResult(false, "Missing session info.");
            }

            if (string.IsNullOrEmpty(exportData.Info.Id))
            {
                return new ValidationResult(false, "Missing session ID.");
            }

            if (string.IsNullOrEmpty(exportData.Info.Title))
            {
                return new ValidationResult(false, "Missing session title.");
            }

            return new ValidationResult(true, "Valid session data.");
        }
        catch (JsonException ex)
        {
            return new ValidationResult(false, $"Invalid JSON: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取会话预览信息（用于交互式选择）。
    /// </summary>
    public async Task<List<SessionPreview>> GetSessionPreviewsAsync()
    {
        var sessions = await _sessionService.ListSessionMetadataAsync();
        var previews = new List<SessionPreview>();

        foreach (var session in sessions)
        {
            var messageCount = await _sessionService.GetMessageCountAsync(session.Id);
            var stats = await _sessionService.GetStatsAsync(session.Id);

            previews.Add(new SessionPreview
            {
                Id = session.Id,
                Title = session.Title ?? "",
                UpdatedAt = session.UpdatedAt,
                MessageCount = messageCount,
                TotalTokens = stats?.TotalTokens?.Input ?? 0,
                TotalCost = stats?.TotalCost ?? 0
            });
        }

        return previews.OrderByDescending(p => p.UpdatedAt).ToList();
    }

    public record ValidationResult(bool IsValid, string Message);

    public record SessionPreview
    {
        public string Id { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public long UpdatedAt { get; init; }
        public int MessageCount { get; init; }
        public long TotalTokens { get; init; }
        public double TotalCost { get; init; }
    }

    private record SessionExportData(
        string ExportVersion,
        DateTime ExportedAt,
        SessionMetadata Info,
        List<MessageInfo> Messages,
        List<TimelineEvent> Timeline
    );
}
