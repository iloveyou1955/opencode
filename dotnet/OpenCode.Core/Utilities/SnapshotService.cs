using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using OpenCode.Core.Contracts;
using OpenCode.Core.Services;
using OpenCode.Core.Attributes;

namespace OpenCode.Core.Utilities;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class SnapshotService
{
    private readonly IProjectContext _projectContext;
    private readonly ILogger<SnapshotService> _logger;
    private readonly string _snapshotGitDir;

    public SnapshotService(IProjectContext projectContext, ILogger<SnapshotService> logger)
    {
        _projectContext = projectContext;
        _logger = logger;
        
        // 存储快照 Git 仓库的路径，通常在用户数据目录下
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenCode", "snapshot");
        _snapshotGitDir = Path.Combine(dataDir, GetSafeId(_projectContext.Directory));
        
        if (!Directory.Exists(_snapshotGitDir))
        {
            Directory.CreateDirectory(_snapshotGitDir);
        }
    }

    private string GetSafeId(string path)
    {
        // 简单哈希一下路径作为 ID
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(path));
        return BitConverter.ToString(hash).Replace("-", "").ToLower().Substring(0, 12);
    }

    public async Task<string?> TrackAsync()
    {
        try
        {
            if (!Directory.Exists(Path.Combine(_snapshotGitDir, ".git")))
            {
                await RunGitAsync("init");
                await RunGitAsync("config core.autocrlf false");
            }

            await RunGitAsync("add .");
            var result = await RunGitAsync("write-tree");
            var hash = result.Trim();
            _logger.LogInformation("Snapshot tracked: {Hash}", hash);
            return hash;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to track snapshot");
            return null;
        }
    }

    public async Task<bool> RestoreAsync(string hash)
    {
        try
        {
            _logger.LogInformation("Restoring snapshot: {Hash}", hash);
            // read-tree 将索引更新为指定哈希，checkout-index 将文件检出到工作区
            await RunGitAsync($"read-tree {hash}");
            await RunGitAsync("checkout-index -a -f");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore snapshot {Hash}", hash);
            return false;
        }
    }

    public async Task<List<FileDiff>> DiffFullAsync(string from, string to)
    {
        var result = new List<FileDiff>();
        try
        {
            // 1. 获取变更状态 (A, D, M)
            var statusOutput = await RunGitAsync($"-c core.autocrlf=false -c core.quotepath=false diff --no-ext-diff --name-status --no-renames {from} {to} -- .");
            var statusMap = new Dictionary<string, string>();
            foreach (var line in statusOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('\t', 2);
                if (parts.Length < 2) continue;
                var code = parts[0];
                var file = parts[1];
                var kind = code.StartsWith("A") ? "added" : code.StartsWith("D") ? "deleted" : "modified";
                statusMap[file] = kind;
            }

            // 2. 获取数值统计 (additions, deletions)
            var numstatOutput = await RunGitAsync($"-c core.autocrlf=false -c core.quotepath=false diff --no-ext-diff --no-renames --numstat {from} {to} -- .");
            foreach (var line in numstatOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('\t', 3);
                if (parts.Length < 3) continue;

                var additionsStr = parts[0];
                var deletionsStr = parts[1];
                var file = parts[2];

                var isBinary = additionsStr == "-" && deletionsStr == "-";
                var additions = isBinary ? 0 : int.Parse(additionsStr);
                var deletions = isBinary ? 0 : int.Parse(deletionsStr);

                // 3. 获取修改前后的内容
                string before = string.Empty;
                string after = string.Empty;

                if (!isBinary)
                {
                    try { before = await RunGitAsync($"-c core.autocrlf=false show {from}:{file}"); } catch { }
                    try { after = await RunGitAsync($"-c core.autocrlf=false show {to}:{file}"); } catch { }
                }

                result.Add(new FileDiff
                {
                    File = file,
                    Before = before,
                    After = after,
                    Additions = additions,
                    Deletions = deletions,
                    Status = statusMap.GetValueOrDefault(file, "modified")
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get full diff between {From} and {To}", from, to);
        }
        return result;
    }

    public class FileDiff
    {
        public string File { get; set; } = string.Empty;
        public string Before { get; set; } = string.Empty;
        public string After { get; set; } = string.Empty;
        public int Additions { get; set; }
        public int Deletions { get; set; }
        public string Status { get; set; } = "modified";
    }

    public class Patch
    {
        public string Hash { get; set; } = string.Empty;
        public List<string> Files { get; set; } = new();
    }

    public async Task<Patch> GetPatchAsync(string hash)
    {
        try
        {
            await RunGitAsync("add .");
            var result = await RunGitAsync($"-c core.autocrlf=false -c core.quotepath=false diff --no-ext-diff --name-only {hash} -- .");
            var files = result.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                             .Select(f => Path.Combine(_projectContext.Directory, f.Trim()))
                             .ToList();
            return new Patch { Hash = hash, Files = files };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get patch for {Hash}", hash);
            return new Patch { Hash = hash };
        }
    }

    public async Task<bool> RevertPatchesAsync(List<Patch> patches)
    {
        var revertedFiles = new HashSet<string>();
        try
        {
            foreach (var patch in patches)
            {
                foreach (var file in patch.Files)
                {
                    if (revertedFiles.Contains(file)) continue;

                    _logger.LogInformation("Reverting file {File} to hash {Hash}", file, patch.Hash);
                    try
                    {
                        await RunGitAsync($"checkout {patch.Hash} -- {file}");
                    }
                    catch
                    {
                        // 如果 checkout 失败，检查文件是否在快照中存在
                        var relativePath = Path.GetRelativePath(_projectContext.Directory, file);
                        var checkTree = await RunGitAsync($"ls-tree {patch.Hash} -- {relativePath}");
                        if (string.IsNullOrWhiteSpace(checkTree))
                        {
                            _logger.LogInformation("File {File} did not exist in snapshot, deleting", file);
                            if (File.Exists(file)) File.Delete(file);
                        }
                        else
                        {
                            _logger.LogWarning("File {File} existed in snapshot but checkout failed, keeping", file);
                        }
                    }
                    revertedFiles.Add(file);
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revert patches");
            return false;
        }
    }

    private async Task<string> RunGitAsync(string arguments)
    {
        var psi = new ProcessStartInfo("git")
        {
            Arguments = arguments,
            WorkingDirectory = _projectContext.Directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // 设置 Git 环境变量，使其使用独立的 Git 目录，但操作当前工作树
        psi.EnvironmentVariables["GIT_DIR"] = Path.Combine(_snapshotGitDir, ".git");
        psi.EnvironmentVariables["GIT_WORK_TREE"] = _projectContext.Directory;

        using var process = Process.Start(psi);
        if (process == null) throw new Exception("Failed to start git process");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = await errorTask;
            throw new Exception($"Git command failed: {arguments}\nError: {error}");
        }

        return await outputTask;
    }
}
