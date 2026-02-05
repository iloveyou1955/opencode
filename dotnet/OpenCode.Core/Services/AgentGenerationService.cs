using Microsoft.Extensions.AI;
using OpenCode.Core.Contracts;
using System.Text.Json;
using System.Text.Json.Nodes;

using OpenCode.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.Core.Services;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class AgentGenerationService
{
    private readonly IChatClient _chatClient;
    private readonly IAgentConfigurationProvider _agentProvider;

    private const string GeneratePrompt = """
        You are an expert at creating AI agent configurations.
        Create an agent configuration based on the user's request.
        Return ONLY a JSON object with the following fields:
        - identifier: A short, unique string identifier (kebab-case)
        - whenToUse: A description of when this agent should be used
        - systemPrompt: The full system prompt for the agent
        """;

    public AgentGenerationService(IChatClient chatClient, IAgentConfigurationProvider agentProvider)
    {
        _chatClient = chatClient;
        _agentProvider = agentProvider;
    }

    public async Task<JsonObject?> GenerateAgentAsync(string description, CancellationToken ct = default)
    {
        var existingAgents = await _agentProvider.GetAllAgentsAsync();
        var existingNames = string.Join(", ", existingAgents.Select(a => a.Name));

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, GeneratePrompt),
            new ChatMessage(ChatRole.User, $"Create an agent configuration based on this request: \"{description}\".\n\nIMPORTANT: The following identifiers already exist and must NOT be used: {existingNames}\nReturn ONLY the JSON object, no other text, do not wrap in backticks.")
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
        var text = response.ToString().Trim();
        
        // 尝试清理可能的 Markdown 代码块包裹
        if (text.StartsWith("```json")) text = text.Substring(7);
        if (text.StartsWith("```")) text = text.Substring(3);
        if (text.EndsWith("```")) text = text.Substring(0, text.Length - 3);
        text = text.Trim();

        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch
        {
            return null;
        }
    }
}
