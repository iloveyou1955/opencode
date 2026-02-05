using OpenCode.Core.Contracts;
using OpenCode.Core.Lsp;

namespace OpenCode.AgentFramework.Abstractions;

public class DefaultToolContext : IToolContext
{
    private readonly IPermissionService _permissionService;
    
    public string SessionId { get; }
    public string MessageId { get; }
    public ILspManager Lsp { get; }

    public DefaultToolContext(string sessionId, string messageId, IPermissionService permissionService, ILspManager lsp)
    {
        SessionId = sessionId;
        MessageId = messageId;
        _permissionService = permissionService;
        Lsp = lsp;
    }

    public async Task<bool> RequestPermissionAsync(string tool, string? pattern = null)
    {
        var action = await _permissionService.CheckPermissionAsync(SessionId, tool, pattern);
        
        return action switch
        {
            PermissionAction.Allow => true,
            PermissionAction.Deny => false,
            PermissionAction.Ask => await AskUserAsync(tool, pattern),
            _ => false
        };
    }

    private async Task<bool> AskUserAsync(string tool, string? pattern)
    {
        var info = new PermissionInfo
        {
            Id = "perm_" + Guid.NewGuid().ToString("N").Substring(0, 8),
            Type = tool,
            Pattern = pattern,
            SessionId = SessionId,
            MessageId = MessageId,
            Message = $"Allow tool execution: {tool} on {pattern}?",
            Metadata = new Dictionary<string, object>
            {
                ["tool"] = tool,
                ["pattern"] = pattern ?? ""
            }
        };

        var response = await _permissionService.AskAsync(info);
        return response != PermissionResponse.Reject;
    }
}
