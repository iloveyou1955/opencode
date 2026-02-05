using System.Text.Json.Nodes;
using OpenCode.Core.Contracts;
using OpenCode.Core.Utilities;
using OpenCode.Core.Services;

namespace OpenCode.Infrastructure.Tools;

/// <summary>
/// 应用补丁工具，支持复杂的增删改移动操作。
/// </summary>
public class ApplyPatchTool : ITool
{
    private readonly BusService _bus;

    public ApplyPatchTool(BusService bus)
    {
        _bus = bus;
    }
    public string Name => "apply_patch";
    public string Description => "应用一个结构化补丁。支持文件添加、删除、更新和移动。比普通的 edit 更强大。";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {
        "patchText": { "type": "string", "description": "包含 *** Begin Patch 和 *** End Patch 的补丁文本" }
      },
      "required": ["patchText"]
    }
    """;

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken ct = default)
    {
        string patchText = args["patchText"]?.ToString() ?? "";
        if (string.IsNullOrEmpty(patchText)) return "错误: patchText 是必需的";

        try
        {
            var hunks = PatchParser.Parse(patchText);
            if (hunks.Count == 0) return "错误: 补丁中未发现有效变更";

            var results = new List<string>();

            foreach (var hunk in hunks)
            {
                if (!await context.RequestPermissionAsync("edit", hunk.Path))
                {
                    results.Add($"跳过 {hunk.Path}: 权限被拒绝");
                    continue;
                }

                switch (hunk.Type)
                {
                    case "add":
                        await File.WriteAllTextAsync(hunk.Path, hunk.Contents, ct);
                        await _bus.PublishAsync("file.edited", new { file = hunk.Path });
                        await _bus.PublishAsync("file.watcher.updated", new { file = hunk.Path, @event = "add" });
                        results.Add($"A {hunk.Path}");
                        break;

                    case "delete":
                        if (File.Exists(hunk.Path))
                        {
                            File.Delete(hunk.Path);
                            await _bus.PublishAsync("file.edited", new { file = hunk.Path });
                            await _bus.PublishAsync("file.watcher.updated", new { file = hunk.Path, @event = "unlink" });
                            results.Add($"D {hunk.Path}");
                        }
                        break;

                    case "update":
                        string content = await File.ReadAllTextAsync(hunk.Path, ct);
                        string newContent = ApplyChunks(content, hunk.Chunks);
                        
                        string targetPath = hunk.MovePath ?? hunk.Path;
                        if (hunk.MovePath != null)
                        {
                            File.Delete(hunk.Path);
                            await _bus.PublishAsync("file.edited", new { file = hunk.Path });
                            await _bus.PublishAsync("file.watcher.updated", new { file = hunk.Path, @event = "unlink" });
                        }
                        
                        await File.WriteAllTextAsync(targetPath, newContent, ct);
                        await _bus.PublishAsync("file.edited", new { file = targetPath });
                        await _bus.PublishAsync("file.watcher.updated", new { file = targetPath, @event = hunk.MovePath != null ? "add" : "change" });
                        results.Add(hunk.MovePath != null ? $"R {hunk.Path} -> {hunk.MovePath}" : $"M {hunk.Path}");
                        break;
                }
            }

            var output = $"成功应用补丁。变更摘要:\n{string.Join("\n", results)}";

            // 报告 LSP 错误
            var diagnostics = await context.Lsp.GetAllDiagnosticsAsync(ct);
            var diagOutput = DiagnosticFormatter.Format(diagnostics);
            if (!string.IsNullOrEmpty(diagOutput))
            {
                output += $"\n\n{diagOutput}";
            }

            return output;
        }
        catch (Exception ex)
        {
            return $"应用补丁时出错: {ex.Message}";
        }
    }

    private string ApplyChunks(string content, List<PatchParser.UpdateChunk> chunks)
    {
        var lines = content.Split('\n').ToList();
        int searchStartIndex = 0;

        foreach (var chunk in chunks)
        {
            if (chunk.OldLines.Count == 0)
            {
                // 纯添加
                if (chunk.IsEndOfFile)
                {
                    lines.AddRange(chunk.NewLines);
                }
                else
                {
                    // 如果有上下文，在上下文之后添加
                    int contextIdx = -1;
                    if (!string.IsNullOrEmpty(chunk.Context))
                    {
                        contextIdx = lines.FindIndex(searchStartIndex, l => l.Trim() == chunk.Context.Trim());
                    }

                    if (contextIdx != -1)
                    {
                        lines.InsertRange(contextIdx + 1, chunk.NewLines);
                        searchStartIndex = contextIdx + 1 + chunk.NewLines.Count;
                    }
                    else
                    {
                        lines.AddRange(chunk.NewLines);
                    }
                }
            }
            else
            {
                // 查找匹配的旧行
                int foundIdx = -1;
                for (int i = searchStartIndex; i <= lines.Count - chunk.OldLines.Count; i++)
                {
                    bool match = true;
                    for (int j = 0; j < chunk.OldLines.Count; j++)
                    {
                        if (lines[i + j].Trim() != chunk.OldLines[j].Trim())
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        foundIdx = i;
                        break;
                    }
                }

                if (foundIdx != -1)
                {
                    lines.RemoveRange(foundIdx, chunk.OldLines.Count);
                    lines.InsertRange(foundIdx, chunk.NewLines);
                    searchStartIndex = foundIdx + chunk.NewLines.Count;
                }
                else
                {
                    throw new Exception($"无法找到匹配的旧行片段。上下文: {chunk.Context}");
                }
            }
        }

        return string.Join("\n", lines);
    }
}
