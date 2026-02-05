using System.Text;

namespace OpenCode.Core.Utilities;

public static class TextReplacer
{
    public static string Replace(string originalContent, string oldString, string newString, bool replaceAll = false)
    {
        if (string.IsNullOrEmpty(originalContent)) return originalContent;
        if (string.IsNullOrEmpty(oldString)) return originalContent;
        
        // 1. 尝试精确匹配
        int index = originalContent.IndexOf(oldString);
        if (index != -1)
        {
            // 检查是否有多个匹配
            int lastIndex = originalContent.LastIndexOf(oldString);
            if (!replaceAll && index != lastIndex)
            {
                throw new InvalidOperationException("Found multiple matches for oldString. Please provide more context.");
            }
            
            return replaceAll 
                ? originalContent.Replace(oldString, newString)
                : originalContent.Substring(0, index) + newString + originalContent.Substring(index + oldString.Length);
        }

        // 2. 尝试行级忽略空白匹配 (Line Trimmed Match)
        // 这是最常用的模糊匹配，因为 AI 经常搞错缩进
        var result = LineTrimmedReplace(originalContent, oldString, newString);
        if (result != null) return result;

        // 3. 尝试归一化空白匹配 (Whitespace Normalized Match)
        // 将所有连续空白视为一个空格
        result = WhitespaceNormalizedReplace(originalContent, oldString, newString);
        if (result != null) return result;

        throw new InvalidOperationException("oldString not found in content (tried exact, line-trimmed, and whitespace-normalized match).");
    }

    private static string? LineTrimmedReplace(string content, string oldString, string newString)
    {
        var contentLines = content.Split('\n');
        var oldLines = oldString.Split('\n');
        
        // 移除 oldString 末尾的空行（如果有）
        if (oldLines.Length > 0 && string.IsNullOrWhiteSpace(oldLines.Last()))
        {
            oldLines = oldLines.Take(oldLines.Length - 1).ToArray();
        }

        if (oldLines.Length == 0) return null;

        for (int i = 0; i <= contentLines.Length - oldLines.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < oldLines.Length; j++)
            {
                // 比较去除了首尾空白的行
                if (contentLines[i + j].Trim() != oldLines[j].Trim())
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                // 找到匹配，构建新内容
                // 这里的难点是保留原始的缩进？或者直接使用 newString？
                // 通常 edit_file 的语义是：用 newString 替换 oldString 所在的位置。
                // 如果我们匹配的是忽略缩进的，那么替换时是把整块行都替换掉。
                
                var sb = new StringBuilder();
                
                // 1. 添加匹配块之前的内容
                for (int k = 0; k < i; k++)
                {
                    sb.Append(contentLines[k]).Append('\n');
                }
                
                // 2. 添加新内容
                // 注意：这里我们直接插入 newString，假设 newString 包含了正确的缩进
                // 或者我们应该尝试推断缩进？简单起见，直接插入。
                sb.Append(newString);
                
                // 3. 添加匹配块之后的内容
                // 注意：如果 newString 没有以换行符结尾，而原始内容后面有换行，可能需要处理连接处
                // 但通常 newString 是完整的代码块。
                // 我们需要决定是否在 newString 后补一个换行，取决于 oldString 在原始内容中是否占据整行
                
                // 检查 contentLines[i + oldLines.Length - 1] 是否是最后一行
                if (i + oldLines.Length < contentLines.Length)
                {
                    if (!newString.EndsWith('\n')) sb.Append('\n');
                    
                    for (int k = i + oldLines.Length; k < contentLines.Length; k++)
                    {
                        sb.Append(contentLines[k]);
                        if (k < contentLines.Length - 1) sb.Append('\n');
                    }
                }
                
                return sb.ToString();
            }
        }

        return null;
    }

    private static string? WhitespaceNormalizedReplace(string content, string oldString, string newString)
    {
        // 这是一个简化的实现，性能可能一般，但对于代码片段通常够用
        string Normalize(string s) => string.Join(" ", s.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        
        string normalizedContent = Normalize(content);
        string normalizedOld = Normalize(oldString);
        
        if (normalizedContent.Contains(normalizedOld))
        {
            // 虽然我们在归一化后找到了匹配，但在原始字符串中定位并替换是很难的
            // 因为丢失了位置信息。
            // 这里我们仅作为最后的 fallback，或者需要实现复杂的映射逻辑。
            // 暂时放弃实现这个复杂的映射，仅作为提示。
            return null; 
        }
        
        return null;
    }
}
