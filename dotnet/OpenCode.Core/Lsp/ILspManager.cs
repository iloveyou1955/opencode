using System.Text.Json;

namespace OpenCode.Core.Lsp;

/// <summary>
/// LSP 管理器接口。
/// </summary>
public interface ILspManager : IAsyncDisposable
{
    /// <summary>
    /// 获取指定文件对应的 LSP 客户端。
    /// </summary>
    Task<ILspClient?> GetClientForFileAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// 获取所有活跃 LSP 客户端的诊断信息。
    /// </summary>
    Task<Dictionary<string, JsonElement>> GetAllDiagnosticsAsync(CancellationToken ct = default);

    /// <summary>
    /// 在所有活跃 LSP 客户端中搜索符号。
    /// </summary>
    Task<List<JsonElement>> SearchSymbolsAsync(string query, CancellationToken ct = default);
}
