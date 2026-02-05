using OpenCode.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Agents.AI;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.DependencyInjection;
using OpenCode.Infrastructure.Plugins;

namespace OpenCode.Infrastructure.AI;

public static class AIClientFactory
{
    public static IChatClient CreateClient(string providerAndModel, ConfigInfo config, IServiceProvider serviceProvider)
    {
        var parts = providerAndModel.Split('/', 2);
        var providerId = parts[0];
        var modelId = parts.Length > 1 ? parts[1] : null;

        var pluginService = serviceProvider.GetRequiredService<PluginService>();
        var authService = serviceProvider.GetRequiredService<AuthService>();
        var modelMapping = serviceProvider.GetRequiredService<ModelMappingService>();

        IChatClient? client = null;
        switch (providerId.ToLower())
        {
            case "openai":
                client = CreateOpenAIClient(providerId, config, authService, modelId);
                break;
            case "github-copilot":
                client = CreateCopilotClient(providerId, config, authService, modelId);
                break;
            case "none":
                throw new InvalidOperationException("No AI model is configured. Please use 'config set model <provider>/<model>' to configure one.");
            default:
                throw new NotSupportedException($"Provider '{providerId}' is not yet supported in .NET version.");
        }

        if (client == null)
        {
            throw new InvalidOperationException($"Failed to create client for provider '{providerId}'.");
        }

        client = new NormalizationChatClient(client, modelMapping, modelId ?? "gpt-4o");

        if (pluginService != null)
        {
            client = new PluginChatClient(client, pluginService);
        }

        return client;
    }

    private static IChatClient CreateOpenAIClient(string providerId, ConfigInfo config, AuthService authService, string? modelId)
    {
        ProviderConfig? providerConfig = null;
        config.Providers?.TryGetValue(providerId, out providerConfig);
        
        // 依次从配置、环境变量、AuthService 获取 API Key
        var apiKey = providerConfig?.ApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        
        if (string.IsNullOrEmpty(apiKey))
        {
            var authInfo = authService.Get(providerId);
            if (authInfo is ApiAuthInfo apiAuth)
            {
                apiKey = apiAuth.Key;
            }
        }
        
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException($"OpenAI API Key is not configured for provider '{providerId}'. Please set it in config.json, environment variables, or run 'auth login {providerId}'.");
        }
        
        var handler = new CodexHttpClientHandler(authService, new HttpClientHandler());
        var httpClient = new HttpClient(handler);
        
        var options = new OpenAIClientOptions { Transport = new HttpClientPipelineTransport(httpClient) };
        if (!string.IsNullOrEmpty(providerConfig?.BaseUrl))
        {
            options.Endpoint = new Uri(providerConfig.BaseUrl);
        }

        var chatClient = new ChatClient(modelId ?? "gpt-4o", new ApiKeyCredential(apiKey), options);
        return chatClient.AsIChatClient();
    }

    private static IChatClient CreateCopilotClient(string providerId, ConfigInfo config, AuthService authService, string? modelId)
    {
        var handler = new CopilotHttpClientHandler(authService, new HttpClientHandler());
        var httpClient = new HttpClient(handler);
        
        // Copilot typically uses OpenAI-compatible API
        var options = new OpenAIClientOptions 
        { 
            Transport = new HttpClientPipelineTransport(httpClient),
            Endpoint = new Uri("https://copilot-api.github.com/v1")
        };

        var chatClient = new ChatClient(modelId ?? "gpt-4o", new ApiKeyCredential("ignored"), options);
        return chatClient.AsIChatClient();
    }

    /// <summary>
    /// 创建带有缓存和重试机制的 ChatClient
    /// </summary>
    public static IChatClient CreateConfiguredClient(IChatClient baseClient, IDistributedCache cache)
    {
        // Microsoft.Extensions.AI 提供了一系列标准中间件
        return baseClient.AsBuilder()
            .UseDistributedCache(cache) // 自动处理消息缓存
            .Build();
    }

    /// <summary>
    /// 创建带有内置消息管理和状态的 Agent
    /// </summary>
    public static AIAgent CreateAgent(IChatClient client, string name, string instructions)
    {
        // AsAIAgent 扩展会自动处理与 MAF 的集成
        return client.AsAIAgent(name: name, instructions: instructions);
    }
}
