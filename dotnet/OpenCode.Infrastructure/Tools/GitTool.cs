using System.Text;
using System.Text.Json.Nodes;
using OpenCode.Core.Contracts;
using OpenCode.Core.Utilities;

namespace OpenCode.Infrastructure.Tools;

public class GitTool : ITool
{
    public string Name => "git";
    public string Description => "Execute Git commands. Commands: status, add, commit, log, diff, checkout, branch";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {
        "command": { "type": "string", "enum": ["status", "add", "commit", "log", "diff", "checkout", "branch", "init"] },
        "args": { "type": "string", "description": "Arguments for the command (e.g., file paths, branch name)" },
        "message": { "type": "string", "description": "Commit message (only for commit)" },
        "limit": { "type": "integer", "description": "Limit for log (default 10)" }
      },
      "required": ["command"]
    }
    """;

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken cancellationToken = default)
    {
        string command = args["command"]?.ToString() ?? "";
        string arguments = args["args"]?.ToString() ?? "";
        string message = args["message"]?.ToString() ?? "";
        string? workingDirectory = args["working_directory"]?.ToString();
        int limit = args["limit"]?.GetValue<int>() ?? 10;

        // 权限校验
        if (!await context.RequestPermissionAsync("git", command))
        {
            return $"Error: Permission denied for git {command}";
        }

        string gitArgs;

        switch (command)
        {
            case "init":
                gitArgs = "init";
                break;
            case "status":
                gitArgs = "status";
                break;
            case "add":
                if (string.IsNullOrWhiteSpace(arguments)) return "Error: 'add' requires arguments (e.g., '.' or file paths).";
                gitArgs = $"add {arguments}";
                break;
            case "commit":
                if (string.IsNullOrWhiteSpace(message)) return "Error: 'commit' requires a message.";
                // Escape quotes in message
                var escapedMessage = message.Replace("\"", "\\\"");
                gitArgs = $"commit -m \"{escapedMessage}\"";
                break;
            case "log":
                gitArgs = $"log -n {limit} --oneline";
                break;
            case "diff":
                gitArgs = string.IsNullOrWhiteSpace(arguments) ? "diff" : $"diff {arguments}";
                break;
            case "checkout":
                if (string.IsNullOrWhiteSpace(arguments)) return "Error: 'checkout' requires branch or file.";
                gitArgs = $"checkout {arguments}";
                break;
            case "branch":
                gitArgs = string.IsNullOrWhiteSpace(arguments) ? "branch" : $"branch {arguments}";
                break;
            default:
                return $"Error: Unknown git command '{command}'";
        }

        // 使用 ShellTool 的逻辑或者直接调用 Process
        // 为了简单，我们复用类似 ShellTool 的 Process 逻辑
        // 注意：这里没有 ShellTool 的实例，所以复制 Process 逻辑
        
        var result = await RunGitProcessAsync(gitArgs, workingDirectory ?? Directory.GetCurrentDirectory(), cancellationToken);
        
        // 如果是 diff 或 log，可能需要截断
        if (command == "diff" || command == "log")
        {
            var trunc = await Truncator.TruncateAsync(result);
            return trunc.Content;
        }

        return result;
    }

    private async Task<string> RunGitProcessAsync(string args, string workingDirectory, CancellationToken ct)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        try
        {
            using var process = new System.Diagnostics.Process { StartInfo = startInfo };
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null) outputBuilder.AppendLine(e.Data);
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null) errorBuilder.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                return $"Git Error (Exit Code {process.ExitCode}):\n{errorBuilder}\n{outputBuilder}";
            }

            return outputBuilder.ToString().Trim();
        }
        catch (Exception ex)
        {
            return $"Error executing git: {ex.Message}. Make sure git is installed and in PATH.";
        }
    }
}
