using System.Text.Json.Nodes;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using OpenCode.Core.Contracts;

namespace OpenCode.Infrastructure.Tools;

/// <summary>
/// 使用 Glob 模式匹配文件的工具。
/// </summary>
public class GlobTool : ITool
{
    public string Name => "glob";
    public string Description => "根据 Glob 模式（如 'src/**/*.ts'）快速查找文件。支持多种模式匹配。";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {
        "pattern": { "type": "string", "description": "Glob 匹配模式，例如 '**/*.cs' 或 'src/utils/*.js'" },
        "rootPath": { "type": "string", "description": "搜索的根目录，默认为项目根目录" }
      },
      "required": ["pattern"]
    }
    """;

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken ct = default)
    {
        string pattern = args["pattern"]?.ToString() ?? "";
        string rootPath = args["rootPath"]?.ToString() ?? ".";

        if (string.IsNullOrEmpty(pattern)) return "错误: pattern 是必需的";

        // 权限校验 (简单检查根目录)
        if (!await context.RequestPermissionAsync("glob", rootPath))
        {
            return $"错误: 对目录 {rootPath} 的 glob 操作权限被拒绝";
        }

        try
        {
            var matcher = new Matcher();
            matcher.AddInclude(pattern);

            string fullRootPath = Path.GetFullPath(rootPath);
            var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(fullRootPath)));

            if (!result.HasMatches) return "未找到匹配的文件。";

            var files = result.Files.Select(f => f.Path).ToList();
            return string.Join("\n", files);
        }
        catch (Exception ex)
        {
            return $"执行 Glob 匹配时出错: {ex.Message}";
        }
    }
}
