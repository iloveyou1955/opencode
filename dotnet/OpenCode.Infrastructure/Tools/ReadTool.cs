using System.Text.Json.Nodes;
using System.Text;
using OpenCode.Core.Contracts;

namespace OpenCode.Infrastructure.Tools;

public class ReadTool : ITool
{
    public string Name => "read";
    public string Description => "Read a file from the filesystem. You can specify offset and limit to read parts of large files.";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {
        "filePath": { "type": "string", "description": "The path to the file to read" },
        "offset": { "type": "integer", "description": "The line number to start reading from (0-based). Default is 0." },
        "limit": { "type": "integer", "description": "The number of lines to read. Default is 2000." }
      },
      "required": ["filePath"]
    }
    """;

    private const int DEFAULT_READ_LIMIT = 2000;
    private const int MAX_LINE_LENGTH = 2000;
    private const int MAX_BYTES = 50 * 1024;

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken cancellationToken = default)
    {
        string filePath = args["filePath"]?.ToString() ?? "";
        int offset = args["offset"]?.GetValue<int>() ?? 0;
        int limit = args["limit"]?.GetValue<int>() ?? DEFAULT_READ_LIMIT;

        if (string.IsNullOrEmpty(filePath)) return "Error: filePath is required";

        // 权限校验
        if (!await context.RequestPermissionAsync("read", filePath))
        {
            return $"Error: Permission denied for read on {filePath}";
        }

        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath)) return $"Error: File '{filePath}' not found.";

        if (IsBinaryFile(fullPath))
        {
             return $"Error: Cannot read binary file: {filePath}";
        }

        try
        {
            var lines = await File.ReadAllLinesAsync(fullPath, cancellationToken);
            return FormatFileContent(lines, offset, limit);
        }
        catch (Exception ex)
        {
            return $"Error reading file: {ex.Message}";
        }
    }

    private static string FormatFileContent(string[] lines, int offset, int limit)
    {
        int start = Math.Max(0, offset);
        int end = Math.Min(lines.Length, start + limit);
        
        var sb = new StringBuilder();
        sb.AppendLine("<file>");
        
        int currentBytes = 0;
        bool truncatedByBytes = false;
        int lastProcessedLine = start;

        for (int i = start; i < end; i++)
        {
            string line = lines[i];
            if (line.Length > MAX_LINE_LENGTH)
            {
                line = line.Substring(0, MAX_LINE_LENGTH) + "...";
            }

            // (index + 1) format: 00001| line content
            string formattedLine = $"{(i + 1):D5}| {line}";
            int lineBytes = Encoding.UTF8.GetByteCount(formattedLine) + 1; // +1 for newline

            if (currentBytes + lineBytes > MAX_BYTES)
            {
                truncatedByBytes = true;
                break;
            }

            sb.AppendLine(formattedLine);
            currentBytes += lineBytes;
            lastProcessedLine = i + 1;
        }

        int totalLines = lines.Length;
        bool hasMoreLines = totalLines > end; // This logic might need slight adjustment if truncatedByBytes

        if (truncatedByBytes)
        {
            sb.AppendLine();
            sb.AppendLine($"(Output truncated at {MAX_BYTES} bytes. Use 'offset' parameter to read beyond line {lastProcessedLine})");
        }
        else if (hasMoreLines)
        {
            sb.AppendLine();
            sb.AppendLine($"(File has more lines. Use 'offset' parameter to read beyond line {end})");
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine($"(End of file - total {totalLines} lines)");
        }

        sb.Append("</file>");
        return sb.ToString();
    }

    private static bool IsBinaryFile(string path)
    {
        var extension = Path.GetExtension(path).ToLower();
        string[] binaryExtensions = { ".exe", ".dll", ".so", ".bin", ".zip", ".tar", ".gz", ".7z", ".pyc", ".class", ".png", ".jpg", ".jpeg", ".pdf" };
        if (binaryExtensions.Contains(extension)) return true;

        try 
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[4096];
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0) return false;

            int nonPrintableCount = 0;
            for (int i = 0; i < read; i++)
            {
                byte b = buffer[i];
                if (b == 0) return true; 
                if (b < 9 || (b > 13 && b < 32))
                {
                    nonPrintableCount++;
                }
            }

            return (double)nonPrintableCount / read > 0.3;
        }
        catch
        {
            return false; // Assume text if can't check
        }
    }
}
