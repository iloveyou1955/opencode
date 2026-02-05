using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace OpenCode.Core.Services;

public class ConfigService
{
    private readonly string _projectRoot;
    private readonly ILogger<ConfigService> _logger;
    private ConfigInfo _config;

    public ConfigService(string projectRoot, ILogger<ConfigService> logger)
    {
        _projectRoot = projectRoot;
        _logger = logger;
        _config = new ConfigInfo();
    }

    public ConfigInfo Config => _config;

    public async Task UpdateCompactionAsync(bool auto, bool prune)
    {
        _config.Compaction ??= new CompactionConfig();
        _config.Compaction.Auto = auto;
        _config.Compaction.Prune = prune;
        await SaveAsync();
    }

    public async Task SaveAsync()
    {
        var dotOpencodePath = Path.Combine(_projectRoot, ".opencode", "opencode.json");
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(_config, options);
        await File.WriteAllTextAsync(dotOpencodePath, json);
    }

    public async Task LoadAsync()
    {
        var result = new ConfigInfo();

        // 1. 加载全局配置 (~/.config/opencode/opencode.json)
        var globalPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "opencode", "opencode.json");
        await MergeFileAsync(result, globalPath);

        // 2. 加载项目配置 (opencode.json)
        var projectPath = Path.Combine(_projectRoot, "opencode.json");
        await MergeFileAsync(result, projectPath);

        // 3. 加载 .opencode/opencode.json
        var dotOpencodePath = Path.Combine(_projectRoot, ".opencode", "opencode.json");
        await MergeFileAsync(result, dotOpencodePath);

        // 4. 加载环境变量配置 (OPENCODE_CONFIG_CONTENT)
        var envConfig = Environment.GetEnvironmentVariable("OPENCODE_CONFIG_CONTENT");
        if (!string.IsNullOrEmpty(envConfig))
        {
            try 
            {
                var options = new JsonSerializerOptions { AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip };
                var source = JsonSerializer.Deserialize<ConfigInfo>(envConfig, options);
                if (source != null) MergeConfigs(result, source);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load config from OPENCODE_CONFIG_CONTENT");
            }
        }

        // 5. 应用环境变量覆盖
        if (Environment.GetEnvironmentVariable("OPENCODE_DISABLE_AUTOCOMPACT") == "true")
        {
            result.Compaction ??= new CompactionConfig();
            result.Compaction.Auto = false;
        }
        if (Environment.GetEnvironmentVariable("OPENCODE_DISABLE_PRUNE") == "true")
        {
            result.Compaction ??= new CompactionConfig();
            result.Compaction.Prune = false;
        }

        _config = result;
    }

    private async Task MergeFileAsync(ConfigInfo target, string path)
    {
        if (!File.Exists(path)) return;

        try
        {
            var text = await File.ReadAllTextAsync(path);
            text = ResolveVariables(text);
            
            var options = new JsonSerializerOptions { AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip };
            var source = JsonSerializer.Deserialize<ConfigInfo>(text, options);

            if (source != null)
            {
                MergeConfigs(target, source);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load config from {Path}", path);
        }
    }

    private string ResolveVariables(string text)
    {
        // 替换 {env:VAR}
        text = Regex.Replace(text, @"\{env:([^}]+)\}", m => 
            Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? "");

        // 替换 {file:path}
        text = Regex.Replace(text, @"\{file:([^}]+)\}", m => {
            var filePath = m.Groups[1].Value;
            if (filePath.StartsWith("~/"))
            {
                filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), filePath.Substring(2));
            }
            else if (!Path.IsPathRooted(filePath))
            {
                filePath = Path.Combine(_projectRoot, filePath);
            }

            return File.Exists(filePath) ? File.ReadAllText(filePath).Replace("\"", "\\\"").Replace("\n", "\\n") : "";
        });

        return text;
    }

    private void MergeConfigs(ConfigInfo target, ConfigInfo source)
    {
        if (!string.IsNullOrEmpty(source.Model)) target.Model = source.Model;
        if (!string.IsNullOrEmpty(source.SmallModel)) target.SmallModel = source.SmallModel;
        if (!string.IsNullOrEmpty(source.DefaultAgent)) target.DefaultAgent = source.DefaultAgent;

        if (source.Providers != null)
        {
            target.Providers ??= new();
            foreach (var kvp in source.Providers) target.Providers[kvp.Key] = kvp.Value;
        }

        if (source.Agents != null)
        {
            target.Agents ??= new();
            foreach (var kvp in source.Agents) target.Agents[kvp.Key] = kvp.Value;
        }

        if (source.Permissions != null)
        {
            target.Permissions ??= new();
            foreach (var kvp in source.Permissions) target.Permissions[kvp.Key] = kvp.Value;
        }

        if (source.Mcp != null)
        {
            target.Mcp ??= new();
            foreach (var kvp in source.Mcp) target.Mcp[kvp.Key] = kvp.Value;
        }
    }
}
