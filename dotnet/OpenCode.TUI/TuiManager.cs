using Terminal.Gui;
using OpenCode.Core.Models;
using OpenCode.Core.Services;
using OpenCode.Core.Utilities;
using System.Text.Json.Nodes;
using OpenCode.TUI.Services;
using OpenCode.TUI.UI;

using OpenCode.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.TUI;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class TuiManager
{
    private readonly SessionService _sessionService;
    private readonly StatsService _statsService;
    private readonly DialogService _dialogService;
    private readonly ConfigService _configService;
    
    private Window? _mainWindow;
    private View? _header;
    private View? _inputWrapper;
    private Label? _shortcuts;
    private Label? _tipIcon;
    private Label? _tipText;
    private View? _statusBar;
    private View? _chatContainer;
    private View? _chatHeader;
    private Label? _sessionTitle;
    private Label? _chatStats;
    private TextField? _inputField;
    private TextView? _chatView;

    private bool _isChatMode = false;
    private string _currentStatus = "Ready";
    private string _currentSessionId = "None";
    private List<string> _chatHistory = new();
    private List<string> _sessions = new();
    private List<string> _timeline = new();
    private SessionStats? _stats;

    public event Func<string, Task>? OnCommandSubmitted;

    public TuiManager(SessionService sessionService, StatsService statsService, DialogService dialogService, ConfigService configService)
    {
        _sessionService = sessionService;
        _statsService = statsService;
        _dialogService = dialogService;
        _configService = configService;
    }

    public void InitGui()
    {
        Application.Init();
        var top = Application.Top;

        // --- 顶级色彩方案：深邃、极简、高对比 ---
        // 核心：利用 Terminal.Gui 的 Attribute 实现精细的色彩控制
        var mainScheme = new ColorScheme()
        {
            Normal = Terminal.Gui.Attribute.Make(Color.Gray, Color.Black),
            Focus = Terminal.Gui.Attribute.Make(Color.White, Color.Black),
            HotNormal = Terminal.Gui.Attribute.Make(Color.BrightCyan, Color.Black),
            HotFocus = Terminal.Gui.Attribute.Make(Color.BrightCyan, Color.Black),
            Disabled = Terminal.Gui.Attribute.Make(Color.DarkGray, Color.Black)
        };

        var accentScheme = new ColorScheme()
        {
            Normal = Terminal.Gui.Attribute.Make(Color.BrightCyan, Color.Black),
            Focus = Terminal.Gui.Attribute.Make(Color.Black, Color.BrightCyan)
        };

        var mutedScheme = new ColorScheme()
        {
            Normal = Terminal.Gui.Attribute.Make(Color.DarkGray, Color.Black)
        };

        _mainWindow = new Window()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Border = new Border { BorderStyle = BorderStyle.None },
            ColorScheme = mainScheme
        };

        // --- 1. 顶部 Header: 品牌几何美学 ---
        _header = new View()
        {
            X = 0,
            Y = Pos.Center() - 10, // 略微上移，增加呼吸感
            Width = Dim.Fill(),
            Height = 7,
            ColorScheme = mainScheme
        };

        var logoLabel = new Label(@"
   ____   ____   ______  _   __  ______  ____   ____   ______
  / __ \ / __ \ / ____/ / | / / / ____/ / __ \ / __ \ / ____/
 / / / // /_/ // __/   /  |/ / / /     / / / // / / // __/   
/ /_/ // ____// /___  / /|  / / /___  / /_/ // /_/ // /___   
\____//_/    /_____/ /_/ |_/  \____/  \____/ \____/ /_____/  ") 
        { 
            X = Pos.Center(), 
            Y = 0, 
            Height = 5,
            ColorScheme = accentScheme
        };
        _header.Add(logoLabel);

        // --- 2. 核心输入容器: 悬浮、聚焦、引导性 ---
        _inputWrapper = new View()
        {
            X = Pos.Center(),
            Y = Pos.Center() - 2,
            Width = 80,
            Height = 6, // 增加高度，为标签留出空间
            ColorScheme = mainScheme
        };

        // 聚焦指示器：极简的青色垂直线
        var focusIndicator = new Label("▎") 
        { 
            X = 0, Y = 1, 
            ColorScheme = accentScheme 
        };

        var inputScheme = new ColorScheme {
            Normal = Terminal.Gui.Attribute.Make(Color.Gray, Color.Black),
            Focus = Terminal.Gui.Attribute.Make(Color.White, Color.Black)
        };

        _inputField = new TextField("Type a message...")
        {
            X = 2,
            Y = 1,
            Width = Dim.Fill() - 2,
            Height = 1,
            ColorScheme = inputScheme
        };

        _inputField.Enter += (args) => {
            if (_inputField.Text.ToString() == "Type a message...") {
                _inputField.Text = "";
                _inputField.ColorScheme = accentScheme;
                _inputField.CursorPosition = 0; // 确保光标在最前面
            }
        };

        _inputField.Leave += (args) => {
            if (string.IsNullOrWhiteSpace(_inputField.Text.ToString())) {
                _inputField.Text = "Type a message...";
                _inputField.ColorScheme = mutedScheme; // 失去焦点且为空时恢复暗色
            }
        };

        _inputField.KeyDown += (args) => {
            if (args.KeyEvent.Key == Key.Enter)
            {
                var text = _inputField.Text.ToString();
                // 如果是占位符，回车时先清空并返回（不提交）
                if (text == "Type a message...") {
                    _inputField.Text = "";
                    _inputField.ColorScheme = accentScheme;
                    args.Handled = true;
                    return;
                }

                var cmd = text?.Trim();
                // 1. 防呆：过滤掉空提交
                if (!string.IsNullOrEmpty(cmd))
                {
                    // 2. 状态保护：禁用输入以防重复提交（简单防呆）
                    _inputField.ReadOnly = true; 
                    
                    _ = Task.Run(async () => {
                        try {
                            await (OnCommandSubmitted?.Invoke(cmd) ?? Task.CompletedTask);
                        } finally {
                            Application.MainLoop.Invoke(() => {
                                _inputField.ReadOnly = false;
                                _inputField.Text = "";
                                if (!_isChatMode) EnterChatMode();
                                _inputField.SetFocus();
                            });
                        }
                    });
                }
                args.Handled = true;
            }
        };

        // 标签组：模块化、扁平化
        var sisyphusTag = new Label(" Sisyphus ") 
        { 
            X = 2, Y = 3, 
            ColorScheme = new ColorScheme { Normal = Terminal.Gui.Attribute.Make(Color.Black, Color.BrightCyan) } 
        };
        var agentTag = new Label(" Grok Code Fast 1 ") 
        { 
            X = Pos.Right(sisyphusTag) + 1, Y = 3, 
            ColorScheme = new ColorScheme { Normal = Terminal.Gui.Attribute.Make(Color.BrightCyan, Color.Black) } 
        };
        var routerTag = new Label(" OpenRouter ") 
        { 
            X = Pos.Right(agentTag) + 1, Y = 3, 
            ColorScheme = mutedScheme
        };

        _inputWrapper.Add(focusIndicator, _inputField, sisyphusTag, agentTag, routerTag);

        // --- 3. 快捷键提示: 低调、对齐 ---
        _shortcuts = new Label("ctrl+k commands  /  ctrl+l sessions")
        {
            X = Pos.Center(),
            Y = Pos.Bottom(_inputWrapper) + 1,
            ColorScheme = mutedScheme
        };

        // --- 4. Tip 区域: 引导性图标与文本 ---
        _tipIcon = new Label("✦ Tip") { 
            X = Pos.Center() - 30, 
            Y = Pos.AnchorEnd(5), 
            ColorScheme = new ColorScheme { Normal = Terminal.Gui.Attribute.Make(Color.BrightYellow, Color.Black) } 
        };
        _tipText = new Label("Use --format json for machine-readable output in scripts") { 
            X = Pos.Right(_tipIcon) + 2, 
            Y = Pos.AnchorEnd(5), 
            ColorScheme = mutedScheme 
        };

        // --- 5. 底部仪表盘: 极简状态行 ---
        _statusBar = new View()
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1,
            ColorScheme = mutedScheme
        };

        var pathLabel = new Label(" 󱂵 D:\\Work\\Opencode") { X = 2, Y = 0 };
        var mcpLabel = new Label("⊙ 3 MCP") { X = Pos.Center(), Y = 0, ColorScheme = new ColorScheme { Normal = Terminal.Gui.Attribute.Make(Color.BrightGreen, Color.Black) } };
        var versionLabel = new Label("v1.1.51 ") { X = Pos.AnchorEnd(10), Y = 0 };
        _statusBar.Add(pathLabel, mcpLabel, versionLabel);

        // --- 6. 聊天容器: 沉浸式阅读体验 ---
        _chatHeader = new View() { 
            X = 0, Y = 0, Width = Dim.Fill(), Height = 2, 
            Visible = false, 
            ColorScheme = mainScheme 
        };
        var chatAccentBar = new Label("┃") { X = 2, Y = 0, ColorScheme = accentScheme };
        _sessionTitle = new Label("# New Session") { X = 4, Y = 0, ColorScheme = mainScheme };
        _chatStats = new Label("24,067 tokens  ·  9% usage") { X = Pos.AnchorEnd(35), Y = 0, ColorScheme = mutedScheme };
        _chatHeader.Add(chatAccentBar, _sessionTitle, _chatStats);

        _chatContainer = new View()
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill() - 9, // 为底部输入框留出空间
            Visible = false,
            ColorScheme = mainScheme
        };

        _chatView = new TextView()
        {
            X = 2, Y = 0, Width = Dim.Fill() - 4, Height = Dim.Fill(),
            ReadOnly = true,
            CanFocus = false,
            ColorScheme = mainScheme
        };
        _chatContainer.Add(_chatView);

        _mainWindow.Add(_header, _inputWrapper, _shortcuts, _tipIcon, _tipText, _statusBar, _chatHeader, _chatContainer);
        top.Add(_mainWindow);

        SetupGlobalKeybindings(top);
    }

    private void SetupGlobalKeybindings(Toplevel top)
    {
        top.KeyDown += (args) =>
        {
            if (args.KeyEvent.Key == (Key.CtrlMask | Key.K))
            {
                ShowCommandPalette();
                args.Handled = true;
            }
            else if (args.KeyEvent.Key == (Key.CtrlMask | Key.L))
            {
                ShowSessionList();
                args.Handled = true;
            }
            else if (args.KeyEvent.Key == Key.Esc && _dialogService.IsDialogOpen)
            {
                _dialogService.CloseDialog();
                args.Handled = true;
            }
        };
    }

    private void ShowCommandPalette()
    {
        var options = new List<CommandOption>
        {
            new() { Title = "New Session", Action = () => _ = Task.Run(async () => await NewSessionAsync()) },
            new() { Title = "Switch Layout", Action = () => { if (_isChatMode) ExitChatMode(); else EnterChatMode(); } },
            new() { Title = "Toggle Context Compression", Action = () => ToggleContextCompression() },
            new() { Title = "Settings", Action = () => ShowSettings() },
            new() { Title = "Exit", Action = () => Application.RequestStop() }
        };

        var dialog = new CommandDialog(_dialogService, options);
        _dialogService.ShowDialog(dialog);
    }

    private void ShowSessionList()
    {
        _ = Task.Run(async () =>
        {
            var sessions = await _sessionService.ListSessionsAsync();
            Application.MainLoop.Invoke(() =>
            {
                var dialog = new FuzzySearchList<string>("Sessions", sessions, s => s);
                dialog.OnItemSelected += (s) =>
                {
                    _dialogService.CloseDialog();
                    _ = Task.Run(async () => await LoadSessionAsync(s));
                };
                dialog.OnCancelled += () => _dialogService.CloseDialog();
                _dialogService.ShowDialog(dialog);
            });
        });
    }

    private async Task NewSessionAsync()
    {
        var sessionId = await _sessionService.CreateSessionAsync("New Session");
        Application.MainLoop.Invoke(() =>
        {
            SetSessionId(sessionId);
            _chatHistory.Clear();
            UpdateChatView();
            if (!_isChatMode) EnterChatMode();
        });
    }

    private async Task LoadSessionAsync(string sessionId)
    {
        await LoadHistoryAsync(sessionId);
        Application.MainLoop.Invoke(() =>
        {
            SetSessionId(sessionId);
            if (!_isChatMode) EnterChatMode();
        });
    }

    private void ToggleContextCompression()
    {
        var config = _configService.Config;
        var currentAuto = config.Compaction?.Auto ?? true;
        var nextAuto = !currentAuto;
        
        _ = Task.Run(async () => 
        {
            await _configService.UpdateCompactionAsync(nextAuto, nextAuto);
            Application.MainLoop.Invoke(() => 
            {
                AddSystemMessage($"Context compression {(nextAuto ? "enabled" : "disabled")}.");
            });
        });
    }

    private void ShowSettings()
    {
        AddSystemMessage("Settings dialog not implemented yet.");
    }

    private void ExitChatMode()
    {
        _isChatMode = false;
        
        // 1. 恢复首页元素可见性
        if (_header != null) _header.Visible = true;
        if (_tipIcon != null) _tipIcon.Visible = true;
        if (_tipText != null) _tipText.Visible = true;
        if (_statusBar != null) _statusBar.Visible = true;

        // 2. 隐藏沟通界面组件
        if (_chatHeader != null) _chatHeader.Visible = false;
        if (_chatContainer != null) _chatContainer.Visible = false;

        // 3. 输入框重定位到屏幕中心
        if (_inputWrapper != null)
        {
            _inputWrapper.Y = Pos.Center() - 2;
            _inputWrapper.X = Pos.Center();
            _inputWrapper.Width = 80;
        }

        // 4. 快捷键提示对齐到输入框下方
        if (_shortcuts != null)
        {
            _shortcuts.X = Pos.Center();
            _shortcuts.Y = Pos.Bottom(_inputWrapper) + 1;
        }

        _mainWindow?.SetNeedsDisplay();
        _inputField?.SetFocus();
    }

    private void EnterChatMode()
    {
        _isChatMode = true;
        
        // 1. 彻底隐藏首页元素
        if (_header != null) _header.Visible = false;
        if (_tipIcon != null) _tipIcon.Visible = false;
        if (_tipText != null) _tipText.Visible = false;
        if (_statusBar != null) _statusBar.Visible = false;

        // 2. 激活沟通界面组件
        if (_chatHeader != null) _chatHeader.Visible = true;
        if (_chatContainer != null) _chatContainer.Visible = true;

        // 3. 悬浮感输入框重定位到底部
        if (_inputWrapper != null)
        {
            _inputWrapper.X = Pos.Center();
            _inputWrapper.Y = Pos.AnchorEnd(7); // 固定到底部上方
            _inputWrapper.Width = Dim.Fill() - 10; // 宽度拉伸
        }

        // 4. 沟通模式下的快捷键提示
        if (_shortcuts != null)
        {
            _shortcuts.X = Pos.AnchorEnd(40);
            _shortcuts.Y = Pos.AnchorEnd(1);
        }

        _mainWindow?.SetNeedsDisplay();
        _inputField?.SetFocus();
    }

    public void Run()
    {
        InitGui();
        Application.Run();
    }

    public void UpdateStatus(string status)
    {
        _currentStatus = status;
        // Status can be shown in the chat stats or title in the future
    }

    public void SetSessionId(string sessionId)
    {
        _currentSessionId = sessionId;
        if (_sessionTitle != null && string.IsNullOrEmpty(_sessionTitle.Text.ToString()))
        {
            _sessionTitle.Text = $"# {sessionId}";
        }
    }

    public void SetSessionTitle(string title)
    {
        if (_sessionTitle != null)
        {
            _sessionTitle.Text = $"# {title}";
            _mainWindow?.SetNeedsDisplay();
        }
    }

    private string _lastAssistantMessageId = string.Empty;

    public void AppendAssistantDelta(string delta)
    {
        if (string.IsNullOrEmpty(delta)) return;

        Application.MainLoop.Invoke(() => {
            if (_chatHistory.Count > 0 && _chatHistory[^1].StartsWith("󰚩 ASSISTANT"))
            {
                // 追加到现有消息
                _chatHistory[^1] += delta;
            }
            else
            {
                // 新建消息块
                var icon = "󰚩";
                var role = "ASSISTANT";
                var formatted = $"\n{icon} {role} · x-ai/grok-code-fast-1\n{delta}";
                _chatHistory.Add(formatted);
            }
            UpdateChatView();
        });
    }

    public void AddChatMessage(string role, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var icon = role.ToLower() switch
        {
            "user" => "",
            "assistant" => "󰚩",
            "system" => "",
            _ => "●"
        };
        
        var formatted = role.ToLower() == "assistant" 
            ? $"\n{icon} {role.ToUpper()} · x-ai/grok-code-fast-1 · 9.8s\n{message}\n"
            : $"\n{icon} {role.ToUpper()}\n{message}\n";
            
        // 简单去重：防止某些事件冒泡或并发导致的完全重复消息
        if (_chatHistory.Count > 0 && _chatHistory[^1].TrimEnd() == formatted.TrimEnd()) return;

        // 如果是完整的助手消息，且最后一条是正在流式生成的助手消息，则替换它
        if (role.ToLower() == "assistant" && _chatHistory.Count > 0 && _chatHistory[^1].Contains("ASSISTANT") && !_chatHistory[^1].Contains("9.8s"))
        {
            _chatHistory[^1] = formatted;
        }
        else
        {
            _chatHistory.Add(formatted);
        }
        
        if (!_isChatMode)
        {
            Application.MainLoop.Invoke(EnterChatMode);
        }
        
        UpdateChatView();
    }

    public void AddSystemMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Contains("Spectre.Console.Table")) return;

        // 防止重复的系统提示（如指令提示词）
        var formatted = $"\n✦ SYSTEM\n{message}\n";
        if (_chatHistory.Count > 0 && _chatHistory[^1].Trim() == formatted.Trim()) return;

        _chatHistory.Add(formatted);
        UpdateChatView();
    }

    private void UpdateChatView()
    {
        if (_chatView != null)
        {
            _chatView.Text = string.Join("\n", _chatHistory);
            
            // 确保在 UI 线程执行滚动，并使用更健壮的滚动逻辑
            Application.MainLoop.Invoke(() => {
                var text = _chatView.Text?.ToString();
                if (text != null)
                {
                    var lines = text.Split('\n').Length;
                    _chatView.ScrollTo(lines);
                    _chatView.SetNeedsDisplay();
                }
            });
        }
    }

    public async Task LoadHistoryAsync(string sessionId)
    {
        _chatHistory.Clear();
        var history = await _sessionService.LoadHistoryAsync(sessionId);
        foreach (var msg in history)
        {
            AddChatMessage(msg.Role, msg.GetText());
        }
    }

    public void AddRenderable(object renderable)
    {
        // 如果是 string 且包含特定的标记（如 Spectre.Console.Table 的 toString），进行清理或格式化
        var text = renderable.ToString() ?? "";
        if (text.Contains("Spectre.Console.Table"))
        {
            // 这是一个降级处理：在 TUI 中我们目前无法直接渲染 Spectre 的 Table 对象，
            // 只能提取其中的文本或将其转换为简单的文本列表。
            // 暂时将其显示为系统通知，避免破坏界面。
            return; 
        }
        
        AddSystemMessage(text);
    }

    public async Task RefreshDataAsync()
    {
        _sessions = await _sessionService.ListSessionsAsync();
        if (_currentSessionId != "None")
        {
            var timeline = await _sessionService.GetTimelineAsync(_currentSessionId);
            _timeline = timeline.Select(e => $"[{DateTimeOffset.FromUnixTimeMilliseconds(e.Timestamp).LocalDateTime:HH:mm:ss}] {e.Type}").ToList();
        }
        _stats = await _statsService.AggregateAsync();
        
        // Update UI components if needed
        Application.MainLoop.Invoke(() => {
            if (_chatStats != null && _stats != null)
            {
                _chatStats.Text = $"{_stats.TotalCost:F4}  9% (${_stats.TotalCost:F2}) v1.1.51";
            }
        });
    }

    public void Render()
    {
        // In Terminal.Gui, we don't manually clear and redraw the whole layout every time.
        // The framework handles it. We just trigger refreshes on components.
        Application.Refresh();
    }
}
