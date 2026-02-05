using OpenCode.Core.Contracts;
using OpenCode.Core.Services;
using OpenCode.Core.Utilities;
using OpenCode.Core.Lsp;
using System.Text;
using System.Text.Json.Nodes;

namespace OpenCode.Infrastructure.AI;

public class InstructionService : IInstructionService
{
    private readonly string _projectRoot;
    private readonly IAgentConfigurationProvider _agentProvider;
    private readonly McpService? _mcpService;
    private readonly ShellService? _shellService;
    private readonly PromptService _promptService;
    private readonly ContextService? _contextService;
    private readonly ILspManager? _lspManager;

    public InstructionService(
        string projectRoot, 
        IAgentConfigurationProvider agentProvider, 
        PromptService promptService,
        McpService? mcpService = null, 
        ShellService? shellService = null,
        ContextService? contextService = null,
        ILspManager? lspManager = null)
    {
        _projectRoot = projectRoot;
        _agentProvider = agentProvider;
        _promptService = promptService;
        _mcpService = mcpService;
        _shellService = shellService;
        _contextService = contextService;
        _lspManager = lspManager;
    }

    public async Task<string> GetAggregatedInstructionsAsync(string agentName)
    {
        return await GetAggregatedInstructionsAsync(agentName, "claude-3-5-sonnet");
    }

    public async Task<string> GetAggregatedInstructionsAsync(string agentName, string modelId)
    {
        var instructions = new List<string>();

        // 1. Base System Prompt (Model-specific)
        var basePrompt = await GetBasePromptForModelAsync(modelId);
        instructions.Add(basePrompt);

        // 2. Environment Context
        instructions.Add(await GetEnvironmentInstructionsAsync(modelId));

        // 2.1 Dynamic Context (Open files, etc.)
        if (_contextService != null)
        {
            if (_lspManager != null)
            {
                await _contextService.UpdateDiagnosticsAsync(_lspManager);
            }
            instructions.Add(await _contextService.GetContextInstructionsAsync());
        }

        // 3. Agent-specific prompt
        var agentMetadata = await _agentProvider.GetAgentAsync(agentName);
        if (agentMetadata != null && !string.IsNullOrWhiteSpace(agentMetadata.Prompt))
        {
            var prompt = ConfigLoader.ReplaceVariables(agentMetadata.Prompt, _projectRoot);
            instructions.Add($"# Agent Role: {agentName}\n{prompt}");
        }

        // 4. Discovery (AGENTS.md, CLAUDE.md, etc.)
        instructions.AddRange(await DiscoverProjectInstructionsAsync());

        // 5. Config Instructions
        instructions.AddRange(await GetConfigInstructionsAsync());

        // 6. MCP Tools
        if (_mcpService != null)
        {
            var mcpInstructions = await GetMcpInstructionsAsync();
            if (!string.IsNullOrEmpty(mcpInstructions))
            {
                instructions.Add(mcpInstructions);
            }
        }

        return string.Join("\n\n---\n\n", instructions.Where(s => !string.IsNullOrEmpty(s)));
    }

    private async Task<string> GetBasePromptForModelAsync(string modelId)
    {
        var id = modelId.ToLower();
        if (id.Contains("gpt-5")) return await _promptService.GetPromptAsync("codex_header");
        if (id.Contains("gpt-") || id.Contains("o1") || id.Contains("o3")) return await _promptService.GetPromptAsync("beast");
        if (id.Contains("gemini")) return await _promptService.GetPromptAsync("gemini");
        if (id.Contains("claude")) return await _promptService.GetPromptAsync("anthropic");
        return await _promptService.GetPromptAsync("qwen");
    }

    private async Task<List<string>> DiscoverProjectInstructionsAsync()
    {
        var result = new List<string>();
        var docFiles = new[] { "AGENTS.md", "CLAUDE.md", "CONTEXT.md", "PROJECT.md" };

        foreach (var fileName in docFiles)
        {
            var path = Path.Combine(_projectRoot, fileName);
            if (File.Exists(path))
            {
                var content = await File.ReadAllTextAsync(path);
                result.Add($"# Local Instructions: {fileName}\n{content}");
            }
        }

        // Global discovery
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var globalClaude = Path.Combine(home, ".claude", "CLAUDE.md");
        if (File.Exists(globalClaude))
        {
            var content = await File.ReadAllTextAsync(globalClaude);
            result.Add($"# Global Instructions: CLAUDE.md\n{content}");
        }

        return result;
    }

    private async Task<string> GetEnvironmentInstructionsAsync(string modelId)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Here is some useful information about the environment you are running in:");
        sb.AppendLine("<env>");
        sb.AppendLine($"  Model: {modelId}");
        sb.AppendLine($"  Working directory: {_projectRoot}");
        sb.AppendLine($"  Platform: {Environment.OSVersion.Platform}");
        sb.AppendLine($"  Today's date: {DateTime.Now:yyyy-MM-dd}");
        sb.AppendLine("</env>");
        return sb.ToString();
    }

    private async Task<string> GetMcpInstructionsAsync()
    {
        if (_mcpService == null) return string.Empty;

        var tools = await _mcpService.ListToolsAsync();
        if (!tools.Any()) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("# Model Context Protocol (MCP) Tools");
        sb.AppendLine("The following tools are available via MCP servers. You can call them using the standard tool call format.");
        
        foreach (var tool in tools)
        {
            sb.AppendLine($"- **{tool["name"]}**: {tool["description"]}");
        }

        return sb.ToString();
    }

    private async Task<List<string>> GetConfigInstructionsAsync()
    {
        var result = new List<string>();
        var configPath = Path.Combine(_projectRoot, "opencode.jsonc");
        if (!File.Exists(configPath))
        {
            configPath = Path.Combine(_projectRoot, "opencode.json");
        }

        if (File.Exists(configPath))
        {
            try
            {
                var content = await File.ReadAllTextAsync(configPath);
                var jsonNode = ConfigLoader.ParseJsonc(content);
                var instructionsArray = jsonNode?["instructions"]?.AsArray();
                
                if (instructionsArray != null)
                {
                    foreach (var item in instructionsArray)
                    {
                        var path = item?.ToString();
                        if (string.IsNullOrEmpty(path)) continue;

                        if (!Path.IsPathRooted(path))
                        {
                            path = Path.Combine(_projectRoot, path);
                        }

                        if (File.Exists(path))
                        {
                            var fileContent = await File.ReadAllTextAsync(path);
                            var processedContent = ConfigLoader.ReplaceVariables(fileContent, Path.GetDirectoryName(path)!);
                            result.Add($"# Instruction: {Path.GetFileName(path)}\n{processedContent}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading config instructions: {ex.Message}");
            }
        }

        return result;
    }
}
