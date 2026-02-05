using Microsoft.Extensions.AI;
using OpenCode.Core.Services;

namespace OpenCode.Infrastructure.AI;

public class NormalizationChatClient : DelegatingChatClient
{
    private readonly ModelMappingService _modelMapping;
    private readonly string _modelId;

    public NormalizationChatClient(IChatClient innerClient, ModelMappingService modelMapping, string modelId) 
        : base(innerClient)
    {
        _modelMapping = modelMapping;
        _modelId = modelId;
    }

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var normalized = _modelMapping.NormalizeMessages(chatMessages.ToList(), _modelId);
            return await base.GetResponseAsync(normalized, options, cancellationToken);
        }
        catch (Exception ex) when (ex.Message.Contains("401") || ex.Message.Contains("403") || ex.Message.Contains("api_key") || ex.Message.Contains("unauthorized"))
        {
            throw new InvalidOperationException("认证失败: API Key 无效或未配置。请使用 'auth' 指令配置您的 AI 服务商。", ex);
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var normalized = _modelMapping.NormalizeMessages(chatMessages.ToList(), _modelId);
        IAsyncEnumerator<ChatResponseUpdate>? enumerator = null;
        try
        {
            enumerator = base.GetStreamingResponseAsync(normalized, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception ex) when (ex.Message.Contains("401") || ex.Message.Contains("403") || ex.Message.Contains("api_key") || ex.Message.Contains("unauthorized"))
        {
            throw new InvalidOperationException("认证失败: API Key 无效或未配置。请使用 'auth' 指令配置您的 AI 服务商。", ex);
        }

        if (enumerator != null)
        {
            while (true)
            {
                ChatResponseUpdate? update = null;
                try
                {
                    if (!await enumerator.MoveNextAsync()) break;
                    update = enumerator.Current;
                }
                catch (Exception ex) when (ex.Message.Contains("401") || ex.Message.Contains("403") || ex.Message.Contains("api_key") || ex.Message.Contains("unauthorized"))
                {
                    throw new InvalidOperationException("认证失败: API Key 无效或未配置。请使用 'auth' 指令配置您的 AI 服务商。", ex);
                }
                yield return update!;
            }
            await enumerator.DisposeAsync();
        }
    }
}
