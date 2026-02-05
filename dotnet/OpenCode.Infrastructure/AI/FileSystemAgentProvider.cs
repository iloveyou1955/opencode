using OpenCode.Core.Contracts;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenCode.Infrastructure.AI;

public class FileSystemAgentProvider : IAgentConfigurationProvider
{
    private readonly string _agentDir;
    private readonly IDeserializer _deserializer;
    private readonly ISerializer _serializer;

    public FileSystemAgentProvider(string agentDir)
    {
        _agentDir = agentDir;
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
    }

    public async Task<AgentMetadata?> GetAgentAsync(string name)
    {
        var filePath = Path.Combine(_agentDir, $"{name}.md");
        if (!File.Exists(filePath))
        {
            // Try fallback to lowercase
            filePath = Path.Combine(_agentDir, $"{name.ToLower()}.md");
            if (!File.Exists(filePath)) return null;
        }

        var content = await File.ReadAllTextAsync(filePath);
        return ParseAgent(name, content);
    }

    public async Task<IEnumerable<AgentMetadata>> GetAllAgentsAsync()
    {
        if (!Directory.Exists(_agentDir)) return Enumerable.Empty<AgentMetadata>();

        var agents = new List<AgentMetadata>();
        foreach (var file in Directory.GetFiles(_agentDir, "*.md"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var content = await File.ReadAllTextAsync(file);
            var agent = ParseAgent(name, content);
            if (agent != null) agents.Add(agent);
        }
        return agents;
    }

    public async Task SaveAgentAsync(AgentMetadata agent)
    {
        if (!Directory.Exists(_agentDir)) Directory.CreateDirectory(_agentDir);

        var filePath = Path.Combine(_agentDir, $"{agent.Name}.md");
        
        // Prepare metadata for YAML (exclude name and prompt as they are handled separately)
        var metadata = agent with { Name = "", Prompt = "" };
        var yaml = _serializer.Serialize(metadata);
        
        var content = $"---\n{yaml}---\n\n{agent.Prompt}";
        await File.WriteAllTextAsync(filePath, content);
    }

    private AgentMetadata? ParseAgent(string name, string content)
    {
        // Simple frontmatter parser
        const string divider = "---";
        if (!content.StartsWith(divider)) return null;

        var parts = content.Split(divider, 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;

        var yaml = parts[0];
        var prompt = parts[1].Trim();

        try
        {
            var metadata = _deserializer.Deserialize<AgentMetadata>(yaml);
            return metadata with { Name = name, Prompt = prompt };
        }
        catch
        {
            return new AgentMetadata { Name = name, Prompt = prompt };
        }
    }
}
