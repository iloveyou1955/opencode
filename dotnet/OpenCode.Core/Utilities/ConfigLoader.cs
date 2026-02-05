using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCode.Core.Utilities;

public static class ConfigLoader
{
    private static readonly Regex EnvRegex = new(@"\{env:([^}]+)\}", RegexOptions.Compiled);
    private static readonly Regex FileRegex = new(@"\{file:([^}]+)\}", RegexOptions.Compiled);

    /// <summary>
    /// Replaces {env:VAR} and {file:PATH} variables in the text.
    /// </summary>
    public static string ReplaceVariables(string text, string configDir)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // 1. Replace {env:VAR}
        text = EnvRegex.Replace(text, m => 
            Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? "");

        // 2. Replace {file:PATH}
        text = FileRegex.Replace(text, m => 
        {
            var path = m.Groups[1].Value;
            if (path.StartsWith("~/"))
            {
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Substring(2));
            }
            
            var resolvedPath = Path.IsPathRooted(path) ? path : Path.Combine(configDir, path);
            try
            {
                if (File.Exists(resolvedPath))
                {
                    var content = File.ReadAllText(resolvedPath).Trim();
                    // Escape for JSON if needed (but usually these are injected into template strings)
                    return content;
                }
            }
            catch
            {
                // Ignore errors
            }
            return m.Value;
        });

        return text;
    }

    /// <summary>
    /// Parses a JSONC (JSON with comments) string.
    /// </summary>
    public static JsonNode? ParseJsonc(string json)
    {
        var options = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        using var doc = JsonDocument.Parse(json, options);
        return JsonNode.Parse(doc.RootElement.GetRawText());
    }
}
