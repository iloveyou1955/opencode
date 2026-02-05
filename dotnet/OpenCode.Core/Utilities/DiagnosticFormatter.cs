using System.Text;
using System.Text.Json;

namespace OpenCode.Core.Utilities;

public static class DiagnosticFormatter
{
    public static string Format(Dictionary<string, JsonElement> diagnostics, string? filterFile = null)
    {
        var sb = new StringBuilder();
        foreach (var kvp in diagnostics)
        {
            var filePath = kvp.Key;
            if (filterFile != null && !filePath.Equals(filterFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileDiagnostics = kvp.Value;
            if (fileDiagnostics.ValueKind != JsonValueKind.Array || fileDiagnostics.GetArrayLength() == 0)
            {
                continue;
            }

            sb.AppendLine($"LSP errors detected in {filePath}:");
            sb.AppendLine("<diagnostics>");
            foreach (var diag in fileDiagnostics.EnumerateArray())
            {
                var message = diag.GetProperty("message").GetString();
                var severity = diag.GetProperty("severity").GetInt32(); // 1 = Error, 2 = Warning
                var range = diag.GetProperty("range");
                var start = range.GetProperty("start");
                var line = start.GetProperty("line").GetInt32() + 1;
                var col = start.GetProperty("character").GetInt32() + 1;

                string severityStr = severity switch
                {
                    1 => "Error",
                    2 => "Warning",
                    3 => "Info",
                    4 => "Hint",
                    _ => "Unknown"
                };

                sb.AppendLine($"[{severityStr}] Line {line}, Col {col}: {message}");
            }
            sb.AppendLine("</diagnostics>");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
