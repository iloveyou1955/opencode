using System.Text.Json.Nodes;
using OpenCode.Core.Contracts;

namespace OpenCode.Infrastructure.Tools;

/// <summary>
/// Invalid Tool, used to report malformed tool calls.
/// </summary>
public class InvalidTool : ITool
{
    public string Name => "invalid";
    public string Description => "Report an invalid tool call.";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {
        "message": { "type": "string", "description": "The error message" }
      },
      "required": ["message"]
    }
    """;

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken cancellationToken = default)
    {
        string? message = args["message"]?.ToString();
        return $"Error: {message ?? "Invalid tool call format"}";
    }
}
