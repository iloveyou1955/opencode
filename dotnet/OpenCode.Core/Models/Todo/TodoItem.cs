using System.Text.Json.Serialization;

namespace OpenCode.Core.Models.Todo;

public class TodoItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending"; // pending, in_progress, completed

    [JsonPropertyName("priority")]
    public string Priority { get; set; } = "medium"; // low, medium, high
}
