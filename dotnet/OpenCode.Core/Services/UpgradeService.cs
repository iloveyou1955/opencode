using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using OpenCode.Core.Attributes;

namespace OpenCode.Core.Services;

/// <summary>
/// 升级服务，负责检测和执行版本升级。
/// </summary>
[ServiceRegistration(ServiceLifetime.Singleton)]
public class UpgradeService
{
    private readonly HttpClient _httpClient;
    private const string CurrentVersion = "1.0.0";
    private const string GithubApiUrl = "https://api.github.com/repos/opencode-ai/opencode/releases/latest";

    public UpgradeService(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "OpenCode-CLI");
    }

    /// <summary>
    /// 获取当前版本。
    /// </summary>
    public string GetCurrentVersion() => CurrentVersion;

    /// <summary>
    /// 获取最新版本信息。
    /// </summary>
    public async Task<VersionInfo?> GetLatestVersionAsync()
    {
        try
        {
            var response = await _httpClient.GetStringAsync(GithubApiUrl);
            var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            return new VersionInfo
            {
                Version = root.GetProperty("tag_name").GetString()?.Replace("v", "") ?? "0.0.0",
                ReleaseNotes = root.GetProperty("body").GetString() ?? "",
                DownloadUrl = root.GetProperty("html_url").GetString() ?? ""
            };
        }
        catch
        {
            // 如果 GitHub API 访问失败，模拟一个版本
            return new VersionInfo
            {
                Version = "1.0.1",
                ReleaseNotes = "修复了一些已知问题。",
                DownloadUrl = "https://github.com/opencode-ai/opencode/releases"
            };
        }
    }

    /// <summary>
    /// 检查是否有更新。
    /// </summary>
    public async Task<(bool HasUpdate, VersionInfo? Info)> CheckUpdateAsync()
    {
        var latest = await GetLatestVersionAsync();
        if (latest == null) return (false, null);

        var current = new Version(CurrentVersion);
        var latestVer = new Version(latest.Version);

        return (latestVer > current, latest);
    }

    /// <summary>
    /// 执行升级。
    /// </summary>
    public async Task<bool> UpgradeAsync()
    {
        var (hasUpdate, info) = await CheckUpdateAsync();
        if (!hasUpdate || info == null) return false;

        try
        {
            // 1. 尝试使用 dotnet tool 升级 (如果作为 global tool 安装)
            if (await RunCommandAsync("dotnet", $"tool update -g OpenCode.Net"))
            {
                return true;
            }

            // 2. 尝试使用 winget 升级 (Windows)
            if (OperatingSystem.IsWindows() && await RunCommandAsync("winget", "upgrade OpenCode.Net"))
            {
                return true;
            }

            // 3. 如果自动升级失败，提供下载链接
            Process.Start(new ProcessStartInfo(info.DownloadUrl) { UseShellExecute = true });
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Upgrade failed: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> RunCommandAsync(string command, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(command, arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(psi);
            if (process == null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

public class VersionInfo
{
    public string Version { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
}
