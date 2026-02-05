using System.Text;
using Microsoft.Extensions.Logging;
using OpenCode.Core.Contracts;

namespace OpenCode.Core.Services;

public class TruncationService
{
    public const int MaxLines = 2000;
    public const int MaxBytes = 50 * 1024; // 50KB
    private readonly string _outputDirectory;
    private readonly Scheduler? _scheduler;
    private readonly ILogger? _logger;

    public TruncationService(string projectRoot, Scheduler? scheduler = null, ILogger? logger = null)
    {
        _outputDirectory = Path.Combine(projectRoot, ".opencode", "data", "tool-output");
        if (!Directory.Exists(_outputDirectory))
        {
            Directory.CreateDirectory(_outputDirectory);
        }

        _scheduler = scheduler;
        _logger = logger;
        if (_scheduler != null)
        {
            _scheduler.Register("truncation.cleanup", TimeSpan.FromHours(1), CleanupAsync);
        }
    }

    public static int EstimateTokens(string input)
    {
        if (string.IsNullOrEmpty(input)) return 0;
        return (int)Math.Round(input.Length / 4.0);
    }

    public async Task CleanupAsync()
    {
        if (!Directory.Exists(_outputDirectory)) return;

        var cutoff = DateTime.Now.AddDays(-7);
        var files = Directory.GetFiles(_outputDirectory, "tool_*");

        foreach (var file in files)
        {
            var creationTime = File.GetCreationTime(file);
            if (creationTime < cutoff)
            {
                try
                {
                    File.Delete(file);
                }
                catch { }
            }
        }
        await Task.CompletedTask;
    }

    public async Task<(string Content, bool Truncated, string? OutputPath)> TruncateAsync(string text, string? toolName = null, AgentMetadata? agent = null, string direction = "head")
    {
        var tokens = EstimateTokens(text);
        _logger?.LogDebug("Truncating text with {Tokens} tokens", tokens);

        var lines = text.Split('\n');
        var bytes = Encoding.UTF8.GetByteCount(text);

        if (lines.Length <= MaxLines && bytes <= MaxBytes)
        {
            return (text, false, null);
        }

        var sb = new StringBuilder();
        int currentBytes = 0;
        int currentLines = 0;
        bool hitBytes = false;

        if (direction == "head")
        {
            for (int i = 0; i < lines.Length && currentLines < MaxLines; i++)
            {
                var line = lines[i];
                var lineBytes = Encoding.UTF8.GetByteCount(line) + (i > 0 ? 1 : 0); // +1 for \n
                if (currentBytes + lineBytes > MaxBytes)
                {
                    hitBytes = true;
                    break;
                }

                if (i > 0) sb.Append('\n');
                sb.Append(line);
                currentBytes += lineBytes;
                currentLines++;
            }
        }
        else
        {
            var selectedLines = new List<string>();
            for (int i = lines.Length - 1; i >= 0 && currentLines < MaxLines; i--)
            {
                var line = lines[i];
                var lineBytes = Encoding.UTF8.GetByteCount(line) + (selectedLines.Count > 0 ? 1 : 0);
                if (currentBytes + lineBytes > MaxBytes)
                {
                    hitBytes = true;
                    break;
                }

                selectedLines.Insert(0, line);
                currentBytes += lineBytes;
                currentLines++;
            }
            sb.Append(string.Join("\n", selectedLines));
        }

        var truncatedContent = sb.ToString();
        var id = Guid.NewGuid().ToString("N").Substring(0, 12);
        var fileName = $"tool_{id}";
        var filePath = Path.Combine(_outputDirectory, fileName);

        await File.WriteAllTextAsync(filePath, text);

        var removed = hitBytes ? bytes - currentBytes : lines.Length - currentLines;
        var unit = hitBytes ? "bytes" : "lines";
        
        bool hasTaskTool = false;
        if (agent?.Permissions != null && agent.Permissions.TryGetValue("task", out var taskPerm))
        {
            // 简单检查权限，TS 版有更复杂的评估逻辑
            hasTaskTool = taskPerm.ToString() != "deny";
        }

        string hint = hasTaskTool
            ? $"The tool call succeeded but the output was truncated. Full output saved to: {filePath}\nUse the Task tool to have explore agent process this file with Grep and Read (with offset/limit). Do NOT read the full file yourself - delegate to save context."
            : $"The tool call succeeded but the output was truncated. Full output saved to: {filePath}\nUse Grep to search the full content or Read with offset/limit to view specific sections.";

        var finalMessage = direction == "head"
            ? $"{truncatedContent}\n\n...{removed} {unit} truncated...\n\n{hint}"
            : $"...{removed} {unit} truncated...\n\n{hint}\n\n{truncatedContent}";

        return (finalMessage, true, filePath);
    }
}
