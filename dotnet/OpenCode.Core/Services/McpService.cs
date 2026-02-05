using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using OpenCode.Core.Contracts;

using OpenCode.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.Core.Services;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class McpService : IDisposable
{
    private readonly ConfigService _configService;
    private readonly McpOAuthService _oauthService;
    private readonly McpAuthService _authService;
    private readonly ILogger<McpService> _logger;
    private readonly Dictionary<string, McpClient> _clients = new();

    public McpService(ConfigService configService, McpOAuthService oauthService, McpAuthService authService, ILogger<McpService> logger)
    {
        _configService = configService;
        _oauthService = oauthService;
        _authService = authService;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        var mcpConfigs = _configService.Config.Mcp;
        if (mcpConfigs == null) return;

        foreach (var (name, config) in mcpConfigs)
        {
            if (!config.Enabled) continue;

            try
            {
                McpClient? client = null;
                if (config.Type == "local" && config.Command != null && config.Command.Length > 0)
                {
                    client = new LocalMcpClient(name, config, _logger);
                }
                else if (config.Type == "remote" && !string.IsNullOrEmpty(config.Url))
                {
                    client = new RemoteMcpClient(name, config, _logger);
                }

                if (client != null)
                {
                    await client.StartAsync();
                    _clients[name] = client;
                    _logger.LogInformation("MCP server {Name} ({Type}) started", name, config.Type);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start MCP server {Name}", name);
            }
        }
    }

    public async Task<IEnumerable<JsonObject>> ListToolsAsync()
    {
        var allTools = new List<JsonObject>();
        foreach (var client in _clients.Values)
        {
            var tools = await client.ListToolsAsync();
            foreach (var tool in tools)
            {
                tool["name"] = $"{client.Name}_{tool["name"]}";
                allTools.Add(tool);
            }
        }
        return allTools;
    }

    public async Task<IEnumerable<JsonObject>> ListResourcesAsync()
    {
        var allResources = new List<JsonObject>();
        foreach (var client in _clients.Values)
        {
            var resources = await client.ListResourcesAsync();
            foreach (var res in resources)
            {
                res["name"] = $"{client.Name}_{res["name"]}";
                allResources.Add(res);
            }
        }
        return allResources;
    }

    public async Task<IEnumerable<JsonObject>> ListPromptsAsync()
    {
        var allPrompts = new List<JsonObject>();
        foreach (var client in _clients.Values)
        {
            var prompts = await client.ListPromptsAsync();
            foreach (var p in prompts)
            {
                p["name"] = $"{client.Name}_{p["name"]}";
                allPrompts.Add(p);
            }
        }
        return allPrompts;
    }

    public IEnumerable<McpServerInfo> GetServers()
    {
        var configs = _configService.Config.Mcp;
        if (configs == null) return Enumerable.Empty<McpServerInfo>();

        return configs.Select(kvp => new McpServerInfo
        {
            Name = kvp.Key,
            Type = kvp.Value.Type,
            IsConnected = _clients.ContainsKey(kvp.Key)
        });
    }

    public async Task<string> CallToolAsync(string toolName, JsonObject args)
    {
        var parts = toolName.Split('_', 2);
        if (parts.Length < 2) throw new ArgumentException("Invalid MCP tool name format");

        var clientName = parts[0];
        var realToolName = parts[1];

        if (_clients.TryGetValue(clientName, out var client))
        {
            return await client.CallToolAsync(realToolName, args);
        }

        throw new KeyNotFoundException($"MCP client {clientName} not found");
    }

    public bool IsMcpTool(string toolName)
    {
        var parts = toolName.Split('_', 2);
        return parts.Length == 2 && _clients.ContainsKey(parts[0]);
    }

    public async Task<string?> StartAuthAsync(string mcpName)
    {
        var mcpConfigs = _configService.Config.Mcp;
        if (mcpConfigs == null || !mcpConfigs.TryGetValue(mcpName, out var config) || config.Type != "remote")
        {
            throw new Exception($"MCP server '{mcpName}' not found or not a remote server");
        }

        var state = Guid.NewGuid().ToString("N");
        await _authService.UpdateOAuthStateAsync(mcpName, state);
        await _oauthService.StartCallbackServerAsync();

        // Construct authorization URL
        // Note: In a real implementation, we'd fetch the auth endpoint from the server's initialize response or well-known
        var authUrl = $"{config.Url}/auth?response_type=code&client_id=opencode&redirect_uri={Uri.EscapeDataString(_oauthService.RedirectUrl)}&state={state}";
        return authUrl;
    }

    public async Task FinishAuthAsync(string mcpName, string code)
    {
        var mcpConfigs = _configService.Config.Mcp;
        if (mcpConfigs == null || !mcpConfigs.TryGetValue(mcpName, out var config)) return;

        // Exchange code for tokens
        // This is a simplified implementation
        using var client = new HttpClient();
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", _oauthService.RedirectUrl),
            new KeyValuePair<string, string>("client_id", "opencode")
        });

        var response = await client.PostAsync($"{config.Url}/token", content);
        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync();
            var tokens = JsonSerializer.Deserialize<McpTokens>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            if (tokens != null)
            {
                await _authService.UpdateTokensAsync(mcpName, tokens, config.Url);
                _logger.LogInformation("Successfully authenticated MCP server {Name}", mcpName);
            }
        }
        else
        {
            throw new Exception($"Failed to exchange code for tokens: {response.ReasonPhrase}");
        }
    }

    public void Dispose()
    {
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }
    }

    private abstract class McpClient : IDisposable
    {
        public string Name { get; }
        protected readonly McpConfig Config;
        protected readonly ILogger Logger;
        protected int RequestId = 1;
        protected readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode>> PendingRequests = new();

        protected McpClient(string name, McpConfig config, ILogger logger)
        {
            Name = name;
            Config = config;
            Logger = logger;
        }

        public abstract Task StartAsync();

        protected async Task InitializeAsync()
        {
            var id = RequestId++;
            var request = new
            {
                jsonrpc = "2.0",
                id = id,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = "OpenCode-DotNet", version = "1.0.0" }
                }
            };

            var tcs = new TaskCompletionSource<JsonNode>();
            PendingRequests[id] = tcs;

            await SendRawRequestAsync(JsonSerializer.Serialize(request));
            var result = await tcs.Task;

            // Notify initialized
            var notify = new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized"
            };
            await SendRawRequestAsync(JsonSerializer.Serialize(notify));
        }

        protected abstract Task SendRawRequestAsync(string json);

        protected void HandleResponse(string json)
        {
            try
            {
                var node = JsonNode.Parse(json);
                if (node?["id"] != null && int.TryParse(node["id"]!.ToString(), out var id))
                {
                    if (PendingRequests.TryRemove(id, out var tcs))
                    {
                        if (node["error"] != null)
                        {
                            tcs.SetException(new Exception(node["error"]!["message"]?.ToString() ?? "Unknown MCP error"));
                        }
                        else
                        {
                            tcs.SetResult(node["result"]!);
                        }
                    }
                }
            }
            catch { }
        }

        public async Task<IEnumerable<JsonObject>> ListToolsAsync()
        {
            var result = await SendRequestAsync("tools/list", new { });
            return result?["tools"]?.AsArray().Select(t => t!.AsObject()) ?? Enumerable.Empty<JsonObject>();
        }

        public async Task<IEnumerable<JsonObject>> ListResourcesAsync()
        {
            var result = await SendRequestAsync("resources/list", new { });
            return result?["resources"]?.AsArray().Select(t => t!.AsObject()) ?? Enumerable.Empty<JsonObject>();
        }

        public async Task<IEnumerable<JsonObject>> ListPromptsAsync()
        {
            var result = await SendRequestAsync("prompts/list", new { });
            return result?["prompts"]?.AsArray().Select(t => t!.AsObject()) ?? Enumerable.Empty<JsonObject>();
        }

        public async Task<string> CallToolAsync(string toolName, JsonObject args)
        {
            var result = await SendRequestAsync("tools/call", new
            {
                name = toolName,
                @arguments = args
            });

            if (result?["content"] != null)
            {
                var content = result["content"]!.AsArray();
                var textParts = content.Where(c => c?["type"]?.ToString() == "text").Select(c => c?["text"]?.ToString());
                return string.Join("\n", textParts);
            }

            return result?.ToString() ?? "Error: Tool call failed";
        }

        protected async Task<JsonNode?> SendRequestAsync(string method, object @params)
        {
            var id = RequestId++;
            var request = new
            {
                jsonrpc = "2.0",
                id = id,
                method = method,
                @params = @params
            };

            var tcs = new TaskCompletionSource<JsonNode>();
            PendingRequests[id] = tcs;

            await SendRawRequestAsync(JsonSerializer.Serialize(request));
            return await tcs.Task;
        }

        public abstract void Dispose();
    }

    private class LocalMcpClient : McpClient
    {
        private Process? _process;

        public LocalMcpClient(string name, McpConfig config, ILogger logger) : base(name, config, logger) { }

        public override async Task StartAsync()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Config.Command![0],
                Arguments = string.Join(" ", Config.Command.Skip(1).Concat(Config.Args ?? Array.Empty<string>())),
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (Config.Env != null)
            {
                foreach (var (k, v) in Config.Env) startInfo.Environment[k] = v;
            }

            _process = new Process { StartInfo = startInfo };
            _process.OutputDataReceived += (s, e) => {
                if (!string.IsNullOrEmpty(e.Data)) HandleResponse(e.Data);
            };
            _process.ErrorDataReceived += (s, e) => {
                if (!string.IsNullOrEmpty(e.Data)) Logger.LogWarning("MCP {Name} stderr: {Message}", Name, e.Data);
            };

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            await InitializeAsync();
        }

        protected override async Task SendRawRequestAsync(string json)
        {
            if (_process?.HasExited == false)
            {
                await _process.StandardInput.WriteLineAsync(json);
            }
        }

        public override void Dispose()
        {
            try { _process?.Kill(); } catch { }
            _process?.Dispose();
        }
    }

    private class RemoteMcpClient : McpClient
    {
        private readonly HttpClient _httpClient;
        private string? _endpoint;

        public RemoteMcpClient(string name, McpConfig config, ILogger logger) : base(name, config, logger)
        {
            _httpClient = new HttpClient();
            if (config.Headers != null)
            {
                foreach (var (k, v) in config.Headers) _httpClient.DefaultRequestHeaders.Add(k, v);
            }
        }

        public override async Task StartAsync()
        {
            // Initial connection to establish SSE
            var url = Config.Url!;
            var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            
            // Note: Simple implementation, assuming SSE or direct HTTP mapping
            // For full SSE support, we'd need to listen to the stream.
            // Here we'll just try to use the base URL for JSON-RPC.
            _endpoint = url;

            await InitializeAsync();
        }

        protected override async Task SendRawRequestAsync(string json)
        {
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(_endpoint, content);
            var resultJson = await response.Content.ReadAsStringAsync();
            HandleResponse(resultJson);
        }

        public override void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
