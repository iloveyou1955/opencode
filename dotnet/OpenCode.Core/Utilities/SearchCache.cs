using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using OpenCode.Core.Attributes;

namespace OpenCode.Core.Utilities;

/// <summary>
/// 为耗时的搜索操作提供缓存支持。
/// </summary>
[ServiceRegistration(ServiceLifetime.Singleton)]
public class SearchCache
{
    private readonly IMemoryCache _cache;

    public SearchCache()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
    }

    public T? Get<T>(string key)
    {
        return _cache.Get<T>(key);
    }

    public void Set<T>(string key, T value, TimeSpan? expiration = null)
    {
        _cache.Set(key, value, expiration ?? TimeSpan.FromSeconds(30));
    }

    public string GenerateKey(string tool, string pattern, string path, string? extra = null)
    {
        return $"{tool}:{pattern}:{path}:{extra ?? ""}";
    }
}
