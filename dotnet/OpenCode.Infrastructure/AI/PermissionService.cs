using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using OpenCode.Core.Contracts;
using OpenCode.Core.Services;
using OpenCode.Core.Utilities;

namespace OpenCode.Infrastructure.AI;

public class PermissionService : IPermissionService
{
    private readonly JsonObject _config;
    private readonly IProjectContext _projectContext;
    private readonly BusService _bus;
    private readonly ILogger<PermissionService> _logger;

    private readonly ConcurrentDictionary<string, PermissionRequest> _pending = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _approved = new(); // SessionId -> Set of patterns

    public PermissionService(JsonObject config, IProjectContext projectContext, BusService bus, ILogger<PermissionService> logger)
    {
        _config = config;
        _projectContext = projectContext;
        _bus = bus;
        _logger = logger;

        // Subscribe to replies
        _bus.Subscribe("permission.replied", (e) => {
            if (e.Properties is JsonObject props && 
                props.TryGetPropertyValue("permissionId", out var idNode) &&
                props.TryGetPropertyValue("response", out var respNode))
            {
                var id = idNode?.ToString();
                if (id != null && Enum.TryParse<PermissionResponse>(respNode?.ToString(), true, out var resp))
                {
                    Respond(id, resp);
                }
            }
            return Task.CompletedTask;
        });
    }

    public async Task<PermissionAction> CheckPermissionAsync(string sessionId, string tool, string? pattern = null)
    {
        // 1. Check approved cache for session
        if (_approved.TryGetValue(sessionId, out var patterns))
        {
            if (patterns.Contains(tool) || (pattern != null && patterns.Any(p => WildcardMatcher.Match(pattern, p))))
            {
                return PermissionAction.Allow;
            }
        }

        // 2. Check for external directory
        if (pattern != null && Path.IsPathRooted(pattern) && !_projectContext.ContainsPath(pattern))
        {
            var externalAction = await CheckRuleAsync("external_directory", pattern);
            if (externalAction != PermissionAction.Allow) return externalAction;
        }

        return await CheckRuleAsync(tool, pattern);
    }

    public async Task<PermissionResponse> AskAsync(PermissionInfo info)
    {
        var tcs = new TaskCompletionSource<PermissionResponse>();
        var request = new PermissionRequest { Info = info, Tcs = tcs };
        _pending[info.Id] = request;

        _logger.LogInformation("Asking permission {Id} for {Type} ({Pattern})", info.Id, info.Type, info.Pattern);
        await _bus.PublishAsync("permission.updated", info);

        return await tcs.Task;
    }

    public void Respond(string permissionId, PermissionResponse response)
    {
        if (_pending.TryRemove(permissionId, out var request))
        {
            if (response == PermissionResponse.Always)
            {
                var patterns = _approved.GetOrAdd(request.Info.SessionId, _ => new HashSet<string>());
                patterns.Add(request.Info.Pattern ?? request.Info.Type);
            }

            request.Tcs.SetResult(response);
        }
    }

    public List<PermissionInfo> GetPending()
    {
        return _pending.Values.Select(r => r.Info).OrderBy(i => i.Id).ToList();
    }

    private Task<PermissionAction> CheckRuleAsync(string tool, string? pattern)
    {
        var permissions = _config["permission"] as JsonObject;
        if (permissions == null) return Task.FromResult(PermissionAction.Ask);

        if (permissions.TryGetPropertyValue(tool, out var toolRule))
        {
            return Task.FromResult(ParseRule(toolRule, pattern));
        }

        if (permissions.TryGetPropertyValue("*", out var globalRule))
        {
            return Task.FromResult(ParseRule(globalRule, pattern));
        }

        return Task.FromResult(PermissionAction.Ask);
    }

    private PermissionAction ParseRule(JsonNode? rule, string? pattern)
    {
        if (rule is JsonValue value)
        {
            return Enum.TryParse<PermissionAction>(value.ToString(), true, out var action) 
                ? action 
                : PermissionAction.Ask;
        }

        if (rule is JsonObject obj && pattern != null)
        {
            foreach (var prop in obj)
            {
                if (WildcardMatcher.Match(pattern, prop.Key))
                {
                    return Enum.TryParse<PermissionAction>(prop.Value?.ToString(), true, out var action)
                        ? action
                        : PermissionAction.Ask;
                }
            }
        }

        return PermissionAction.Ask;
    }

    private class PermissionRequest
    {
        public PermissionInfo Info { get; set; } = null!;
        public TaskCompletionSource<PermissionResponse> Tcs { get; set; } = null!;
    }
}
