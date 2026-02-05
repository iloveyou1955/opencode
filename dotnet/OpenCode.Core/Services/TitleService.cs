using System.Text;
using Microsoft.Extensions.AI;
using OpenCode.Core.Contracts;

using OpenCode.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.Core.Services;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class TitleService
{
    private readonly IChatClient _chatClient;
    private readonly PromptService _promptService;

    public TitleService(IChatClient chatClient, PromptService promptService)
    {
        _chatClient = chatClient;
        _promptService = promptService;
    }

    public async Task<string> GenerateTitleAsync(string firstUserMessage, CancellationToken ct = default)
    {
        var titlePrompt = await _promptService.GetPromptAsync("title");
        if (string.IsNullOrEmpty(titlePrompt))
        {
            titlePrompt = "Generate a brief title for this conversation.";
        }

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, titlePrompt),
            new ChatMessage(ChatRole.User, firstUserMessage)
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
        
        // 使用 ToString() 获取内容，这是目前 codebase 中通用的方式
        var title = response.ToString();
        
        if (string.IsNullOrWhiteSpace(title))
        {
            return "New Session";
        }

        return title.Trim().Trim('"').Trim();
    }
}
