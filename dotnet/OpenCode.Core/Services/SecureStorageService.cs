using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;

namespace OpenCode.Core.Services;

public interface ISecureStorage
{
    Task ProtectAsync(string key, string data);
    Task<string?> UnprotectAsync(string key);
    Task RemoveAsync(string key);
}

[SupportedOSPlatform("windows")]
public class SecureStorageService : ISecureStorage
{
    private readonly string _basePath;

    public SecureStorageService()
    {
        _basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenCode", "secure");
        if (!Directory.Exists(_basePath)) Directory.CreateDirectory(_basePath);
    }

    public async Task ProtectAsync(string key, string data)
    {
        var filePath = GetFilePath(key);
        var bytes = Encoding.UTF8.GetBytes(data);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(filePath, protectedBytes);
    }

    public async Task<string?> UnprotectAsync(string key)
    {
        var filePath = GetFilePath(key);
        if (!File.Exists(filePath)) return null;

        var protectedBytes = await File.ReadAllBytesAsync(filePath);
        try
        {
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    public Task RemoveAsync(string key)
    {
        var filePath = GetFilePath(key);
        if (File.Exists(filePath)) File.Delete(filePath);
        return Task.CompletedTask;
    }

    private string GetFilePath(string key)
    {
        // Simple hash to avoid invalid filename characters
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
        var fileName = Convert.ToHexString(hash);
        return Path.Combine(_basePath, fileName);
    }
}
