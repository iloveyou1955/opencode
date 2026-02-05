using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenCode.Core.Contracts;

using OpenCode.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.Core.Services;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class VcsService
{
    private readonly IProjectContext _projectContext;
    private readonly BusService _bus;
    private readonly ILogger<VcsService> _logger;
    private string? _currentBranch;

    public VcsService(IProjectContext projectContext, BusService bus, ILogger<VcsService> logger)
    {
        _projectContext = projectContext;
        _bus = bus;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        _currentBranch = await GetCurrentBranchAsync();
        _logger.LogDebug("VCS initialized. Current branch: {Branch}", _currentBranch);

        // 订阅文件更新事件来检测分支变化
        _bus.SubscribeAll(async (e) => {
            if (e.Type == "file.watcher.updated")
            {
                // 如果 .git/HEAD 发生变化，或者其他关键 git 文件变化，重新检查分支
                var nextBranch = await GetCurrentBranchAsync();
                if (nextBranch != _currentBranch)
                {
                    _logger.LogInformation("Branch changed: {From} -> {To}", _currentBranch, nextBranch);
                    _currentBranch = nextBranch;
                    _bus.Publish("vcs.branch.updated", new { branch = _currentBranch });
                }
            }
        });
    }

    public string? Branch => _currentBranch;

    public async Task<RepoInfo?> GetRepoInfoAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                Arguments = "remote get-url origin",
                WorkingDirectory = _projectContext.Directory,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return null;
            var url = (await process.StandardOutput.ReadToEndAsync()).Trim();
            await process.WaitForExitAsync();

            if (string.IsNullOrEmpty(url)) return null;

            // Simple regex to parse GitHub remote URLs
            var match = System.Text.RegularExpressions.Regex.Match(url, @"^(?:(?:https?|ssh):\/\/)?(?:git@)?github\.com[:/]([^/]+)\/([^/]+?)(?:\.git)?$");
            if (!match.Success) return null;

            return new RepoInfo(match.Groups[1].Value, match.Groups[2].Value);
        }
        catch
        {
            return null;
        }
    }

    public record RepoInfo(string Owner, string Name);

    public async Task<bool> IsDirtyAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                Arguments = "status --porcelain",
                WorkingDirectory = _projectContext.Directory,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return false;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return !string.IsNullOrWhiteSpace(output);
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> GetCurrentBranchAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                Arguments = "rev-parse --abbrev-ref HEAD",
                WorkingDirectory = _projectContext.Directory,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return null;
            var branch = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return branch.Trim();
        }
        catch
        {
            return null;
        }
    }
}
