using System.Text.RegularExpressions;

namespace OpenCode.Core.Utilities;

public static class WildcardMatcher
{
    /// <summary>
    /// Checks if a string matches a wildcard pattern.
    /// Supports * (any chars except separator) and ** (any chars including separator).
    /// Aligned with .gitignore-like behavior.
    /// </summary>
    public static bool Match(string input, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return false;
        if (pattern == "*") return true;
        if (pattern == "**") return true;

        // Normalize paths to use forward slashes for consistency in matching
        input = input.Replace("\\", "/");
        pattern = pattern.Replace("\\", "/");

        // Handle patterns starting with / (root relative)
        bool rootRelative = pattern.StartsWith("/");
        if (rootRelative) pattern = pattern.Substring(1);

        // If it doesn't contain a slash (excluding trailing slash), it matches in any directory
        bool anyDirectory = !pattern.TrimEnd('/').Contains("/");
        
        string regexPattern;
        if (anyDirectory)
        {
            // Match filename in any directory
            regexPattern = "(^|/)" + GlobToRegex(pattern) + "($|/)";
        }
        else
        {
            // Match from root
            regexPattern = "^" + GlobToRegex(pattern) + "($|/)";
        }

        return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase);
    }

    private static string GlobToRegex(string pattern)
    {
        return Regex.Escape(pattern)
            .Replace(@"\*\*", ".*")             // ** matches everything
            .Replace(@"\*", "[^/]*")            // * matches until separator
            .Replace(@"\?", ".")                // ? matches one char
            .Replace(@"/", @"\/");              // escape slash
    }
}
