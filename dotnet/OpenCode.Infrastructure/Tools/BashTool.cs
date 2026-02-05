using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using OpenCode.Core.Contracts;
using OpenCode.Core.Services;

namespace OpenCode.Infrastructure.Tools;

public class BashTool : ITool
{
    private readonly IProjectContext _projectContext;
    private readonly ShellService _shellService;

    public BashTool(IProjectContext projectContext, ShellService shellService)
    {
        _projectContext = projectContext;
        _shellService = shellService;
    }

    public string Name => "bash";
    public string Description => "Execute a shell command. Use this tool to run system commands, scripts, or other CLI tools.";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {
        "command": { "type": "string", "description": "The command to execute" },
        "workdir": { "type": "string", "description": "The working directory to run the command in. Defaults to current directory." },
        "timeout": { "type": "number", "description": "Optional timeout in milliseconds" },
        "description": { "type": "string", "description": "Clear, concise description of what this command does in 5-10 words." }
      },
      "required": ["command", "description"]
    }
    """;

    private const int DEFAULT_TIMEOUT = 120 * 1000; // 2 minutes

    public async ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken cancellationToken = default)
    {
        string command = args["command"]?.ToString() ?? "";
        string? workdir = args["workdir"]?.ToString();
        double timeoutMs = args["timeout"]?.GetValue<double>() ?? DEFAULT_TIMEOUT;
        string description = args["description"]?.ToString() ?? ""; // Used for logging/metadata in TS, ignored here for execution

        if (string.IsNullOrWhiteSpace(command)) return "Error: Command cannot be empty.";
        if (timeoutMs < 0) return "Error: Timeout must be positive.";

        // 权限校验
        if (!await context.RequestPermissionAsync("bash", command))
        {
            return $"Error: Permission denied for bash command: {command}";
        }

        // 检查外部目录访问
        await CheckExternalDirectoryAccessAsync(command, workdir, context);

        var (shell, shellArgs) = GetShellInfo(command);
        
        var startInfo = new ProcessStartInfo
        {
            FileName = shell,
            Arguments = shellArgs,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workdir ?? Directory.GetCurrentDirectory()
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
             startInfo.StandardOutputEncoding = Encoding.UTF8;
             startInfo.StandardErrorEncoding = Encoding.UTF8;
        }

        try
        {
            using var process = new Process { StartInfo = startInfo };
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (sender, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                if (timeoutCts.IsCancellationRequested)
                {
                    process.Kill();
                    return $"Error: Command timed out (exceeded {timeoutMs}ms).\nOutput so far:\n{outputBuilder}";
                }
                else
                {
                    process.Kill();
                    throw; 
                }
            }

            string output = outputBuilder.ToString();
            string error = errorBuilder.ToString();
            int exitCode = process.ExitCode;

            var resultBuilder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(output))
            {
                resultBuilder.AppendLine(output);
            }
            
            if (!string.IsNullOrWhiteSpace(error))
            {
                // In many CLI tools, stderr is used for progress/info, not just errors.
                // We append it but maybe label it if mixed? 
                // TS implementation just appends both.
                resultBuilder.AppendLine(error);
            }

            if (exitCode != 0)
            {
                resultBuilder.AppendLine($"\nExit Code: {exitCode}");
            }
            else if (resultBuilder.Length == 0)
            {
                resultBuilder.Append("Command executed successfully (no output).");
            }

            return resultBuilder.ToString().Trim();
        }
        catch (Exception ex)
        {
            return $"Error executing command: {ex.Message}";
        }
    }

    private async Task CheckExternalDirectoryAccessAsync(string command, string? workdir, IToolContext context)
    {
        var tokens = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cwd = workdir ?? _projectContext.Directory;

        foreach (var token in tokens)
        {
            // 简单的路径启发式检测
            if (token.Contains("/") || token.Contains("\\") || token.StartsWith("."))
            {
                try
                {
                    var fullPath = Path.GetFullPath(Path.Combine(cwd, token));
                    if (!_projectContext.ContainsPath(fullPath))
                    {
                        // 请求外部目录权限
                        await context.RequestPermissionAsync("external_directory", fullPath);
                    }
                }
                catch { }
            }
        }
    }

    private (string shell, string args) GetShellInfo(string command)
    {
        var shell = _shellService.PreferredShell;
        var isBash = shell.EndsWith("bash") || shell.EndsWith("bash.exe") || shell.EndsWith("sh");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !isBash)
        {
            return (shell, $"/c \"{command}\"");
        }
        else
        {
            // Escape double quotes for bash -c
            var escapedCommand = command.Replace("\"", "\\\"");
            return (shell, $"-c \"{escapedCommand}\"");
        }
    }
}
