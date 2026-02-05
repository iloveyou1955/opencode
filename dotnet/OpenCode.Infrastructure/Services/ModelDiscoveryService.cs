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
            _cache = response ?? new();
            return _cache;
        }
        catch
        {
            return new();
        }
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
