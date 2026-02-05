using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;

using OpenCode.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.Core.Services;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class McpOAuthService
{
    private readonly ILogger<McpOAuthService> _logger;
    private readonly McpAuthService _mcpAuthService;
    private const int CallbackPort = 19876;
    private const string CallbackPath = "/mcp/oauth/callback";
    private HttpListener? _listener;
    private readonly Dictionary<string, TaskCompletionSource<string>> _pendingAuths = new();

    public McpOAuthService(McpAuthService mcpAuthService, ILogger<McpOAuthService> logger)
    {
        _mcpAuthService = mcpAuthService;
        _logger = logger;
    }

    public string RedirectUrl => $"http://127.0.0.1:{CallbackPort}{CallbackPath}";

    public async Task StartCallbackServerAsync()
    {
        if (_listener != null) return;

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{CallbackPort}/");
        _listener.Start();
        _logger.LogInformation("MCP OAuth callback server started on port {Port}", CallbackPort);

        _ = Task.Run(async () =>
        {
            while (_listener?.IsListening == true)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    await HandleRequestAsync(context);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling OAuth callback request");
                }
            }
        });
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        if (request.Url?.AbsolutePath != CallbackPath)
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            response.Close();
            return;
        }

        var query = request.QueryString;
        var code = query["code"];
        var state = query["state"];
        var error = query["error"];
        var errorDescription = query["error_description"];

        string html;
        if (!string.IsNullOrEmpty(error))
        {
            html = GetErrorHtml(errorDescription ?? error);
            if (state != null && _pendingAuths.TryGetValue(state, out var tcs))
            {
                tcs.TrySetException(new Exception(errorDescription ?? error));
                _pendingAuths.Remove(state);
            }
        }
        else if (string.IsNullOrEmpty(code))
        {
            html = GetErrorHtml("No authorization code provided");
        }
        else if (state == null || !_pendingAuths.TryGetValue(state, out var tcs))
        {
            html = GetErrorHtml("Invalid or expired state parameter");
        }
        else
        {
            html = GetSuccessHtml();
            tcs.TrySetResult(code);
            _pendingAuths.Remove(state);
        }

        var buffer = Encoding.UTF8.GetBytes(html);
        response.ContentLength64 = buffer.Length;
        response.ContentType = "text/html";
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        response.Close();
    }

    public async Task<string> WaitForCallbackAsync(string state, int timeoutSeconds = 300)
    {
        var tcs = new TaskCompletionSource<string>();
        _pendingAuths[state] = tcs;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        cts.Token.Register(() =>
        {
            tcs.TrySetException(new TimeoutException("OAuth callback timed out"));
            _pendingAuths.Remove(state);
        });

        return await tcs.Task;
    }

    private string GetSuccessHtml() => @"<!DOCTYPE html>
<html>
<head>
  <title>OpenCode - Authorization Successful</title>
  <style>
    body { font-family: system-ui, -apple-system, sans-serif; display: flex; justify-content: center; align-items: center; height: 100vh; margin: 0; background: #1a1a2e; color: #eee; }
    .container { text-align: center; padding: 2rem; }
    h1 { color: #4ade80; margin-bottom: 1rem; }
    p { color: #aaa; }
  </style>
</head>
<body>
  <div class='container'>
    <h1>Authorization Successful</h1>
    <p>You can close this window and return to OpenCode.</p>
  </div>
  <script>setTimeout(() => window.close(), 2000);</script>
</body>
</html>";

    private string GetErrorHtml(string error) => $@"<!DOCTYPE html>
<html>
<head>
  <title>OpenCode - Authorization Failed</title>
  <style>
    body {{ font-family: system-ui, -apple-system, sans-serif; display: flex; justify-content: center; align-items: center; height: 100vh; margin: 0; background: #1a1a2e; color: #eee; }}
    .container {{ text-align: center; padding: 2rem; }}
    h1 {{ color: #f87171; margin-bottom: 1rem; }}
    p {{ color: #aaa; }}
    .error {{ color: #fca5a5; font-family: monospace; margin-top: 1rem; padding: 1rem; background: rgba(248,113,113,0.1); border-radius: 0.5rem; }}
  </style>
</head>
<body>
  <div class='container'>
    <h1>Authorization Failed</h1>
    <p>An error occurred during authorization.</p>
    <div class='error'>{error}</div>
  </div>
</body>
</html>";

    public void Stop()
    {
        _listener?.Stop();
        _listener = null;
        foreach (var tcs in _pendingAuths.Values)
        {
            tcs.TrySetException(new Exception("OAuth callback server stopped"));
        }
        _pendingAuths.Clear();
    }
}
