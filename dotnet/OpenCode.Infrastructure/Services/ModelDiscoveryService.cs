using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace OpenCode.Infrastructure.Services;

public class ModelDiscoveryService
{
    private const string ModelsDevUrl = "https://models.dev/api/models";
    private readonly HttpClient _httpClient;
    private Dictionary<string, ModelsDevProvider>? _cache;

    public ModelDiscoveryService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<Dictionary<string, ModelsDevProvider>> GetModelsAsync(bool refresh = false)
    {
        if (_cache != null && !refresh) return _cache;

        try
        {
            var response = await _httpClient.GetFromJsonAsync<Dictionary<string, ModelsDevProvider>>(ModelsDevUrl);
            _cache = NormalizeProviders(response ?? new());
            return _cache;
        }
        catch
        {
            return new();
        }
    }

    private Dictionary<string, ModelsDevProvider> NormalizeProviders(Dictionary<string, ModelsDevProvider> providers)
    {
        if (providers.TryGetValue("openai", out var provider))
        {
            provider.Models = NormalizeOpenAiModels(provider.Models);
        }

        return providers;
    }

    private Dictionary<string, ModelsDevModel> NormalizeOpenAiModels(Dictionary<string, ModelsDevModel> models)
    {
        var preferred = new[]
        {
            "gpt-5",
            "gpt-5-mini",
            "gpt-5-nano",
            "gpt-5-chat-latest",
            "o1",
            "o1-mini",
            "o1-preview",
            "o3",
            "o3-pro",
            "o3-mini",
            "o3-mini-high"
        };

        var removed = new HashSet<string>(StringComparer.Ordinal) { "o4-mini", "O1", "O3" };
        var ordered = new Dictionary<string, ModelsDevModel>(StringComparer.Ordinal);

        foreach (var id in preferred)
        {
            if (models.TryGetValue(id, out var model))
            {
                ordered[id] = model;
                continue;
            }
        }

        foreach (var (id, model) in models.OrderBy(x => x.Key))
        {
            if (removed.Contains(id)) continue;
            if (ordered.ContainsKey(id)) continue;
            ordered[id] = model;
        }

        return ordered;
    }
}

public class ModelsDevProvider
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("models")]
    public Dictionary<string, ModelsDevModel> Models { get; set; } = new();
}

public class ModelsDevModel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("limit")]
    public ModelLimit Limit { get; set; } = new();
}

public class ModelLimit
{
    [JsonPropertyName("context")]
    public int Context { get; set; }

    [JsonPropertyName("max_output")]
    public int MaxOutput { get; set; }
}
