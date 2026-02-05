using System.Threading.Tasks;

namespace OpenCode.TUI.Services
{
    /// <summary>
    /// 指令处理器接口
    /// </summary>
    public interface ICommandProcessor
    {
        /// <summary>
        /// 处理用户输入的指令
        /// </summary>
        /// <param name="input">用户输入的原始文本</param>
        /// <param name="context">执行上下文</param>
        /// <returns>如果指令被处理且不需要继续执行后续逻辑（如 AI 对话），返回 true；否则返回 false</returns>
        Task<bool> ProcessCommandAsync(string input, CommandContext context);

        /// <summary>
        /// 读取用户输入，并处理快捷键
        /// </summary>
        /// <param name="context">执行上下文</param>
        /// <returns>用户输入的文本；如果是快捷键处理且需要刷新，返回 null</returns>
        Task<string?> ReadInputAsync(CommandContext context);
    }
}
