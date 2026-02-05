using System.Net.Http.Headers;
using OpenCode.Core.Services;

namespace OpenCode.Infrastructure.Plugins;

public class CopilotHttpClientHandler : DelegatingHandler
{
    private readonly AuthService _authService;

    public CopilotHttpClientHandler(AuthService authService, HttpMessageHandler innerHandler) : base(innerHandler)
    {
        _authService = authService;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Check if it's a GitHub Copilot request (often goes through api.github.com or copilot-api.github.com)
        if (request.RequestUri?.Host.Contains("github.com") == true)
        {
            var auth = await _authService.GetAsync("github-copilot");
            if (auth is OAuthAuthInfo oauth)
            {
                // Add headers
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", oauth.Access);
                request.Headers.Add("Openai-Intent", "conversation-edits");
                request.Headers.Add("User-Agent", "opencode/1.0.0");
                
                // x-initiator header could be added here if we had session context
                // For now, let's assume 'user' or use a default
                if (!request.Headers.Contains("x-initiator"))
                {
                    request.Headers.Add("x-initiator", "user");
                }
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
