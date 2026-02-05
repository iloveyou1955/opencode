using System.Text.Json;

namespace OpenCode.Core.Lsp;

/// <summary>
/// LSP 客户端接口，定义了通用的 LSP 操作。
/// </summary>
public interface ILspClient : IAsyncDisposable
{
    /// <summary>
    /// 初始化 LSP 服务器。
    /// </summary>
    /// <param name="rootPath">工作区根目录。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>是否初始化成功。</returns>
    Task<bool> InitializeAsync(string rootPath, CancellationToken ct = default);

    /// <summary>
    /// 跳转到定义。
    /// </summary>
    Task<JsonElement?> GoToDefinitionAsync(string filePath, int line, int character, CancellationToken ct = default);

    /// <summary>
    /// 查找引用。
    /// </summary>
    Task<JsonElement?> FindReferencesAsync(string filePath, int line, int character, CancellationToken ct = default);

    /// <summary>
    /// 悬停提示。
    /// </summary>
    Task<JsonElement?> HoverAsync(string filePath, int line, int character, CancellationToken ct = default);

    /// <summary>
    /// 文档符号。
    /// </summary>
    Task<JsonElement?> DocumentSymbolAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// 工作区符号。
    /// </summary>
    Task<JsonElement?> WorkspaceSymbolAsync(string query, CancellationToken ct = default);

    /// <summary>
    /// 跳转到实现。
    /// </summary>
    Task<JsonElement?> GoToImplementationAsync(string filePath, int line, int character, CancellationToken ct = default);

    /// <summary>
    /// 获取当前所有诊断信息。
    /// </summary>
    Task<Dictionary<string, JsonElement>> GetDiagnosticsAsync(CancellationToken ct = default);
}
