namespace OpenCode.Core.Utilities;

public static class BashArity
{
    private static readonly Dictionary<string, int> Arity = new()
    {
        { "cat", 1 }, { "cd", 1 }, { "chmod", 1 }, { "chown", 1 }, { "cp", 1 },
        { "grep", 1 }, { "ls", 1 }, { "mkdir", 1 }, { "mv", 1 }, { "rm", 1 },
        { "git", 2 }, { "git checkout", 3 }, { "git commit", 3 },
        { "npm", 2 }, { "npm run", 3 }, { "npm install", 3 },
        { "dotnet", 2 }, { "dotnet build", 3 }, { "dotnet run", 3 }
    };

    public static string GetPrefix(string command)
    {
        var tokens = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return "";

        for (int len = Math.Min(tokens.Length, 3); len > 0; len--)
        {
            var prefix = string.Join(" ", tokens.Take(len));
            if (Arity.TryGetValue(prefix, out int arity))
            {
                return string.Join(" ", tokens.Take(arity));
            }
        }

        return tokens[0];
    }
}
