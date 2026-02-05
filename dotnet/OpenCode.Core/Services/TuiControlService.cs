using System.Text.Json.Nodes;
using OpenCode.Core.Utilities;

namespace OpenCode.Core.Services;

public class TuiControlService
{
    private readonly AsyncQueue<TuiRequest> _requests = new();
    private readonly AsyncQueue<JsonNode?> _responses = new();

    public void PushRequest(string path, JsonNode? body)
    {
        _requests.Push(new TuiRequest(path, body));
    }

    public async Task<TuiRequest> GetNextRequestAsync(CancellationToken ct)
    {
        return await _requests.NextAsync(ct);
    }

    public void PushResponse(JsonNode? body)
    {
        _responses.Push(body);
    }

    public async Task<JsonNode?> GetNextResponseAsync(CancellationToken ct)
    {
        return await _responses.NextAsync(ct);
    }
}

public record TuiRequest(string Path, JsonNode? Body);
