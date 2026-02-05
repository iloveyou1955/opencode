using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;

using OpenCode.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.Core.Services;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class McpOAuthCallback : IDisposable
{
    private HttpListener? _listener;
    private readonly Dictionary<string, TaskCompletionSource<string>> _pendingAuths = new();
    private readonly ILogger<McpOAuthCallback> _logger;
    private const int Port = 1455;
    private const string Path = "/auth/callback";

    public McpOAuthCallback(ILogger<McpOAuthCallback> logger)
    {
        _logger = logger;
    }

    public void Start()
    {
        if (_listener != null) return;

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        _listener.Start();
        
        _logger.LogInformation("OAuth callback server started on port {Port}", Port);

        Task.Run(ListenAsync);
    }

    private async Task ListenAsync()
    {
        while (_listener != null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                var request = context.Request;
                var response = context.Response;

                if (request.Url?.AbsolutePath != Path)
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    response.Close();
                    continue;
                }

                var code = request.QueryString["code"];
                var state = request.QueryString["state"];
                var error = request.QueryString["error"];
                var errorDescription = request.QueryString["error_description"];

                _logger.LogInformation("Received OAuth callback. HasCode: {HasCode}, State: {State}, Error: {Error}", !string.IsNullOrEmpty(code), state, error);

                if (string.IsNullOrEmpty(state))
                {
                    await SendResponseAsync(response, HttpStatusCode.BadRequest, GetErrorHtml("Missing state parameter"));
                    continue;
                }

                if (!string.IsNullOrEmpty(error))
                {
                    var msg = errorDescription ?? error;
                    if (_pendingAuths.TryGetValue(state, out var tcs))
                    {
                        tcs.TrySetException(new Exception(msg));
                        _pendingAuths.Remove(state);
                    }
                    await SendResponseAsync(response, HttpStatusCode.OK, GetErrorHtml(msg));
                    continue;
                }

                if (string.IsNullOrEmpty(code))
                {
                    await SendResponseAsync(response, HttpStatusCode.BadRequest, GetErrorHtml("No authorization code provided"));
                    continue;
                }

                if (_pendingAuths.TryGetValue(state, out var tcs2))
                {
                    tcs2.TrySetResult(code);
                    _pendingAuths.Remove(state);
                    await SendResponseAsync(response, HttpStatusCode.OK, GetSuccessHtml());
                }
                else
                {
                    await SendResponseAsync(response, HttpStatusCode.BadRequest, GetErrorHtml("Invalid or expired state parameter"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling OAuth callback");
            }
        }
    }

    public Task<string> WaitForCallbackAsync(string state, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<string>();
        _pendingAuths[state] = tcs;

        ct.Register(() => {
            if (_pendingAuths.Remove(state))
            {
                tcs.TrySetCanceled();
            }
        });

        return tcs.Task;
    }

    private async Task SendResponseAsync(HttpListenerResponse response, HttpStatusCode status, string html)
    {
        var buffer = Encoding.UTF8.GetBytes(html);
        response.StatusCode = (int)status;
        response.ContentType = "text/html";
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        response.Close();
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
  <div class=""container"">
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
  <div class=""container"">
    <h1>Authorization Failed</h1>
    <p>An error occurred during authorization.</p>
    <div class=""error"">{error}</div>
  </div>
</body>
</html>";

    public void Dispose()
    {
        _listener?.Stop();
        _listener?.Close();
        _listener = null;
    }
}
