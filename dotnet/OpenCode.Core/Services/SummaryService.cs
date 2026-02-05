using System.Text;
using Microsoft.Extensions.AI;
using OpenCode.Core.Contracts;

using OpenCode.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.Core.Services;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class SummaryService
{
    private readonly IChatClient _chatClient;
    private readonly PromptService _promptService;

    public SummaryService(IChatClient chatClient, PromptService promptService)
    {
        _chatClient = chatClient;
        _promptService = promptService;
    }

    public async Task<string> GenerateSummaryAsync(IEnumerable<ChatMessage> history, CancellationToken ct = default)
    {
        var summaryPrompt = await _promptService.GetPromptAsync("summary");
        if (string.IsNullOrEmpty(summaryPrompt))
        {
            summaryPrompt = "Summarize what was done in this conversation.";
        }

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, summaryPrompt)
        };

        // 仅包含用户和助手的消息，工具输出太长且不需要包含在摘要生成中
        foreach (var msg in history)
        {
            if (msg.Role == ChatRole.User || msg.Role == ChatRole.Assistant)
            {
                messages.Add(msg);
            }
        }

        if (messages.Count <= 1) return "No significant activity to summarize.";

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
        return response.ToString();
    }
}
