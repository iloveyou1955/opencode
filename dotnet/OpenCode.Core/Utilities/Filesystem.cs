using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OpenCode.Core.Utilities;

public static class Filesystem
{
    /// <summary>
    /// 在 Windows 上，将路径规范化为其在文件系统中的实际大小写。
    /// </summary>
    public static string NormalizePath(string path)
    {
        if (!OperatingSystem.IsWindows()) return path;

        try
        {
            // GetFullPath 已经处理了大部分规范化，但在 Windows 上可能不保证大小写一致
            var fullPath = Path.GetFullPath(path);
            
            // 使用 DirectoryInfo 获取文件系统中的实际名称（包括大小写）
            if (File.Exists(fullPath))
            {
                var fileInfo = new FileInfo(fullPath);
                return fileInfo.FullName;
            }
            if (Directory.Exists(fullPath))
            {
                var dirInfo = new DirectoryInfo(fullPath);
                return dirInfo.FullName;
            }
            return fullPath;
        }
        catch
        {
            return path;
        }
    }

    public static bool Overlaps(string a, string b)
    {
        var relA = Path.GetRelativePath(a, b);
        var relB = Path.GetRelativePath(b, a);
        return string.IsNullOrEmpty(relA) || !relA.StartsWith("..") || 
               string.IsNullOrEmpty(relB) || !relB.StartsWith("..");
    }

    public static bool Contains(string parent, string child)
    {
        var relative = Path.GetRelativePath(parent, child);
        return !relative.StartsWith("..");
    }

    public static async Task<List<string>> FindUpAsync(string target, string start, string? stop = null)
    {
        var result = new List<string>();
        var current = Path.GetFullPath(start);
        var stopPath = stop != null ? Path.GetFullPath(stop) : null;

        while (true)
        {
            var search = Path.Combine(current, target);
            if (File.Exists(search) || Directory.Exists(search))
            {
                result.Add(search);
            }

            if (current == stopPath) break;

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || parent == current) break;
            
            current = parent;
        }

        return result;
    }

    public static async IAsyncEnumerable<string> UpAsync(IEnumerable<string> targets, string start, string? stop = null)
    {
        var current = Path.GetFullPath(start);
        var stopPath = stop != null ? Path.GetFullPath(stop) : null;

        while (true)
        {
            foreach (var target in targets)
            {
                var search = Path.Combine(current, target);
                if (File.Exists(search) || Directory.Exists(search))
                {
                    yield return search;
                }
            }

            if (current == stopPath) break;

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || parent == current) break;

            current = parent;
        }
    }

    public static async Task<List<string>> GlobUpAsync(string pattern, string start, string? stop = null)
    {
        var result = new List<string>();
        var current = Path.GetFullPath(start);
        var stopPath = stop != null ? Path.GetFullPath(stop) : null;

        while (true)
        {
            try
            {
                // 使用 .NET 核心的枚举方法
                var matches = Directory.EnumerateFiles(current, pattern, SearchOption.TopDirectoryOnly);
                result.AddRange(matches);
            }
            catch
            {
                // 忽略无效的 pattern 或无权限目录
            }

            if (current == stopPath) break;

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || parent == current) break;

            current = parent;
        }

        return result;
    }
}
