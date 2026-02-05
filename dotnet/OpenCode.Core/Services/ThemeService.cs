using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCode.Core.Services;

public record ThemeInfo(
    string Name,
    Dictionary<string, string> Colors
);

public class ThemeService
{
    private readonly string _themeDir;
    private readonly Dictionary<string, ThemeInfo> _themes = new();

    public ThemeService(string projectRoot)
    {
        _themeDir = Path.Combine(projectRoot, "packages", "opencode", "src", "cli", "cmd", "tui", "context", "theme");
        LoadThemes();
    }

    private void LoadThemes()
    {
        if (!Directory.Exists(_themeDir)) return;

        foreach (var file in Directory.GetFiles(_themeDir, "*.json"))
        {
            try
            {
                var content = File.ReadAllText(file);
                var colors = JsonSerializer.Deserialize<Dictionary<string, string>>(content);
                if (colors != null)
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    _themes[name] = new ThemeInfo(name, colors);
                }
            }
            catch { }
        }
    }

    public IEnumerable<string> ListThemes() => _themes.Keys;

    public ThemeInfo? GetTheme(string name)
    {
        return _themes.TryGetValue(name, out var theme) ? theme : null;
    }
}
