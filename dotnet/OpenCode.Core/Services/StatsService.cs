using OpenCode.Core.Contracts;
using OpenCode.Core.Models;
using OpenCode.Core.Utilities;
using System.Collections.Concurrent;

using OpenCode.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

using System.Text;
using System.IO;

namespace OpenCode.Core.Services;

public class SessionStats
{
    public int TotalSessions { get; set; }
    public int TotalMessages { get; set; }
    public double TotalCost { get; set; }
    public double MedianCost { get; set; } // 中位数消耗
    public int MedianMessages { get; set; } // 中位数消息数
    public TokenStats TotalTokens { get; set; } = new();
    public Dictionary<string, int> ToolUsage { get; set; } = new();
    public Dictionary<string, ModelUsageStats> ModelUsage { get; set; } = new();
    public Dictionary<string, DailyUsageStats> DailyUsage { get; set; } = new(); // yyyy-MM-dd -> stats
    public long EarliestTime { get; set; }
    public long LatestTime { get; set; }
}

public class DailyUsageStats
{
    public int Messages { get; set; }
    public double Cost { get; set; }
    public TokenStats Tokens { get; set; } = new();
}

public class TokenStats
{
    public long Input { get; set; }
    public long Output { get; set; }
    public long Reasoning { get; set; }
    public CacheStats Cache { get; set; } = new();
}

public class CacheStats
{
    public long Read { get; set; }
    public long Write { get; set; }
}

public class ModelUsageStats
{
    public int Messages { get; set; }
    public TokenStats Tokens { get; set; } = new();
    public double Cost { get; set; }
}

[ServiceRegistration(ServiceLifetime.Singleton)]
public class StatsService
{
    private readonly SessionService _sessionService;
    private readonly IProjectContext _projectContext;

    public StatsService(SessionService sessionService, IProjectContext projectContext)
    {
        _sessionService = sessionService;
        _projectContext = projectContext;
    }

    public async Task<SessionStats> AggregateAsync(int? days = null, string? projectFilter = null)
    {
        var stats = new SessionStats();
        var sessionIds = await _sessionService.ListSessionsAsync();
        
        var cutoff = days.HasValue ? DateTimeOffset.UtcNow.AddDays(-days.Value).ToUnixTimeSeconds() : 0;
        
        stats.EarliestTime = long.MaxValue;
        stats.LatestTime = 0;

        var costs = new List<double>();
        var messageCounts = new List<int>();

        foreach (var sessionId in sessionIds)
        {
            var metadata = await _sessionService.GetMetadataAsync(sessionId);
            if (metadata == null) continue;

            // 项目过滤逻辑
            if (!string.IsNullOrEmpty(projectFilter) && metadata.ProjectId != projectFilter) continue;

            if (metadata.UpdatedAt < cutoff) continue;
            
            stats.TotalSessions++;
            stats.EarliestTime = Math.Min(stats.EarliestTime, metadata.CreatedAt);
            stats.LatestTime = Math.Max(stats.LatestTime, metadata.UpdatedAt);

            var messages = await _sessionService.LoadHistoryAsync(sessionId);
            stats.TotalMessages += messages.Count;
            messageCounts.Add(messages.Count);

            double sessionCost = 0;
            foreach (var msg in messages)
            {
                if (msg.Role == "assistant")
                {
                    sessionCost += msg.Metadata.Cost;
                    stats.TotalCost += msg.Metadata.Cost;
                    
                    if (msg.Metadata.Tokens != null)
                    {
                        stats.TotalTokens.Input += msg.Metadata.Tokens.Input;
                        stats.TotalTokens.Output += msg.Metadata.Tokens.Output;
                        stats.TotalTokens.Reasoning += msg.Metadata.Tokens.Reasoning;
                        
                        if (msg.Metadata.Tokens.Cache != null)
                        {
                            stats.TotalTokens.Cache.Read += msg.Metadata.Tokens.Cache.Read;
                            stats.TotalTokens.Cache.Write += msg.Metadata.Tokens.Cache.Write;
                        }
                    }

                    // 模型使用情况统计
                    var modelId = msg.Metadata.ModelId ?? "unknown";
                    if (!stats.ModelUsage.ContainsKey(modelId))
                    {
                        stats.ModelUsage[modelId] = new ModelUsageStats();
                    }
                    var modelStats = stats.ModelUsage[modelId];
                    modelStats.Messages++;
                    modelStats.Cost += msg.Metadata.Cost;

                    // 每日统计
                    var dateKey = DateTimeOffset.FromUnixTimeSeconds(msg.Metadata.Created).LocalDateTime.ToString("yyyy-MM-dd");
                    if (!stats.DailyUsage.ContainsKey(dateKey))
                    {
                        stats.DailyUsage[dateKey] = new DailyUsageStats();
                    }
                    var daily = stats.DailyUsage[dateKey];
                    daily.Messages++;
                    daily.Cost += msg.Metadata.Cost;

                    if (msg.Metadata.Tokens != null)
                    {
                        modelStats.Tokens.Input += msg.Metadata.Tokens.Input;
                        modelStats.Tokens.Output += msg.Metadata.Tokens.Output;
                        modelStats.Tokens.Reasoning += msg.Metadata.Tokens.Reasoning;

                        daily.Tokens.Input += msg.Metadata.Tokens.Input;
                        daily.Tokens.Output += msg.Metadata.Tokens.Output;
                        daily.Tokens.Reasoning += msg.Metadata.Tokens.Reasoning;

                        if (msg.Metadata.Tokens.Cache != null)
                        {
                            modelStats.Tokens.Cache.Read += msg.Metadata.Tokens.Cache.Read;
                            modelStats.Tokens.Cache.Write += msg.Metadata.Tokens.Cache.Write;
                            daily.Tokens.Cache.Read += msg.Metadata.Tokens.Cache.Read;
                            daily.Tokens.Cache.Write += msg.Metadata.Tokens.Cache.Write;
                        }
                    }
                }

                foreach (var part in msg.Parts)
                {
                    if (part is ToolInvocationPart toolPart)
                    {
                        var toolName = toolPart.ToolInvocation.ToolName;
                        if (!stats.ToolUsage.ContainsKey(toolName)) stats.ToolUsage[toolName] = 0;
                        stats.ToolUsage[toolName]++;
                    }
                }
            }
            costs.Add(sessionCost);
        }

        if (stats.EarliestTime == long.MaxValue) stats.EarliestTime = 0;

        // 计算中位数
        stats.MedianCost = CalculateMedian(costs);
        stats.MedianMessages = (int)CalculateMedian(messageCounts.Select(x => (double)x).ToList());

        return stats;
    }

    private double CalculateMedian(List<double> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        int mid = sorted.Count / 2;
        if (sorted.Count % 2 != 0) return sorted[mid];
        return (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    public async Task<string> ExportToCsvAsync(SessionStats stats, string outputPath)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Type,Key,Messages,Cost,InputTokens,OutputTokens,ReasoningTokens,CacheRead,CacheWrite");

        // 1. 每日统计
        foreach (var (date, usage) in stats.DailyUsage.OrderBy(x => x.Key))
        {
            csv.AppendLine($"Daily,{date},{usage.Messages},{usage.Cost:F4},{usage.Tokens.Input},{usage.Tokens.Output},{usage.Tokens.Reasoning},{usage.Tokens.Cache.Read},{usage.Tokens.Cache.Write}");
        }

        // 2. 模型统计
        foreach (var (model, usage) in stats.ModelUsage.OrderByDescending(x => x.Value.Cost))
        {
            csv.AppendLine($"Model,{model},{usage.Messages},{usage.Cost:F4},{usage.Tokens.Input},{usage.Tokens.Output},{usage.Tokens.Reasoning},{usage.Tokens.Cache.Read},{usage.Tokens.Cache.Write}");
        }

        // 3. 工具统计
        foreach (var (tool, count) in stats.ToolUsage.OrderByDescending(x => x.Value))
        {
            csv.AppendLine($"Tool,{tool},{count},0,0,0,0,0,0");
        }

        await File.WriteAllTextAsync(outputPath, csv.ToString(), Encoding.UTF8);
        return outputPath;
    }
}
