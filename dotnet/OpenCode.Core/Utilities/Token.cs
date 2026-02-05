namespace OpenCode.Core.Utilities;

/// <summary>
/// Token 计数实用工具类。
/// </summary>
public static class Token
{
    private const int CharsPerToken = 4;

    /// <summary>
    /// 估算给定文本的 Token 数量。
    /// 对齐 TS 实现：Math.max(0, Math.round((input || "").length / CHARS_PER_TOKEN))
    /// </summary>
    /// <param name="input">输入文本。</param>
    /// <returns>估算的 Token 数量。</returns>
    public static int Estimate(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return 0;
        }

        return Math.Max(0, (int)Math.Round((double)input.Length / CharsPerToken));
    }
}
