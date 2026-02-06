using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;
using OpenCode.Core.Contracts;

namespace OpenCode.Core.Services;

public record WorktreeInfo(
    string Name,
    string Branch,
    string Directory,
    DateTime CreatedAt,
    DateTime? LastAccessedAt = null
);

public class WorktreeService
{
    private readonly IProjectContext _projectContext;
    private readonly ShellService _shellService;
    private readonly ILogger<WorktreeService> _logger;
    private readonly string _worktreeBaseDir;

    private static readonly string[] Adjectives = {
        "brave", "calm", "clever", "cosmic", "crisp", "curious", "eager", "gentle", "glowing", "happy"
    };

    private static readonly string[] Nouns = {
        "cabin", "cactus", "canyon", "circuit", "comet", "eagle", "engine", "falcon", "forest", "garden"
    };

    public WorktreeService(IProjectContext projectContext, ShellService shellService, ILogger<WorktreeService> logger)
    {
        _projectContext = projectContext;
        _shellService = shellService;
        _logger = logger;
        
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenCode");
        _worktreeBaseDir = Path.Combine(dataDir, "worktree", Slug(Path.GetFileName(_projectContext.Directory)));
        
        if (!Directory.Exists(_worktreeBaseDir)) Directory.CreateDirectory(_worktreeBaseDir);
    }

    public async Task<WorktreeInfo> CreateAsync(string? name = null, string? startCommand = null)
    {
        var random = new Random();
        var generatedName = name ?? $"{Adjectives[random.Next(Adjectives.Length)]}-{Nouns[random.Next(Nouns.Length)]}";
        var slugName = Slug(generatedName);
        var branch = $"opencode/{slugName}";
        var directory = Path.Combine(_worktreeBaseDir, slugName);

        if (Directory.Exists(directory))
        {
            throw new Exception($"Worktree directory already exists: {directory}");
        }

        _logger.LogInformation("Creating worktree {Name} at {Directory}", slugName, directory);

        // git worktree add --no-checkout -b branch directory
        var process = await _shellService.SpawnAsync("git", new[] { "worktree", "add", "--no-checkout", "-b", branch, directory }, _projectContext.Directory);
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"Failed to create git worktree: {process.ExitCode}");
        }

        // git reset --hard in the new directory
        var resetProcess = await _shellService.SpawnAsync("git", new[] { "reset", "--hard" }, directory);
        await resetProcess.WaitForExitAsync();

        // 自动加载/同步 .env 文件
        await SyncEnvFilesAsync(_projectContext.Directory, directory);

        // 自动恢复环境
        await RestoreEnvironmentAsync(directory);

        if (!string.IsNullOrEmpty(startCommand))
        {
            _logger.LogInformation("Running start command in worktree: {Command}", startCommand);
            var startProcess = await _shellService.SpawnAsync(startCommand, null, directory);
            // We don't necessarily wait for start command if it's a long-running process
        }

        return new WorktreeInfo(slugName, branch, directory, DateTime.UtcNow, DateTime.UtcNow);
    }

    public async Task CleanupExpiredWorktreesAsync(TimeSpan expiry = default)
    {
        if (expiry == default) expiry = TimeSpan.FromDays(7); // 默认 7 天过期

        var worktrees = await ListAsync();
        var now = DateTime.UtcNow;

        foreach (var wt in worktrees)
        {
            // 如果目录不存在，git worktree remove 也会清理引用
            if (!Directory.Exists(wt.Directory))
            {
                await RemoveAsync(wt.Directory);
                continue;
            }

            // 检查最后访问时间（使用目录的最后写入时间作为参考）
            var lastWrite = Directory.GetLastWriteTimeUtc(wt.Directory);
            if (now - lastWrite > expiry)
            {
                _logger.LogInformation("Cleaning up expired worktree: {Name} (Last modified: {LastWrite})", wt.Name, lastWrite);
                await RemoveAsync(wt.Directory);
            }
        }
    }

    private async Task SyncEnvFilesAsync(string sourceDir, string targetDir)
    {
        try
        {
            var envFiles = Directory.GetFiles(sourceDir, ".env*");
            foreach (var envFile in envFiles)
            {
                var fileName = Path.GetFileName(envFile);
                var destPath = Path.Combine(targetDir, fileName);
                
                // 仅在目标不存在时同步，保持环境隔离
                if (!File.Exists(destPath))
                {
                    _logger.LogInformation("Syncing {File} to worktree...", fileName);
                    File.Copy(envFile, destPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sync .env files to {Directory}", targetDir);
        }
    }

    public async Task<List<WorktreeInfo>> ListAsync()
    {
        var process = await _shellService.SpawnAsync("git", new[] { "worktree", "list", "--porcelain" }, _projectContext.Directory);
        var output = new System.Text.StringBuilder();
        process.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        await process.WaitForExitAsync();

        var worktrees = new List<WorktreeInfo>();
        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        string? currentPath = null;
        foreach (var line in lines)
        {
            if (line.StartsWith("worktree "))
            {
                currentPath = line.Substring("worktree ".Length).Trim();
            }
            else if (line.StartsWith("branch ") && currentPath != null)
            {
                var branch = line.Substring("branch ".Length).Trim().Replace("refs/heads/", "");
                var name = Path.GetFileName(currentPath);
                
                var createdAt = Directory.GetCreationTimeUtc(currentPath);
                var lastWrite = Directory.GetLastWriteTimeUtc(currentPath);
                
                worktrees.Add(new WorktreeInfo(name, branch, currentPath, createdAt, lastWrite));
                currentPath = null;
            }
        }

        return worktrees;
    }

    public async Task RemoveAsync(string directory)
    {
        _logger.LogInformation("Removing worktree at {Directory}", directory);
        
        var process = await _shellService.SpawnAsync("git", new[] { "worktree", "remove", "--force", directory }, _projectContext.Directory);
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"Failed to remove git worktree: {process.ExitCode}");
        }
    }

    public async Task ResetAsync(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new Exception($"Worktree directory not found: {directory}");
        }

        var branch = await GetPrimaryBranchAsync();
        await RunGitAsync(new[] { "fetch", "origin", branch }, directory);
        await RunGitAsync(new[] { "reset", "--hard", $"origin/{branch}" }, directory);
        await RunGitAsync(new[] { "clean", "-fdx" }, directory);
        await RunGitAsync(new[] { "submodule", "update", "--init", "--recursive", "--force" }, directory);
    }

    private string Slug(string input)
    {
        return input.Trim().ToLower().Replace(" ", "-").Replace("\\", "-").Replace("/", "-");
    }

    private async Task<string> GetPrimaryBranchAsync()
    {
        var head = await RunGitAsync(new[] { "symbolic-ref", "refs/remotes/origin/HEAD" }, _projectContext.Directory);
        if (head.ExitCode == 0 && !string.IsNullOrWhiteSpace(head.Output))
        {
            var parts = head.Output.Trim().Split('/');
            return parts.Last();
        }

        var main = await RunGitAsync(new[] { "show-ref", "--verify", "--quiet", "refs/remotes/origin/main" }, _projectContext.Directory);
        if (main.ExitCode == 0) return "main";

        var master = await RunGitAsync(new[] { "show-ref", "--verify", "--quiet", "refs/remotes/origin/master" }, _projectContext.Directory);
        if (master.ExitCode == 0) return "master";

        return "main";
    }

    private async Task<CommandResult> RunGitAsync(string[] args, string directory)
    {
        var process = await _shellService.SpawnAsync("git", args, directory);
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            _logger.LogWarning("Git command failed: {Args}\n{Error}", string.Join(" ", args), error);
        }

        return new CommandResult(process.ExitCode, output.Trim(), error.Trim());
    }

    private record CommandResult(int ExitCode, string Output, string Error);

    private async Task RestoreEnvironmentAsync(string directory)
    {
        try
        {
            // 1. .NET 项目恢复
            if (Directory.GetFiles(directory, "*.sln").Any() || Directory.GetFiles(directory, "*.csproj").Any())
            {
                _logger.LogInformation("Restoring .NET dependencies in {Directory}...", directory);
                var process = await _shellService.SpawnAsync("dotnet", new[] { "restore" }, directory);
                await process.WaitForExitAsync();
            }

            // 2. Node.js 项目恢复
            if (File.Exists(Path.Combine(directory, "package.json")))
            {
                var lockFile = File.Exists(Path.Combine(directory, "package-lock.json")) ? "npm" :
                              File.Exists(Path.Combine(directory, "yarn.lock")) ? "yarn" :
                              File.Exists(Path.Combine(directory, "pnpm-lock.yaml")) ? "pnpm" : "npm";

                _logger.LogInformation("Restoring Node.js dependencies using {Manager} in {Directory}...", lockFile, directory);
                var process = await _shellService.SpawnAsync(lockFile, new[] { "install" }, directory);
                await process.WaitForExitAsync();
            }

            // 3. Python 项目恢复 (带 venv 支持)
            if (File.Exists(Path.Combine(directory, "requirements.txt")) || File.Exists(Path.Combine(directory, "pyproject.toml")))
            {
                var venvPath = Path.Combine(directory, ".venv");
                if (!Directory.Exists(venvPath))
                {
                    _logger.LogInformation("Creating Python virtual environment in {Directory}...", directory);
                    var venvProcess = await _shellService.SpawnAsync("python", new[] { "-m", "venv", ".venv" }, directory);
                    await venvProcess.WaitForExitAsync();
                }

                // 在 Windows 上使用 .venv\Scripts\python，在其他系统上使用 .venv/bin/python
                var pythonExe = Path.Combine(venvPath, OperatingSystem.IsWindows() ? "Scripts\\python.exe" : "bin/python");
                
                if (File.Exists(Path.Combine(directory, "requirements.txt")))
                {
                    _logger.LogInformation("Restoring Python dependencies from requirements.txt in {Directory}...", directory);
                    var process = await _shellService.SpawnAsync(pythonExe, new[] { "-m", "pip", "install", "-r", "requirements.txt" }, directory);
                    await process.WaitForExitAsync();
                }
                else if (File.Exists(Path.Combine(directory, "pyproject.toml")))
                {
                    _logger.LogInformation("Restoring Python dependencies using pip in {Directory}...", directory);
                    var process = await _shellService.SpawnAsync(pythonExe, new[] { "-m", "pip", "install", "." }, directory);
                    await process.WaitForExitAsync();
                }
            }

            // 4. Rust 项目恢复
            if (File.Exists(Path.Combine(directory, "Cargo.toml")))
            {
                _logger.LogInformation("Restoring Rust dependencies in {Directory}...", directory);
                var process = await _shellService.SpawnAsync("cargo", new[] { "fetch" }, directory);
                await process.WaitForExitAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Environment restoration failed in {Directory}. You may need to restore dependencies manually.", directory);
        }
    }
}
