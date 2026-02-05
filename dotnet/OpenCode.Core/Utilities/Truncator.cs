using System.Text;

namespace OpenCode.Core.Utilities;

/// <summary>
/// 输出截断结果
/// </summary>
public record TruncationResult(string Content, bool IsTruncated, string? OutputPath = null);

/// <summary>
/// 截断选项
/// </summary>
public class TruncationOptions
{
    public int MaxLines { get; set; } = 2000;
    public int MaxBytes { get; set; } = 50 * 1024; // 50KB
    public TruncationDirection Direction { get; set; } = TruncationDirection.Head;
}

public enum TruncationDirection
{
    Head,
    Tail
}

/// <summary>
/// 负责截断过长的工具输出，避免爆 Token
/// </summary>
public static class Truncator
{
    // Use TempPath instead of AppData to avoid permission issues in sandbox/CI
    private static readonly string OutputDir = Path.Combine(Path.GetTempPath(), "OpenCode", "tool-output");

    static Truncator()
    {
        if (!Directory.Exists(OutputDir))
        {
            Directory.CreateDirectory(OutputDir);
        }
    }

    /// <summary>
    /// 处理输出文本，如果过长则截断并保存到文件
    /// </summary>
    public static async ValueTask<TruncationResult> TruncateAsync(string text, TruncationOptions? options = null)
    {
        options ??= new TruncationOptions();
        
        // 快速检查
        int byteCount = Encoding.UTF8.GetByteCount(text);
        var lines = text.Split('\n'); // 简单分割，也可以使用更高效的 Span 分割
        
        if (lines.Length <= options.MaxLines && byteCount <= options.MaxBytes)
        {
            return new TruncationResult(text, false);
        }

        // 需要截断
        var sb = new StringBuilder();
        int currentBytes = 0;
        bool hitBytes = false;
        int linesIncluded = 0;

        if (options.Direction == TruncationDirection.Head)
        {
            foreach (var line in lines)
            {
                if (linesIncluded >= options.MaxLines) break;
                
                int lineBytes = Encoding.UTF8.GetByteCount(line) + 1; // +1 for newline
                if (currentBytes + lineBytes > options.MaxBytes)
                {
                    hitBytes = true;
                    break;
                }
                
                sb.Append(line).Append('\n');
                currentBytes += lineBytes;
                linesIncluded++;
            }
        }
        else // Tail
        {
            // 从后往前取，但顺序要保持
            var keptLines = new LinkedList<string>();
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                if (keptLines.Count >= options.MaxLines) break;
                
                var line = lines[i];
                int lineBytes = Encoding.UTF8.GetByteCount(line) + 1;
                if (currentBytes + lineBytes > options.MaxBytes)
                {
                    hitBytes = true;
                    break;
                }
                
                keptLines.AddFirst(line);
                currentBytes += lineBytes;
            }
            
            foreach (var line in keptLines)
            {
                sb.Append(line).Append('\n');
            }
        }

        string preview = sb.ToString().TrimEnd();
        
        // 保存完整内容到文件
        string fileName = $"tool_output_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..8]}.txt";
        string filePath = Path.Combine(OutputDir, fileName);
        await File.WriteAllTextAsync(filePath, text);

        // 构造提示信息
        long removedCount = hitBytes 
            ? byteCount - currentBytes 
            : lines.Length - linesIncluded;
            
        string unit = hitBytes ? "bytes" : "lines";
        
        string hint = $"The tool call succeeded but the output was truncated. Full output saved to: {filePath}\n" +
                      $"Use 'read_file' with offset/limit to view specific sections, or 'grep' to search.";

        string finalMessage = options.Direction == TruncationDirection.Head
            ? $"{preview}\n\n...{removedCount} {unit} truncated...\n\n{hint}"
            : $"...{removedCount} {unit} truncated...\n\n{hint}\n\n{preview}";

        return new TruncationResult(finalMessage, true, filePath);
    }
}
