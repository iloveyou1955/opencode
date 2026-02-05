using System.Net.Http.Headers;
using OpenCode.Core.Services;

namespace OpenCode.Infrastructure.Plugins;

public class CodexHttpClientHandler : DelegatingHandler
{
    private readonly AuthService _authService;
    private const string CodexApiEndpoint = "https://chatgpt.com/backend-api/codex/responses";

    public CodexHttpClientHandler(AuthService authService, HttpMessageHandler innerHandler) : base(innerHandler)
    {
        _authService = authService;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Check if it's an OpenAI request
        if (request.RequestUri?.Host == "api.openai.com")
        {
            var auth = await _authService.GetAsync("openai");
            if (auth is OAuthAuthInfo oauth)
            {
                // Check if token expired
                if (oauth.Expires < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                {
                    // Refresh token logic would go here
                    // For now, let's assume it's fresh or handled elsewhere
                }

                // Rewrite URL
                if (request.RequestUri.AbsolutePath.Contains("/v1/chat/completions"))
                {
                    request.RequestUri = new Uri(CodexApiEndpoint);
                }

                // Add headers
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", oauth.Access);
                if (!string.IsNullOrEmpty(oauth.AccountId))
                {
                    request.Headers.Add("ChatGPT-Account-Id", oauth.AccountId);
                }
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
