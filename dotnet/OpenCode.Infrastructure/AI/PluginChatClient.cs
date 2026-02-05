using Microsoft.Extensions.AI;
using OpenCode.Core.Services;
using System.Text.Json.Nodes;
using System.Runtime.CompilerServices;

namespace OpenCode.Infrastructure.AI;

public class PluginChatClient : DelegatingChatClient
{
    private readonly PluginService _pluginService;

    public PluginChatClient(IChatClient innerClient, PluginService pluginService) : base(innerClient)
    {
        _pluginService = pluginService;
    }

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (options != null)
        {
            await _pluginService.TriggerChatOptionsAsync(options);
        }

        var response = await base.GetResponseAsync(chatMessages, options, cancellationToken);

        // Post-processing
        var result = response.ToString();
        var prompt = string.Join("\n", chatMessages.Select(m => m.Text));
        
        var modifiedResult = await _pluginService.TriggerTextCompleteAsync(prompt, result);
        
        if (modifiedResult != result)
        {
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, modifiedResult));
        }

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (options != null)
        {
            await _pluginService.TriggerChatOptionsAsync(options);
        }

        // Streaming interception is more complex. 
        // For simplicity, let's just pass through for now or collect and modify at the end.
        await foreach (var update in base.GetStreamingResponseAsync(chatMessages, options, cancellationToken))
        {
            yield return update;
        }
    }
}
