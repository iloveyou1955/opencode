using System;

namespace OpenCode.TUI.Services
{
    /// <summary>
    /// 指令执行上下文，保存 TUI 运行时的状态
    /// </summary>
    public class CommandContext
    {
        public required string CurrentSessionId { get; set; }
        public bool IsFirstMessage { get; set; }
        public bool ShowDetails { get; set; }
        public bool ShowThinking { get; set; }
        public bool ShowTimestamps { get; set; }
        public required string ProjectRoot { get; set; }
        public required IServiceProvider ServiceProvider { get; set; }
        public required TuiManager Tui { get; set; }
        
        // 用于指令更新外部状态的回调
        public required Action<string> SetSessionId { get; set; }
        public required Action<bool> SetFirstMessage { get; set; }
        public required Action<bool> SetShowDetails { get; set; }
        public required Action<bool> SetShowThinking { get; set; }
        public required Action<bool> SetShowTimestamps { get; set; }
    }
}
