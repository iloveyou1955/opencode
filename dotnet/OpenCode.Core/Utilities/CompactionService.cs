using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenCode.Core.Services;
using OpenCode.Core.Attributes;

namespace OpenCode.Core.Utilities;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class CompactionService
{
    private readonly IChatClient _chatClient;
    private readonly PromptService? _promptService;
    private string? _compactionPrompt;

    public CompactionService(IChatClient chatClient, string? compactionPrompt = null, PromptService? promptService = null)
    {
        _chatClient = chatClient;
        _compactionPrompt = compactionPrompt;
        _promptService = promptService;
    }

    public bool IsOverflow(int inputTokens, int outputTokens, int contextLimit, int maxOutputTokens = 4096)
    {
        if (contextLimit <= 0) return false;
        
        var total = inputTokens + outputTokens;
        var usable = contextLimit - maxOutputTokens;
        
        return total > usable;
    }

    public async Task<string> CompactAsync(IEnumerable<ChatMessage> history, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_compactionPrompt) && _promptService != null)
        {
            _compactionPrompt = await _promptService.GetPromptAsync("compaction");
        }

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, _compactionPrompt ?? "Summarize the conversation.")
        };
        
        var historyText = string.Join("\n", history.Select(m => $"{m.Role}: {m.Text}"));
        messages.Add(new ChatMessage(ChatRole.User, $"Please compact the following conversation history into a concise summary that preserves all key information and current state:\n\n{historyText}"));

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
        
        // In Microsoft.Extensions.AI, the response can often be implicitly converted to string or has a Text property
        return response?.ToString() ?? "Summary generation failed.";
    }

    /// <summary>
    /// Prunes the history by trimming large tool outputs, keeping recent context.
    /// </summary>
    public List<ChatMessage> Prune(IEnumerable<ChatMessage> history, int pruneProtectTokens = 40000)
    {
        var msgs = history.ToList();
        var pruned = new List<ChatMessage>();
        int totalTokens = 0;
        
        // Go backwards through history
        for (int i = msgs.Count - 1; i >= 0; i--)
        {
            var msg = msgs[i];
            int estimate = TruncationService.EstimateTokens(msg.Text ?? "");
            
            if (msg.Role == ChatRole.Tool && totalTokens > pruneProtectTokens)
            {
                // Prune old tool outputs
                var trimmedText = (msg.Text?.Length > 100 ? msg.Text.Substring(0, 100) : msg.Text) + "\n... (pruned for context)";
                pruned.Insert(0, new ChatMessage(ChatRole.Tool, trimmedText) { AuthorName = msg.AuthorName });
            }
            else
            {
                pruned.Insert(0, msg);
                totalTokens += estimate;
            }
        }
        
        return pruned;
    }
}
