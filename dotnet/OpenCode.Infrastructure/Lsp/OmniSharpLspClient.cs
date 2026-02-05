using System.Diagnostics;
using System.Text.Json;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OpenCode.Core.Lsp;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;

namespace OpenCode.Infrastructure.Lsp;

/// <summary>
/// 使用 OmniSharp.Extensions 实现的 LSP 客户端。
/// </summary>
public class OmniSharpLspClient : ILspClient
{
    private readonly string _serverPath;
    private readonly string[] _args;
    private Process? _process;
    private ILanguageClient? _client;
    private bool _isInitialized;
    private readonly Dictionary<string, List<Diagnostic>> _diagnostics = new();

    public OmniSharpLspClient(string serverPath, params string[] args)
    {
        _serverPath = serverPath;
        _args = args;
    }

    public async Task<bool> InitializeAsync(string rootPath, CancellationToken ct = default)
    {
        if (_isInitialized) return true;

        var startInfo = new ProcessStartInfo
        {
            FileName = _serverPath,
            Arguments = string.Join(" ", _args),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = rootPath
        };

        _process = Process.Start(startInfo) ?? throw new Exception($"无法启动 LSP 服务器: {_serverPath}");

        _client = LanguageClient.Create(options =>
        {
            options
                .WithInput(_process.StandardOutput.BaseStream)
                .WithOutput(_process.StandardInput.BaseStream)
                .WithRootPath(rootPath)
                .WithRootUri(DocumentUri.FromFileSystemPath(rootPath))
                .OnPublishDiagnostics(paramsObj =>
                {
                    var path = paramsObj.Uri.GetFileSystemPath();
                    _diagnostics[path] = paramsObj.Diagnostics.ToList();
                });
        });

        await _client.Initialize(ct).ConfigureAwait(false);

        _isInitialized = true;
        return true;
    }

    public async Task<JsonElement?> GoToDefinitionAsync(string filePath, int line, int character, CancellationToken ct = default)
    {
        EnsureInitialized();
        var result = await _client!.RequestDefinition(new DefinitionParams
        {
            TextDocument = new TextDocumentIdentifier(DocumentUri.FromFileSystemPath(filePath)),
            Position = new Position(line - 1, character - 1)
        }, ct);

        return ToJsonElement(result);
    }

    public async Task<JsonElement?> FindReferencesAsync(string filePath, int line, int character, CancellationToken ct = default)
    {
        EnsureInitialized();
        var result = await _client!.RequestReferences(new ReferenceParams
        {
            TextDocument = new TextDocumentIdentifier(DocumentUri.FromFileSystemPath(filePath)),
            Position = new Position(line - 1, character - 1),
            Context = new ReferenceContext { IncludeDeclaration = true }
        }, ct);

        return ToJsonElement(result);
    }

    public async Task<JsonElement?> HoverAsync(string filePath, int line, int character, CancellationToken ct = default)
    {
        EnsureInitialized();
        var result = await _client!.RequestHover(new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(DocumentUri.FromFileSystemPath(filePath)),
            Position = new Position(line - 1, character - 1)
        }, ct);

        return ToJsonElement(result);
    }

    public async Task<JsonElement?> DocumentSymbolAsync(string filePath, CancellationToken ct = default)
    {
        EnsureInitialized();
        var result = await _client!.RequestDocumentSymbol(new DocumentSymbolParams
        {
            TextDocument = new TextDocumentIdentifier(DocumentUri.FromFileSystemPath(filePath))
        }, ct);

        return ToJsonElement(result);
    }

    public async Task<JsonElement?> WorkspaceSymbolAsync(string query, CancellationToken ct = default)
    {
        EnsureInitialized();
        // 使用通用的 SendRequest
        var result = await _client!.SendRequest(new WorkspaceSymbolParams
        {
            Query = query
        }, ct);

        return ToJsonElement(result);
    }

    public async Task<JsonElement?> GoToImplementationAsync(string filePath, int line, int character, CancellationToken ct = default)
    {
        EnsureInitialized();
        var result = await _client!.RequestImplementation(new ImplementationParams
        {
            TextDocument = new TextDocumentIdentifier(DocumentUri.FromFileSystemPath(filePath)),
            Position = new Position(line - 1, character - 1)
        }, ct);

        return ToJsonElement(result);
    }

    public Task<Dictionary<string, JsonElement>> GetDiagnosticsAsync(CancellationToken ct = default)
    {
        var result = new Dictionary<string, JsonElement>();
        foreach (var kvp in _diagnostics)
        {
            var element = ToJsonElement(kvp.Value);
            if (element.HasValue)
            {
                result[kvp.Key] = element.Value;
            }
        }
        return Task.FromResult(result);
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized || _client == null)
        {
            throw new InvalidOperationException("LSP 客户端尚未初始化。");
        }
    }

    private static JsonElement? ToJsonElement(object? obj)
    {
        if (obj == null) return null;
        var json = JsonSerializer.Serialize(obj);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client != null)
        {
            await _client.Shutdown();
            _client.Dispose();
        }

        if (_process != null && !_process.HasExited)
        {
            _process.Kill();
            _process.Dispose();
        }
    }
}
