using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using OpenCode.Core.Contracts;
using OpenCode.Core.Models;
using OpenCode.Core.Services;
using System.Text;
using System.Security.Cryptography;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.Infrastructure.Plugins;

public class CodexPlugin : IPlugin
{
    public string Name => "codex";
    public string Version => "1.0.0";

    public async Task<IPluginHooks> InitializeAsync(PluginInput input)
    {
        return new CodexHooks(input);
    }

    private class CodexHooks : IPluginHooks
    {
        private readonly PluginInput _input;
        private readonly AuthService _authService;
        private readonly McpOAuthCallback _callback;
        private readonly ILogger _logger;
        private const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann";
        private const string Issuer = "https://auth.openai.com";
        private const string CodexApiEndpoint = "https://chatgpt.com/backend-api/codex/responses";

        public CodexHooks(PluginInput input)
        {
            _input = input;
            _authService = input.ServiceProvider.GetRequiredService<AuthService>();
            _callback = input.ServiceProvider.GetRequiredService<McpOAuthCallback>();
            _logger = input.ServiceProvider.GetRequiredService<ILogger<CodexPlugin>>();
        }

        public Task OnConfigAsync(ConfigService config) => Task.CompletedTask;
        public Task OnEventAsync(BusEvent @event) => Task.CompletedTask;
        public Task OnToolCallAsync(string toolName, JsonObject args) => Task.CompletedTask;
        public Task OnSessionCompactingAsync(string sessionId, List<ChatMessage> history) => Task.CompletedTask;
        
        public Task<string> OnTextCompleteAsync(string prompt, string result) => Task.FromResult(result);
        public Task<PermissionResponse?> OnPermissionAskAsync(PermissionInfo info) => Task.FromResult<PermissionResponse?>(null);
        public Task OnChatOptionsAsync(ChatOptions options, string? sessionId = null) => Task.CompletedTask;

        public AuthHook? Auth => new AuthHook
        {
            Provider = "openai",
            Methods = new List<AuthMethod>
            {
                new OAuthAuthMethod
                {
                    Label = "ChatGPT Pro/Plus (browser)",
                    AuthorizeAsync = AuthorizeOAuthAsync
                },
                new ApiAuthMethod
                {
                    Label = "Manually enter API Key"
                }
            }
        };

        private async Task<OAuthAuthorizeResult> AuthorizeOAuthAsync()
        {
            var pkce = GeneratePKCE();
            var state = GenerateState();
            var redirectUri = "http://localhost:1455/auth/callback";
            
            var url = BuildAuthorizeUrl(redirectUri, pkce, state);
            
            _callback.Start();

            return new OAuthAuthorizeResult
            {
                Url = url,
                Instructions = "Complete authorization in your browser. This window will close automatically.",
                Method = "auto",
                CallbackAsync = async () =>
                {
                    var code = await _callback.WaitForCallbackAsync(state);
                    var tokens = await ExchangeCodeForTokensAsync(code, redirectUri, pkce);
                    
                    return new OAuthResult
                    {
                        Status = "success",
                        AccessToken = tokens.Access,
                        RefreshToken = tokens.Refresh,
                        ExpiresAt = tokens.Expires,
                        AccountId = ExtractAccountId(tokens.Access)
                    };
                }
            };
        }

        private string BuildAuthorizeUrl(string redirectUri, (string Verifier, string Challenge) pkce, string state)
        {
            var query = new StringBuilder();
            query.Append("?response_type=code");
            query.Append($"&client_id={ClientId}");
            query.Append($"&redirect_uri={Uri.EscapeDataString(redirectUri)}");
            query.Append("&scope=openid%20profile%20email%20offline_access");
            query.Append($"&code_challenge={pkce.Challenge}");
            query.Append("&code_challenge_method=S256");
            query.Append("&id_token_add_organizations=true");
            query.Append("&codex_cli_simplified_flow=true");
            query.Append($"&state={state}");
            query.Append("&originator=opencode");

            return $"{Issuer}/oauth/authorize{query}";
        }

        private async Task<OAuthAuthInfo> ExchangeCodeForTokensAsync(string code, string redirectUri, (string Verifier, string Challenge) pkce)
        {
            using var client = new HttpClient();
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = ClientId,
                ["code_verifier"] = pkce.Verifier
            });

            var response = await client.PostAsync($"{Issuer}/oauth/token", content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(json);

            return new OAuthAuthInfo(
                Refresh: node?["refresh_token"]?.ToString() ?? "",
                Access: node?["access_token"]?.ToString() ?? "",
                Expires: DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (node?["expires_in"]?.GetValue<int>() ?? 3600)
            );
        }

        private string? ExtractAccountId(string token)
        {
            // Simplified JWT parsing
            try
            {
                var parts = token.Split('.');
                if (parts.Length != 3) return null;
                var payload = parts[1];
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(PadBase64(payload)));
                var node = JsonNode.Parse(decoded);
                return node?["chatgpt_account_id"]?.ToString() ?? node?["organizations"]?[0]?["id"]?.ToString();
            }
            catch { return null; }
        }

        private string PadBase64(string s) => s.Length % 4 == 0 ? s : s + new string('=', 4 - s.Length % 4);

        private (string Verifier, string Challenge) GeneratePKCE()
        {
            var bytes = new byte[32];
            RandomNumberGenerator.Fill(bytes);
            var verifier = Base64UrlEncode(bytes);
            
            using var sha256 = SHA256.Create();
            var challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(verifier));
            var challenge = Base64UrlEncode(challengeBytes);
            
            return (verifier, challenge);
        }

        private string GenerateState()
        {
            var bytes = new byte[32];
            RandomNumberGenerator.Fill(bytes);
            return BitConverter.ToString(bytes).Replace("-", "").ToLower();
        }

        private string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");
        }
    }
}
