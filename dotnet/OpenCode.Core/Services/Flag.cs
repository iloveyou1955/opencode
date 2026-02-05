namespace OpenCode.Core.Services;

public static class Flag
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _overrides = new();

    private static bool GetTruthy(string key)
    {
        if (_overrides.TryGetValue(key, out var overridden)) return overridden;
        var value = Environment.GetEnvironmentVariable(key)?.ToLower();
        return value == "true" || value == "1";
    }

    public static void SetTruthy(string key, bool value)
    {
        _overrides[key] = value;
    }

    public static Dictionary<string, bool> GetStatus()
    {
        return new Dictionary<string, bool>
        {
            ["EXPERIMENTAL"] = Experimental,
            ["EXPERIMENTAL_FILEWATCHER"] = ExperimentalFileWatcher,
            ["EXPERIMENTAL_ICON_DISCOVERY"] = ExperimentalIconDiscovery,
            ["ENABLE_EXA"] = EnableExa,
            ["EXPERIMENTAL_OXFMT"] = ExperimentalOxfmt,
            ["EXPERIMENTAL_PLAN_MODE"] = ExperimentalPlanMode,
            ["DISABLE_LSP"] = DisableLsp,
            ["AUTO_SHARE"] = AutoShare
        };
    }

    private static int? GetNumber(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (int.TryParse(value, out var result) && result > 0) return result;
        return null;
    }

    public static bool AutoShare => GetTruthy("OPENCODE_AUTO_SHARE");
    public static string? GitBashPath => Environment.GetEnvironmentVariable("OPENCODE_GIT_BASH_PATH");
    public static string? Config => Environment.GetEnvironmentVariable("OPENCODE_CONFIG");
    public static string? ConfigDir => Environment.GetEnvironmentVariable("OPENCODE_CONFIG_DIR");
    public static bool DisableAutoUpdate => GetTruthy("OPENCODE_DISABLE_AUTOUPDATE");
    public static bool DisablePrune => GetTruthy("OPENCODE_DISABLE_PRUNE");
    public static bool DisableTerminalTitle => GetTruthy("OPENCODE_DISABLE_TERMINAL_TITLE");
    public static string? Permission => Environment.GetEnvironmentVariable("OPENCODE_PERMISSION");
    public static bool DisableDefaultPlugins => GetTruthy("OPENCODE_DISABLE_DEFAULT_PLUGINS");
    public static bool DisableLspDownload => GetTruthy("OPENCODE_DISABLE_LSP_DOWNLOAD");
    public static bool EnableExperimentalModels => GetTruthy("OPENCODE_ENABLE_EXPERIMENTAL_MODELS");
    public static bool DisableAutoCompact => GetTruthy("OPENCODE_DISABLE_AUTOCOMPACT");
    public static bool DisableModelsFetch => GetTruthy("OPENCODE_DISABLE_MODELS_FETCH");
    public static bool DisableClaudeCode => GetTruthy("OPENCODE_DISABLE_CLAUDE_CODE");
    public static bool DisableLsp => GetTruthy("OPENCODE_DISABLE_LSP");
    public static string? ServerPassword => Environment.GetEnvironmentVariable("OPENCODE_SERVER_PASSWORD");
    public static string? ServerUsername => Environment.GetEnvironmentVariable("OPENCODE_SERVER_USERNAME");

    // Language Paths
    public static string? NodePath => Environment.GetEnvironmentVariable("OPENCODE_NODE_PATH");
    public static string? PythonPath => Environment.GetEnvironmentVariable("OPENCODE_PYTHON_PATH");
    public static string? DotnetPath => Environment.GetEnvironmentVariable("OPENCODE_DOTNET_PATH");
    public static string? GoPath => Environment.GetEnvironmentVariable("OPENCODE_GO_PATH");
    public static string? RustPath => Environment.GetEnvironmentVariable("OPENCODE_RUST_PATH");
    public static string? JavaPath => Environment.GetEnvironmentVariable("OPENCODE_JAVA_PATH");

    // Experimental
    public static bool Experimental => GetTruthy("OPENCODE_EXPERIMENTAL");
    public static bool ExperimentalFileWatcher => GetTruthy("OPENCODE_EXPERIMENTAL_FILEWATCHER");
    public static bool ExperimentalIconDiscovery => Experimental || GetTruthy("OPENCODE_EXPERIMENTAL_ICON_DISCOVERY");
    public static bool EnableExa => GetTruthy("OPENCODE_ENABLE_EXA") || Experimental || GetTruthy("OPENCODE_EXPERIMENTAL_EXA");
    public static int? BashDefaultTimeoutMs => GetNumber("OPENCODE_EXPERIMENTAL_BASH_DEFAULT_TIMEOUT_MS");
    public static bool ExperimentalOxfmt => Experimental || GetTruthy("OPENCODE_EXPERIMENTAL_OXFMT");
    public static bool ExperimentalPlanMode => Experimental || GetTruthy("OPENCODE_EXPERIMENTAL_PLAN_MODE");
    public static string? ModelsUrl => Environment.GetEnvironmentVariable("OPENCODE_MODELS_URL");
    public static string? ModelsPath => Environment.GetEnvironmentVariable("OPENCODE_MODELS_PATH");

    public static string Client => Environment.GetEnvironmentVariable("OPENCODE_CLIENT") ?? "cli";
}
