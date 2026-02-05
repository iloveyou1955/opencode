using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCode.Core.Contracts;

namespace OpenCode.Infrastructure.Tools;

/// <summary>
/// LSP 工具，提供代码智能分析功能。
/// </summary>
public class LspTool : ITool
{
    public string Name => "lsp";
    public string Description => "执行 LSP (Language Server Protocol) 操作，如跳转到定义、查找引用、悬停提示、文档符号等。";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {
        "operation": {
          "type": "string",
          "enum": ["goToDefinition", "findReferences", "hover", "documentSymbol", "workspaceSymbol", "goToImplementation", "diagnostics"],
          "description": "要执行的 LSP 操作"
        },
        "filePath": { "type": "string", "description": "文件的绝对或相对路径" },
        "line": { "type": "integer", "description": "行号 (从 1 开始)" },
        "character": { "type": "integer", "description": "字符偏移量 (从 1 开始)" },
        "query": { "type": "string", "description": "workspaceSymbol 操作的查询字符串" }
      },
      "required": ["operation"]
    }
    """;

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken ct = default)
    {
        string operation = args["operation"]?.ToString() ?? "";
        string filePath = args["filePath"]?.ToString() ?? "";
        int line = args["line"]?.GetValue<int>() ?? 1;
        int character = args["character"]?.GetValue<int>() ?? 1;
        string query = args["query"]?.ToString() ?? "";

        if (string.IsNullOrEmpty(operation)) return "错误: operation 是必需的";

        // workspaceSymbol 不需要 filePath
        if (operation != "workspaceSymbol" && string.IsNullOrEmpty(filePath))
        {
            return "错误: 此操作需要 filePath";
        }

        // 权限校验
        if (!string.IsNullOrEmpty(filePath) && !await context.RequestPermissionAsync("lsp", filePath))
        {
            return $"错误: 对 {filePath} 的 lsp 操作权限被拒绝";
        }

        if (operation == "diagnostics")
        {
            var diagnostics = await context.Lsp.GetAllDiagnosticsAsync(ct);
            return JsonSerializer.Serialize(diagnostics, new JsonSerializerOptions { WriteIndented = true });
        }

        var client = await context.Lsp.GetClientForFileAsync(filePath, ct);
        if (client == null)
        {
            return $"错误: 没有可用于文件 '{filePath}' 的 LSP 服务器。请检查配置文件中的 'lsp' 设置。";
        }

        try
        {
            JsonElement? result = operation switch
            {
                "goToDefinition" => await client.GoToDefinitionAsync(filePath, line, character, ct),
                "findReferences" => await client.FindReferencesAsync(filePath, line, character, ct),
                "hover" => await client.HoverAsync(filePath, line, character, ct),
                "documentSymbol" => await client.DocumentSymbolAsync(filePath, ct),
                "workspaceSymbol" => await client.WorkspaceSymbolAsync(query, ct),
                "goToImplementation" => await client.GoToImplementationAsync(filePath, line, character, ct),
                _ => throw new NotSupportedException($"不支持的操作 '{operation}'。")
            };

            if (result == null) return "未找到结果。";

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"执行 LSP 操作时出错: {ex.Message}";
        }
    }
}
