using System.Net;
using Microsoft.Extensions.Logging;

using OpenCode.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.Core.Services;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class SessionRetryService
{
    private const int RetryInitialDelay = 2000;
    private const int RetryBackoffFactor = 2;
    private const int RetryMaxDelayNoHeaders = 30000;
    private readonly ILogger<SessionRetryService> _logger;

    public SessionRetryService(ILogger<SessionRetryService> logger)
    {
        _logger = logger;
    }

    public async Task SleepAsync(int ms, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Sleeping for {ms}ms before retry...", ms);
        await Task.Delay(ms, cancellationToken);
    }

    public int GetDelay(int attempt, HttpResponseMessage? response = null)
    {
        if (response != null && response.Headers.RetryAfter != null)
        {
            if (response.Headers.RetryAfter.Delta.HasValue)
            {
                return (int)response.Headers.RetryAfter.Delta.Value.TotalMilliseconds;
            }
            if (response.Headers.RetryAfter.Date.HasValue)
            {
                var delay = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                if (delay.TotalMilliseconds > 0)
                {
                    return (int)delay.TotalMilliseconds;
                }
            }
        }

        // Custom headers often used by AI providers
        if (response != null)
        {
            if (response.Headers.TryGetValues("retry-after-ms", out var values))
            {
                if (int.TryParse(values.FirstOrDefault(), out var ms)) return ms;
            }
        }

        var exponentialDelay = RetryInitialDelay * Math.Pow(RetryBackoffFactor, attempt - 1);
        return (int)Math.Min(exponentialDelay, RetryMaxDelayNoHeaders);
    }

    public bool IsRetryable(Exception ex)
    {
        // Simple heuristic for now, can be expanded based on provider-specific error bodies
        if (ex is HttpRequestException httpEx)
        {
            return httpEx.StatusCode == HttpStatusCode.TooManyRequests || 
                   httpEx.StatusCode == HttpStatusCode.ServiceUnavailable ||
                   httpEx.StatusCode == HttpStatusCode.GatewayTimeout;
        }
        
        var msg = ex.Message.ToLower();
        return msg.Contains("rate limit") || msg.Contains("too many requests") || msg.Contains("overloaded") || msg.Contains("unavailable");
    }
}
