using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using OpenCode.Core.Contracts;
using Microsoft.Extensions.Logging;
using OpenCode.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.Core.Services;

public interface IPluginHooks
{
    Task OnConfigAsync(ConfigService config);
    Task OnEventAsync(BusEvent @event);
    Task OnToolCallAsync(string toolName, JsonObject args);
    Task OnSessionCompactingAsync(string sessionId, List<ChatMessage> history);
    Task<string> OnTextCompleteAsync(string prompt, string result);
    Task<PermissionResponse?> OnPermissionAskAsync(PermissionInfo info);
    Task OnChatOptionsAsync(ChatOptions options, string? sessionId = null);
    AuthHook? Auth { get; }
}

public class AuthHook
{
    public required string Provider { get; init; }
    public required List<AuthMethod> Methods { get; init; }
}

public abstract class AuthMethod
{
    public required string Label { get; init; }
    public string Type { get; protected set; } = string.Empty;
}

public class OAuthAuthMethod : AuthMethod
{
    public OAuthAuthMethod() { Type = "oauth"; }
    public required Func<Task<OAuthAuthorizeResult>> AuthorizeAsync { get; init; }
}

public class ApiAuthMethod : AuthMethod
{
    public ApiAuthMethod() { Type = "api"; }
}

public class OAuthAuthorizeResult
{
    public required string Url { get; init; }
    public required string Instructions { get; init; }
    public required string Method { get; init; } // "auto" or "code"
    public required Func<Task<OAuthResult>> CallbackAsync { get; init; }
}

public class OAuthResult
{
    public required string Status { get; init; } // "success" or "failed"
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public long? ExpiresAt { get; init; }
    public string? AccountId { get; init; }
}

public class PluginInput
{
    public required IProjectContext Project { get; init; }
    public required string Directory { get; init; }
    public required BusService Bus { get; init; }
    public required IServiceProvider ServiceProvider { get; init; }
}

public interface IPlugin
{
    string Name { get; }
    string Version { get; }
    Task<IPluginHooks> InitializeAsync(PluginInput input);
}

[ServiceRegistration(ServiceLifetime.Singleton)]
public class PluginService
{
    private readonly List<IPluginHooks> _hooks = new();
    private readonly PluginInput _input;
    private readonly ILogger<PluginService> _logger;

    public PluginService(IProjectContext project, BusService bus, IServiceProvider serviceProvider, ILogger<PluginService> logger)
    {
        _input = new PluginInput
        {
            Project = project,
            Directory = project.Directory,
            Bus = bus,
            ServiceProvider = serviceProvider
        };
        _logger = logger;

        bus.SubscribeAll(async (e) => {
            foreach (var hook in _hooks)
            {
                try { await hook.OnEventAsync(e); } catch { }
            }
        });
    }

    public async Task LoadPluginAsync(IPlugin plugin)
    {
        _logger.LogInformation("Loading plugin {Name}@{Version}", plugin.Name, plugin.Version);
        var hooks = await plugin.InitializeAsync(_input);
        _hooks.Add(hooks);
    }

    public async Task LoadPluginsFromDirectoryAsync(string directory)
    {
        if (!Directory.Exists(directory)) return;

        foreach (var file in Directory.GetFiles(directory, "*.dll"))
        {
            try
            {
                var assembly = System.Reflection.Assembly.LoadFrom(file);
                var pluginTypes = assembly.GetTypes().Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
                
                foreach (var type in pluginTypes)
                {
                    if (Activator.CreateInstance(type) is IPlugin plugin)
                    {
                        await LoadPluginAsync(plugin);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load plugin from {File}", file);
            }
        }
    }

    public async Task TriggerToolCallAsync(string toolName, JsonObject args)
    {
        foreach (var hook in _hooks)
        {
            try { await hook.OnToolCallAsync(toolName, args); } catch { }
        }
    }

    public async Task TriggerSessionCompactingAsync(string sessionId, List<ChatMessage> history)
    {
        foreach (var hook in _hooks)
        {
            try { await hook.OnSessionCompactingAsync(sessionId, history); } catch { }
        }
    }

    public async Task<string> TriggerTextCompleteAsync(string prompt, string result)
    {
        var current = result;
        foreach (var hook in _hooks)
        {
            try { current = await hook.OnTextCompleteAsync(prompt, current); } catch { }
        }
        return current;
    }

    public async Task<PermissionResponse?> TriggerPermissionAskAsync(PermissionInfo info)
    {
        foreach (var hook in _hooks)
        {
            try 
            { 
                var resp = await hook.OnPermissionAskAsync(info);
                if (resp != null) return resp;
            } 
            catch { }
        }
        return null;
    }

    public async Task TriggerChatOptionsAsync(ChatOptions options, string? sessionId = null)
    {
        foreach (var hook in _hooks)
        {
            try { await hook.OnChatOptionsAsync(options, sessionId); } catch { }
        }
    }

    public List<AuthHook> GetAuthHooks()
    {
        return _hooks.Select(h => h.Auth).Where(a => a != null).Cast<AuthHook>().ToList();
    }
}
