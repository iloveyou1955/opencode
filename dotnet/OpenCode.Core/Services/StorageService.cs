using System.Text.Json;
using Microsoft.Extensions.Logging;

using OpenCode.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.Core.Services;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class StorageService
{
    private readonly string _baseDir;
    private readonly ILogger<StorageService> _logger;
    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public StorageService(ILogger<StorageService> logger)
    {
        _baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenCode", "storage");
        _logger = logger;
        
        if (!Directory.Exists(_baseDir))
        {
            Directory.CreateDirectory(_baseDir);
        }
    }

    private string GetPath(params string[] key)
    {
        var relativePath = Path.Combine(key);
        var fullPath = Path.Combine(_baseDir, relativePath + ".json");
        var dir = Path.GetDirectoryName(fullPath);
        if (dir != null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return fullPath;
    }

    public async Task WriteAsync<T>(T content, params string[] key)
    {
        var path = GetPath(key);
        var json = JsonSerializer.Serialize(content, _options);
        await File.WriteAllTextAsync(path, json);
    }

    public async Task<T?> ReadAsync<T>(params string[] key)
    {
        var path = GetPath(key);
        if (!File.Exists(path)) return default;

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<T>(json, _options);
    }

    public async Task UpdateAsync<T>(Action<T> updateFn, params string[] key)
    {
        var content = await ReadAsync<T>(key);
        if (content == null) return;
        updateFn(content);
        await WriteAsync(content, key);
    }

    public async Task RemoveAsync(params string[] key)
    {
        var path = GetPath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public async Task UpsertAsync<T>(T content, params string[] key)
    {
        await WriteAsync(content, key);
    }

    public async Task<bool> ExistsAsync(params string[] key)
    {
        var path = GetPath(key);
        return await Task.FromResult(File.Exists(path));
    }

    public async Task<List<string[]>> ListAsync(params string[] prefix)
    {
        var dir = prefix.Length > 0 ? Path.Combine(_baseDir, Path.Combine(prefix)) : _baseDir;
        if (!Directory.Exists(dir)) return new List<string[]>();

        var files = Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories);
        return await Task.FromResult(files.Select(f => {
            var relative = Path.GetRelativePath(_baseDir, f);
            var keyStr = relative.Substring(0, relative.Length - 5); // remove .json
            return keyStr.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        }).ToList());
    }
}
