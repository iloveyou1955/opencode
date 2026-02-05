using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using OpenCode.Core.Attributes;

namespace OpenCode.Core.Services;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class PromptService
{
    private readonly string _promptDir;

    public PromptService()
    {
        // Try to find the prompt directory relative to the assembly
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        _promptDir = Path.Combine(assemblyDir ?? "", "Resources", "Prompts");
        
        // Fallback for development
        if (!Directory.Exists(_promptDir))
        {
            // If we are in dotnet/bin/Debug/net10.0, we want to look at dotnet/OpenCode.Core/Resources/Prompts
            // This is a bit hacky but works for local dev
            var projectDir = Path.GetFullPath(Path.Combine(assemblyDir ?? "", "..", "..", "..", "OpenCode.Core", "Resources", "Prompts"));
            if (Directory.Exists(projectDir))
            {
                _promptDir = projectDir;
            }
        }
    }

    public async Task<string> GetPromptAsync(string name)
    {
        var path = Path.Combine(_promptDir, $"{name}.txt");
        if (File.Exists(path))
        {
            return await File.ReadAllTextAsync(path);
        }
        return string.Empty;
    }
}
