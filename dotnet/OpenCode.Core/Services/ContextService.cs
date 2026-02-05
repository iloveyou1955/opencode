using OpenCode.Core.Contracts;
using OpenCode.Core.Lsp;
using System.Text.Json.Nodes;

using OpenCode.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.Core.Services;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class ContextService
{
    private readonly List<string> _openFiles = new();
    private readonly Dictionary<string, List<string>> _diagnostics = new();
    private string? _cursorFile;
    private int _cursorLine;
    private int _cursorCharacter;

    public void SetOpenFiles(IEnumerable<string> files)
    {
        _openFiles.Clear();
        _openFiles.AddRange(files);
    }

    public void AddOpenFile(string filePath)
    {
        if (!_openFiles.Contains(filePath))
            _openFiles.Add(filePath);
    }

    public void SetDiagnostics(string filePath, List<string> diagnostics)
    {
        _diagnostics[filePath] = diagnostics;
    }

    public void SetCursor(string filePath, int line, int character)
    {
        _cursorFile = filePath;
        _cursorLine = line;
        _cursorCharacter = character;
        AddOpenFile(filePath);
    }

    public async Task UpdateDiagnosticsAsync(ILspManager lspManager, CancellationToken ct = default)
    {
        var diagnostics = await lspManager.GetAllDiagnosticsAsync(ct);
        foreach (var entry in diagnostics)
        {
            var diags = new List<string>();
            if (entry.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var diag in entry.Value.EnumerateArray())
                {
                    var message = diag.GetProperty("message").GetString();
                    var severity = diag.TryGetProperty("severity", out var s) ? s.ToString() : "Error";
                    var line = diag.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32();
                    diags.Add($"[{severity}] {message} (line {line + 1})");
                }
            }
            SetDiagnostics(entry.Key, diags);
        }
    }

    public async Task<string> GetContextInstructionsAsync()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<context>");
        
        if (_openFiles.Any())
        {
            sb.AppendLine("  <open_files>");
            foreach (var file in _openFiles)
            {
                sb.AppendLine($"    - {file}");
            }
            sb.AppendLine("  </open_files>");
        }

        if (_cursorFile != null)
        {
            sb.AppendLine($"  <cursor file=\"{_cursorFile}\" line=\"{_cursorLine}\" character=\"{_cursorCharacter}\" />");
        }

        if (_diagnostics.Any())
        {
            sb.AppendLine("  <diagnostics>");
            foreach (var entry in _diagnostics)
            {
                if (entry.Value.Any())
                {
                    sb.AppendLine($"    <file path=\"{entry.Key}\">");
                    foreach (var diag in entry.Value)
                    {
                        sb.AppendLine($"      - {diag}");
                    }
                    sb.AppendLine("    </file>");
                }
            }
            sb.AppendLine("  </diagnostics>");
        }

        sb.AppendLine("</context>");
        return await Task.FromResult(sb.ToString());
    }
}
