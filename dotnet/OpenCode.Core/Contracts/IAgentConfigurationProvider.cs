namespace OpenCode.Core.Contracts;

public record AgentMetadata
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Prompt { get; init; } = string.Empty;
    public string Color { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string Mode { get; init; } = "primary"; // subagent, primary, all
    public bool Hidden { get; init; } = false;
    public bool Native { get; init; } = false;
    public double? Temperature { get; init; }
    public Dictionary<string, object>? Permissions { get; init; }
}

public interface IAgentConfigurationProvider
{
    Task<AgentMetadata?> GetAgentAsync(string name);
    Task<IEnumerable<AgentMetadata>> GetAllAgentsAsync();
    Task SaveAgentAsync(AgentMetadata agent);
}
