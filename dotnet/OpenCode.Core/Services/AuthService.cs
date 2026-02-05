using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using OpenCode.Core.Attributes;

namespace OpenCode.Core.Services;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(OAuthAuthInfo), "oauth")]
[JsonDerivedType(typeof(ApiAuthInfo), "api")]
[JsonDerivedType(typeof(WellKnownAuthInfo), "wellknown")]
public abstract record AuthInfo
{
    [JsonIgnore]
    public abstract string Type { get; }
}

public record OAuthAuthInfo(
    string Refresh,
    string Access,
    long Expires,
    string? AccountId = null,
    string? EnterpriseUrl = null
) : AuthInfo
{
    [JsonIgnore]
    public override string Type => "oauth";
}

public record ApiAuthInfo(string Key) : AuthInfo
{
    [JsonIgnore]
    public override string Type => "api";
}

public record WellKnownAuthInfo(string Key, string Token) : AuthInfo
{
    [JsonIgnore]
    public override string Type => "wellknown";
}

[ServiceRegistration(ServiceLifetime.Singleton)]
public class AuthService
{
    private readonly string _authFile;
    private readonly ISecureStorage? _secureStorage;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AuthService(ISecureStorage? secureStorage = null)
    {
        _secureStorage = secureStorage;
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenCode");
        if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
        _authFile = Path.Combine(dataDir, "auth.json");
    }

    public async Task<AuthInfo?> GetAsync(string providerId)
    {
        if (_secureStorage != null)
        {
            var protectedJson = await _secureStorage.UnprotectAsync($"auth:{providerId}");
            if (protectedJson != null)
            {
                return JsonSerializer.Deserialize<AuthInfo>(protectedJson, Options);
            }
        }

        var auths = await AllAsync();
        return auths.TryGetValue(providerId, out var info) ? info : null;
    }

    public AuthInfo? Get(string providerId)
    {
        if (_secureStorage != null)
        {
            // Secure storage is usually inherently async, so we might still need to wait here
            // but for the file-based fallback we can be sync.
            var protectedJson = _secureStorage.UnprotectAsync($"auth:{providerId}").GetAwaiter().GetResult();
            if (protectedJson != null)
            {
                return JsonSerializer.Deserialize<AuthInfo>(protectedJson, Options);
            }
        }

        var auths = All();
        return auths.TryGetValue(providerId, out var info) ? info : null;
    }

    public async Task<Dictionary<string, AuthInfo>> AllAsync()
    {
        if (!File.Exists(_authFile)) return new Dictionary<string, AuthInfo>();

        try
        {
            var json = await File.ReadAllTextAsync(_authFile);
            return JsonSerializer.Deserialize<Dictionary<string, AuthInfo>>(json, Options) ?? new Dictionary<string, AuthInfo>();
        }
        catch
        {
            return new Dictionary<string, AuthInfo>();
        }
    }

    public Dictionary<string, AuthInfo> All()
    {
        if (!File.Exists(_authFile)) return new Dictionary<string, AuthInfo>();

        try
        {
            var json = File.ReadAllText(_authFile);
            return JsonSerializer.Deserialize<Dictionary<string, AuthInfo>>(json, Options) ?? new Dictionary<string, AuthInfo>();
        }
        catch
        {
            return new Dictionary<string, AuthInfo>();
        }
    }

    public async Task SetAsync(string providerId, AuthInfo info)
    {
        if (_secureStorage != null)
        {
            var json = JsonSerializer.Serialize(info, Options);
            await _secureStorage.ProtectAsync($"auth:{providerId}", json);
            
            // Remove from plain text if it exists
            var auths = await AllAsync();
            if (auths.Remove(providerId))
            {
                var newJson = JsonSerializer.Serialize(auths, Options);
                await File.WriteAllTextAsync(_authFile, newJson);
            }
        }
        else
        {
            var auths = await AllAsync();
            auths[providerId] = info;
            var json = JsonSerializer.Serialize(auths, Options);
            await File.WriteAllTextAsync(_authFile, json);
        }
    }

    public async Task RemoveAsync(string providerId)
    {
        if (_secureStorage != null)
        {
            await _secureStorage.RemoveAsync($"auth:{providerId}");
        }

        var auths = await AllAsync();
        if (auths.Remove(providerId))
        {
            var json = JsonSerializer.Serialize(auths, Options);
            await File.WriteAllTextAsync(_authFile, json);
        }
    }
}
