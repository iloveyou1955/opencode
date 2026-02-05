using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenCode.Core.Contracts;

using OpenCode.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.Core.Services;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class ShellService
{
    private readonly IProjectContext _projectContext;
    private readonly ILogger<ShellService> _logger;
    private readonly string _preferredShell;

    public ShellService(IProjectContext projectContext, ILogger<ShellService> logger)
    {
        _projectContext = projectContext;
        _logger = logger;
        _preferredShell = DiscoverPreferredShell();
    }

    public string PreferredShell => _preferredShell;

    public async Task KillTreeAsync(Process process)
    {
        if (process.HasExited) return;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                var psi = new ProcessStartInfo("taskkill", $"/pid {process.Id} /f /t")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(psi)?.WaitForExit();
            }
            else
            {
                process.Kill(true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to kill process tree for PID {Pid}", process.Id);
        }
        await Task.CompletedTask;
    }

    public async Task<Process> SpawnAsync(string command, string[]? args = null, string? cwd = null, IDictionary<string, string>? env = null)
    {
        var shell = DiscoverPreferredShell();
        var finalArgs = new List<string>();

        if (shell.Contains("bash"))
        {
            finalArgs.Add("-c");
            finalArgs.Add(command + (args != null ? " " + string.Join(" ", args) : ""));
        }
        else if (shell.Contains("cmd.exe"))
        {
            finalArgs.Add("/c");
            finalArgs.Add(command + (args != null ? " " + string.Join(" ", args) : ""));
        }
        else
        {
            if (args != null) finalArgs.AddRange(args);
        }

        var psi = new ProcessStartInfo(shell)
        {
            WorkingDirectory = cwd ?? _projectContext.Directory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in finalArgs) psi.ArgumentList.Add(arg);
        if (env != null)
        {
            foreach (var (k, v) in env) psi.Environment[k] = v;
        }

        var process = Process.Start(psi) ?? throw new Exception($"Failed to start shell: {shell}");
        return await Task.FromResult(process);
    }

    private string DiscoverPreferredShell()
    {
        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrEmpty(shell)) return shell;

        if (OperatingSystem.IsWindows())
        {
            var gitPath = FindInPath("git.exe");
            if (!string.IsNullOrEmpty(gitPath))
            {
                var bashPath = Path.GetFullPath(Path.Combine(gitPath, "..", "..", "bin", "bash.exe"));
                if (File.Exists(bashPath)) return bashPath;
            }

            return Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
        }

        var bash = FindInPath("bash");
        if (!string.IsNullOrEmpty(bash)) return bash;

        return "/bin/sh";
    }

    private string? FindInPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;

        var paths = path.Split(Path.PathSeparator);
        foreach (var p in paths)
        {
            var fullPath = Path.Combine(p, fileName);
            if (File.Exists(fullPath)) return fullPath;
        }
        return null;
    }
}
