using System.Text.Json.Serialization;

namespace OpenCode.Core.Services;

public class ConfigInfo
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("small_model")]
    public string? SmallModel { get; set; }

    [JsonPropertyName("default_agent")]
    public string? DefaultAgent { get; set; }

    [JsonPropertyName("provider")]
    public Dictionary<string, ProviderConfig>? Providers { get; set; }

    [JsonPropertyName("agent")]
    public Dictionary<string, AgentConfig>? Agents { get; set; }

    [JsonPropertyName("permission")]
    public Dictionary<string, object>? Permissions { get; set; }

    [JsonPropertyName("tools")]
    public Dictionary<string, bool>? Tools { get; set; }

    [JsonPropertyName("lsp")]
    public Dictionary<string, LspConfig>? Lsp { get; set; }

    [JsonPropertyName("mcp")]
    public Dictionary<string, McpConfig>? Mcp { get; set; }

    [JsonPropertyName("compaction")]
    public CompactionConfig? Compaction { get; set; }
}

public class CompactionConfig
{
    [JsonPropertyName("auto")]
    public bool Auto { get; set; } = false;

    [JsonPropertyName("prune")]
    public bool Prune { get; set; } = false;
}

public class McpConfig
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "local"; // local, sse, http

    [JsonPropertyName("command")]
    public string[]? Command { get; set; }

    [JsonPropertyName("args")]
    public string[]? Args { get; set; }

    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("auth")]
    public string? Auth { get; set; } // "oauth"

    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }
}

public class LspConfig
{
    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("args")]
    public string[]? Args { get; set; }

    [JsonPropertyName("languages")]
    public string[]? Languages { get; set; }
}

public class ProviderConfig
{
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("baseURL")]
    public string? BaseUrl { get; set; }

    [JsonPropertyName("models")]
    public Dictionary<string, object>? Models { get; set; }
}

public class AgentConfig
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("mode")]
    public string? Mode { get; set; }
}
