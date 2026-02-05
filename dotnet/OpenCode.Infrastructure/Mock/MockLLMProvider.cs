using Microsoft.Extensions.AI;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace OpenCode.Infrastructure.Mock;

/// <summary>
/// 模拟 ChatClient，基于 Microsoft.Extensions.AI 标准接口
/// </summary>
public class MockChatClient : IChatClient
{
    private int _turn = 0;
    private readonly Queue<string> _responseQueue = new();

    public ChatClientMetadata Metadata => new ChatClientMetadata("MockProvider", null, "mock-model");

    public void SetNextResponse(string response)
    {
        _responseQueue.Enqueue(response);
    }

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var responseText = GetResponse();
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var responseText = GetResponse();
        for (int i = 0; i < responseText.Length; i += 5)
        {
            int len = Math.Min(5, responseText.Length - i);
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = new List<AIContent> { new TextContent(responseText.Substring(i, len)) }
            };
        }
    }

    private string GetResponse()
    {
        if (_responseQueue.Count > 0) return _responseQueue.Dequeue();

        var response = _turn switch
        {
            0 => "好的，我先看看当前目录下有什么文件。\n```json\n{ \"tool\": \"file_system\", \"args\": { \"command\": \"list_dir\", \"path\": \".\" } }\n```",
            1 => "我看到了 `docs` 目录。让我看看技术设计文档。\n```json\n{ \"tool\": \"file_system\", \"args\": { \"command\": \"read_file\", \"path\": \"./docs/TECHNICAL_DESIGN.md\" } }\n```",
            _ => "```json\n{ \"answer\": \"我已经阅读了设计文档，架构设计非常清晰。\" }\n```"
        };
        _turn++;
        return response;
    }

    public void Dispose() { }
    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;
}