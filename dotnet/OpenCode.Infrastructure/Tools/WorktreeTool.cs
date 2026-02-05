using System.Text.Json.Nodes;
using OpenCode.Core.Contracts;
using OpenCode.Core.Services;

namespace OpenCode.Infrastructure.Tools;

public class WorktreeTool : ITool
{
    private readonly WorktreeService _worktreeService;

    public WorktreeTool(WorktreeService worktreeService)
    {
        _worktreeService = worktreeService;
    }

    public string Name => "worktree";
    public string Description => "Manage git worktrees for running tests or experiments in isolation.";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {
        "action": { "type": "string", "enum": ["create", "list", "remove", "reset"], "description": "The action to perform" },
        "name": { "type": "string", "description": "Name for the new worktree (for create)" },
        "startCommand": { "type": "string", "description": "Optional startup script (for create)" },
        "directory": { "type": "string", "description": "Directory of the worktree to remove" }
      },
      "required": ["action"]
    }
    """;

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken cancellationToken = default)
    {
        var action = args["action"]?.ToString();

        try
        {
            switch (action)
            {
                case "create":
                    var name = args["name"]?.ToString();
                    var startCommand = args["startCommand"]?.ToString();
                    var info = await _worktreeService.CreateAsync(name, startCommand);
                    return $"Worktree created successfully:\nName: {info.Name}\nBranch: {info.Branch}\nDirectory: {info.Directory}";

                case "list":
                    var worktrees = await _worktreeService.ListAsync();
                    if (worktrees.Count == 0) return "No active worktrees found.";
                    return "Active Worktrees:\n" + string.Join("\n", worktrees.Select(w => $"- {w.Name} ({w.Branch}) at {w.Directory}"));

                case "remove":
                    var directory = args["directory"]?.ToString();
                    if (string.IsNullOrEmpty(directory)) return "Error: Directory is required for remove action.";
                    await _worktreeService.RemoveAsync(directory);
                    return $"Worktree at {directory} removed successfully.";

                case "reset":
                    var target = args["directory"]?.ToString();
                    if (string.IsNullOrEmpty(target)) return "Error: Directory is required for reset action.";
                    await _worktreeService.ResetAsync(target);
                    return $"Worktree at {target} reset successfully.";

                default:
                    return $"Error: Unknown action '{action}'";
            }
        }
        catch (Exception ex)
        {
            return $"Error performing worktree action: {ex.Message}";
        }
    }
}
