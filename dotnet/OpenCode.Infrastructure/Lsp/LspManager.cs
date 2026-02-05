using OpenCode.Core.Lsp;
using OpenCode.Core.Services;
using OpenCode.Infrastructure.Services;
using System.Text.Json;

namespace OpenCode.Infrastructure.Lsp;

/// <summary>
/// LSP 管理器，负责根据文件类型管理和调度不同的 LSP 客户端。
/// </summary>
public class LspManager : ILspManager
{
    private readonly Dictionary<string, ILspClient> _clients = new();
    private readonly Dictionary<string, LspConfig> _configs;
    private readonly string _rootPath;

    public LspManager(string rootPath, Dictionary<string, LspConfig>? configs)
    {
        _rootPath = rootPath;
        _configs = configs ?? new Dictionary<string, LspConfig>();
    }

    /// <summary>
    /// 获取指定文件对应的 LSP 客户端。
    /// </summary>
    /// <param name="filePath">文件路径。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>LSP 客户端，如果未找到匹配的服务器则返回 null。</returns>
    public async Task<ILspClient?> GetClientForFileAsync(string filePath, CancellationToken ct = default)
    {
        if (Flag.DisableLsp) return null;

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) return null;

        // 查找支持该扩展名的配置
        var configEntry = _configs.FirstOrDefault(x => x.Value.Languages != null && x.Value.Languages.Contains(ext));
        
        if (configEntry.Value == null || string.IsNullOrEmpty(configEntry.Value.Command))
        {
            return null;
        }

        var key = configEntry.Key;
        if (_clients.TryGetValue(key, out var client))
        {
            return client;
        }

        // 创建并初始化新客户端
        var newClient = new OmniSharpLspClient(configEntry.Value.Command, configEntry.Value.Args ?? Array.Empty<string>());
        var success = await newClient.InitializeAsync(_rootPath, ct).ConfigureAwait(false);
        
        if (success)
        {
            _clients[key] = newClient;
            return newClient;
        }

        await newClient.DisposeAsync().ConfigureAwait(false);
        return null;
    }

    public async Task<Dictionary<string, JsonElement>> GetAllDiagnosticsAsync(CancellationToken ct = default)
    {
        var allDiagnostics = new Dictionary<string, JsonElement>();
        foreach (var client in _clients.Values)
        {
            var diagnostics = await client.GetDiagnosticsAsync(ct);
            foreach (var kvp in diagnostics)
            {
                allDiagnostics[kvp.Key] = kvp.Value;
            }
        }
        return allDiagnostics;
    }

    public async Task<List<JsonElement>> SearchSymbolsAsync(string query, CancellationToken ct = default)
    {
        var allSymbols = new List<JsonElement>();
        foreach (var client in _clients.Values)
        {
            var result = await client.WorkspaceSymbolAsync(query, ct);
            if (result.HasValue && result.Value.ValueKind == JsonValueKind.Array)
            {
                allSymbols.AddRange(result.Value.EnumerateArray());
            }
        }
        return allSymbols;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients.Values)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
        _clients.Clear();
    }
}
