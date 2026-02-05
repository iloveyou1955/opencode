using System.IO.Compression;

namespace OpenCode.Core.Utilities;

/// <summary>
/// 归档实用工具类，提供 ZIP 压缩包解压功能。
/// </summary>
public static class Archive
{
    /// <summary>
    /// 将指定的 ZIP 文件解压到目标目录。
    /// </summary>
    /// <param name="zipPath">ZIP 文件的绝对路径。</param>
    /// <param name="destDir">解压目标目录的绝对路径。</param>
    /// <param name="overwrite">是否覆盖现有文件。</param>
    public static void ExtractZip(string zipPath, string destDir, bool overwrite = true)
    {
        // 确保目标目录存在
        if (!Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        // 使用 .NET 标准库进行解压，对齐 TS 版本中的跨平台行为
        ZipFile.ExtractToDirectory(zipPath, destDir, overwrite);
    }
}
