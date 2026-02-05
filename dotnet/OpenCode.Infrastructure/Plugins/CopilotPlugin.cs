using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using OpenCode.Core.Contracts;
using OpenCode.Core.Models;
using OpenCode.Core.Services;
using System.Text;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;

namespace OpenCode.Infrastructure.Plugins;

public class CopilotPlugin : IPlugin
{
    public string Name => "copilot";
    public string Version => "1.0.0";

    public async Task<IPluginHooks> InitializeAsync(PluginInput input)
    {
        return new CopilotHooks(input);
    }

    private class CopilotHooks : IPluginHooks
    {
        private readonly PluginInput _input;
        private readonly AuthService _authService;
        private readonly ILogger _logger;
        private const string ClientId = "Ov23li8tweQw6odWQebz";
        private const string GitHubDomain = "github.com";

        public CopilotHooks(PluginInput input)
        {
            _input = input;
            _authService = input.ServiceProvider.GetRequiredService<AuthService>();
            _logger = input.ServiceProvider.GetRequiredService<ILogger<CopilotPlugin>>();
        }

        public Task OnConfigAsync(ConfigService config) => Task.CompletedTask;
        public Task OnEventAsync(BusEvent @event) => Task.CompletedTask;
        public Task OnToolCallAsync(string toolName, JsonObject args) => Task.CompletedTask;
        public Task OnSessionCompactingAsync(string sessionId, List<ChatMessage> history) => Task.CompletedTask;
        public Task<string> OnTextCompleteAsync(string prompt, string result) => Task.FromResult(result);
        public Task<PermissionResponse?> OnPermissionAskAsync(PermissionInfo info) => Task.FromResult<PermissionResponse?>(null);

        public Task OnChatOptionsAsync(ChatOptions options, string? sessionId = null)
        {
            // We can use AdditionalProperties to pass hints to CopilotHttpClientHandler
            if (sessionId != null)
            {
                options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
                options.AdditionalProperties["sessionId"] = sessionId;
            }
            return Task.CompletedTask;
        }

        public AuthHook? Auth => new AuthHook
        {
            Provider = "github-copilot",
            Methods = new List<AuthMethod>
            {
                new OAuthAuthMethod
                {
                    Label = "Login with GitHub Copilot",
                    AuthorizeAsync = AuthorizeOAuthAsync
                }
            }
        };

        private async Task<OAuthAuthorizeResult> AuthorizeOAuthAsync()
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("User-Agent", "opencode/1.0.0");

            var response = await client.PostAsJsonAsync($"https://{GitHubDomain}/login/device/code", new
            {
                client_id = ClientId,
                scope = "read:user"
            });

            if (!response.IsSuccessStatusCode)
                throw new Exception("Failed to initiate device authorization");

            var deviceData = await response.Content.ReadFromJsonAsync<DeviceCodeResponse>();
            if (deviceData == null) throw new Exception("Invalid response from GitHub");

            return new OAuthAuthorizeResult
            {
                Url = deviceData.VerificationUri,
                Instructions = $"Enter code: {deviceData.UserCode}",
                Method = "auto",
                CallbackAsync = async () =>
                {
                    var interval = deviceData.Interval > 0 ? deviceData.Interval : 5;
                    while (true)
                    {
                        await Task.Delay((interval + 3) * 1000); // 3s safety margin

                        var tokenResponse = await client.PostAsJsonAsync($"https://{GitHubDomain}/login/oauth/access_token", new
                        {
                            client_id = ClientId,
                            device_code = deviceData.DeviceCode,
                            grant_type = "urn:ietf:params:oauth:grant-type:device_code"
                        });

                        if (!tokenResponse.IsSuccessStatusCode) return new OAuthResult { Status = "failed" };

                        var tokenData = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>();
                        if (tokenData == null) return new OAuthResult { Status = "failed" };

                        if (!string.IsNullOrEmpty(tokenData.AccessToken))
                        {
                            return new OAuthResult
                            {
                                Status = "success",
                                AccessToken = tokenData.AccessToken,
                                RefreshToken = tokenData.AccessToken, // GitHub Copilot uses the same token for both
                                ExpiresAt = 0 // Never expires? (Actually GitHub tokens have long life)
                            };
                        }

                        if (tokenData.Error == "authorization_pending") continue;
                        if (tokenData.Error == "slow_down")
                        {
                            interval += 5;
                            continue;
                        }

                        return new OAuthResult { Status = "failed" };
                    }
                }
            };
        }

        private class DeviceCodeResponse
        {
            public string DeviceCode { get; set; } = "";
            public string UserCode { get; set; } = "";
            public string VerificationUri { get; set; } = "";
            public int Interval { get; set; }
        }

        private class TokenResponse
        {
            public string? AccessToken { get; set; }
            public string? Error { get; set; }
        }
    }
}
