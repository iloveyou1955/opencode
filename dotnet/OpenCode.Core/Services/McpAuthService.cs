using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCode.Core.Contracts;
using OpenCode.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.Core.Services;

public record McpTokens(
    string AccessToken,
    string? RefreshToken = null,
    long? ExpiresAt = null,
    string? Scope = null
);

public record McpClientInfo(
    string ClientId,
    string? ClientSecret = null,
    long? ClientIdIssuedAt = null,
    long? ClientSecretExpiresAt = null
);

public record McpAuthEntry(
    McpTokens? Tokens = null,
    McpClientInfo? ClientInfo = null,
    string? CodeVerifier = null,
    string? OAuthState = null,
    string? ServerUrl = null
);

[ServiceRegistration(ServiceLifetime.Singleton)]
public class McpAuthService
{
    private readonly string _authFile;
    private readonly ISecureStorage? _secureStorage;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public McpAuthService(ISecureStorage? secureStorage = null)
    {
        _secureStorage = secureStorage;
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenCode");
        if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
        _authFile = Path.Combine(dataDir, "mcp-auth.json");
    }

    public async Task<McpAuthEntry?> GetAsync(string mcpName)
    {
        var all = await AllAsync();
        return all.TryGetValue(mcpName, out var entry) ? entry : null;
    }

    public async Task<Dictionary<string, McpAuthEntry>> AllAsync()
    {
        if (!File.Exists(_authFile)) return new Dictionary<string, McpAuthEntry>();

        try
        {
            var json = await File.ReadAllTextAsync(_authFile);
            return JsonSerializer.Deserialize<Dictionary<string, McpAuthEntry>>(json, Options) ?? new Dictionary<string, McpAuthEntry>();
        }
        catch
        {
            return new Dictionary<string, McpAuthEntry>();
        }
    }

    public async Task SetAsync(string mcpName, McpAuthEntry entry)
    {
        var all = await AllAsync();
        all[mcpName] = entry;
        var json = JsonSerializer.Serialize(all, Options);
        await File.WriteAllTextAsync(_authFile, json);
    }

    public async Task UpdateTokensAsync(string mcpName, McpTokens tokens, string? serverUrl = null)
    {
        var entry = (await GetAsync(mcpName)) ?? new McpAuthEntry();
        entry = entry with { Tokens = tokens };
        if (serverUrl != null) entry = entry with { ServerUrl = serverUrl };
        await SetAsync(mcpName, entry);
    }

    public async Task UpdateClientInfoAsync(string mcpName, McpClientInfo info, string? serverUrl = null)
    {
        var entry = (await GetAsync(mcpName)) ?? new McpAuthEntry();
        entry = entry with { ClientInfo = info };
        if (serverUrl != null) entry = entry with { ServerUrl = serverUrl };
        await SetAsync(mcpName, entry);
    }

    public async Task UpdateCodeVerifierAsync(string mcpName, string codeVerifier)
    {
        var entry = (await GetAsync(mcpName)) ?? new McpAuthEntry();
        entry = entry with { CodeVerifier = codeVerifier };
        await SetAsync(mcpName, entry);
    }

    public async Task ClearCodeVerifierAsync(string mcpName)
    {
        var entry = await GetAsync(mcpName);
        if (entry != null)
        {
            entry = entry with { CodeVerifier = null };
            await SetAsync(mcpName, entry);
        }
    }

    public async Task UpdateOAuthStateAsync(string mcpName, string oauthState)
    {
        var entry = (await GetAsync(mcpName)) ?? new McpAuthEntry();
        entry = entry with { OAuthState = oauthState };
        await SetAsync(mcpName, entry);
    }

    public async Task<string?> GetOAuthStateAsync(string mcpName)
    {
        var entry = await GetAsync(mcpName);
        return entry?.OAuthState;
    }

    public async Task ClearOAuthStateAsync(string mcpName)
    {
        var entry = await GetAsync(mcpName);
        if (entry != null)
        {
            entry = entry with { OAuthState = null };
            await SetAsync(mcpName, entry);
        }
    }

    public async Task<bool?> IsTokenExpiredAsync(string mcpName)
    {
        var entry = await GetAsync(mcpName);
        if (entry?.Tokens == null) return null;
        if (entry.Tokens.ExpiresAt == null) return false;
        return entry.Tokens.ExpiresAt < DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
