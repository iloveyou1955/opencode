namespace OpenCode.Core.Contracts;

public interface IProjectContext
{
    string Directory { get; }
    string Worktree { get; }
    bool ContainsPath(string path);
    string GetRelativePath(string path);
    string ResolvePath(string path);
}

public class ProjectContext : IProjectContext
{
    public string Directory { get; }
    public string Worktree { get; }

    public ProjectContext(string directory, string? worktree = null)
    {
        Directory = Path.GetFullPath(directory);
        Worktree = worktree != null ? Path.GetFullPath(worktree) : Directory;
    }

    public bool ContainsPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        
        return IsChildOf(fullPath, Directory) || IsChildOf(fullPath, Worktree);
    }

    private static bool IsChildOf(string path, string parent)
    {
        if (string.IsNullOrEmpty(parent)) return false;
        
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (normalizedPath.Equals(normalizedParent, StringComparison.OrdinalIgnoreCase)) return true;

        return normalizedPath.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public string GetRelativePath(string path)
    {
        return Path.GetRelativePath(Directory, path);
    }

    public string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path)) return path;
        return Path.Combine(Directory, path);
    }
}
