using Terminal.Gui;
using OpenCode.Core.Models;
using OpenCode.Core.Services;
using OpenCode.Core.Utilities;
using OpenCode.TUI.Services;
using OpenCode.TUI.UI;
using OpenCode.Core.Contracts;
using Spectre.Console;
using System.Linq;

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
    private readonly IProjectContext _projectContext;
    private readonly McpService _mcpService;
    private readonly UpgradeService _upgradeService;
    private readonly ModelDiscoveryService _discovery;
    private readonly AuthService _authService;
    
    private Window? _mainWindow;
    private View? _header;
    private View? _inputWrapper;
    private Label? _shortcuts;
    private Label? _tipIcon;
    private Label? _tipText;
    private Label? _agentTag;
    private Label? _modelTag;
    private Label? _providerTag;
    private View? _statusBar;
    private Label? _pathLabel;
    private Label? _mcpLabel;
    private Label? _versionLabel;
    private View? _chatContainer;
    private View? _chatHeader;
    private Label? _sessionTitle;
    private Label? _chatStats;
    private TextView? _inputField;
    private Label? _inputPlaceholder;
    private TextView? _chatView;
    private FrameView? _sidebar;
    private ListView? _sessionList;
    private ListView? _timelineList;

    private bool _isChatMode = false;
    private string _currentStatus = "Ready";
    private string _currentSessionId = "None";
    private string _currentModel = "unknown";
    private string _currentAgent = "default";
    private readonly List<string> _chatHistory = new();
    private readonly List<SessionMetadata> _sessions = new();
    private readonly List<string> _timeline = new();
    private readonly List<string> _promptHistory = new();
    private int _promptIndex = -1;
    private string _lastThinking = "";
    private readonly List<string> _tips = new()
    {
        "Use /share to create a public link to your conversation",
        "Use /unshare to remove a session from public access",
        "Press Ctrl+K to open the command palette",
        "Press Ctrl+L to quickly switch sessions",
        "Use /fork to branch a session from a message",
        "Use /revert to roll back to a timeline event",
        "Use /archive to hide inactive sessions",
        "Use /stats to review model usage and cost",
        "Use /help to list all available commands"
    };
    private SessionStats? _stats;

    public event Func<string, Task>? OnCommandSubmitted;

    public TuiManager(SessionService sessionService, StatsService statsService, DialogService dialogService, ConfigService configService, IProjectContext projectContext, McpService mcpService, UpgradeService upgradeService, ModelDiscoveryService discovery, AuthService authService)
    {
        _sessionService = sessionService;
        _statsService = statsService;
        _dialogService = dialogService;
        _configService = configService;
        _projectContext = projectContext;
        _mcpService = mcpService;
        _upgradeService = upgradeService;
        _discovery = discovery;
        _authService = authService;
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

        _inputField = new TextView()
        {
            X = 2,
            Y = 1,
            Width = Dim.Fill() - 2,
            Height = 3,
            ColorScheme = inputScheme
        };

        _inputPlaceholder = new Label("Type a message or / for commands")
        {
            X = 2,
            Y = 1,
            ColorScheme = mutedScheme
        };

        _inputField.Enter += (args) =>
        {
            if (_inputPlaceholder != null) _inputPlaceholder.Visible = false;
            _inputField.ColorScheme = accentScheme;
        };

        _inputField.Leave += (args) =>
        {
            if (string.IsNullOrWhiteSpace(_inputField.Text.ToString()))
            {
                if (_inputPlaceholder != null) _inputPlaceholder.Visible = true;
                _inputField.ColorScheme = mutedScheme;
            }
        };

        _inputField.KeyDown += (args) =>
        {
            var key = args.KeyEvent.Key;
            if (key == (Key.Enter | Key.ShiftMask))
            {
                _inputField.Text += "\n";
                args.Handled = true;
                return;
            }

            if (key == Key.Enter)
            {
                var text = _inputField.Text.ToString();
                var cmd = text?.Trim();
                // 1. 防呆：过滤掉空提交
                if (!string.IsNullOrEmpty(cmd))
                {
                    _promptHistory.Add(cmd);
                    _promptIndex = _promptHistory.Count;
                    // 2. 状态保护：禁用输入以防重复提交（简单防呆）
                    _inputField.ReadOnly = true; 
                    
                    _ = Task.Run(async () => {
                        try {
                            await (OnCommandSubmitted?.Invoke(cmd) ?? Task.CompletedTask);
                        } finally {
                            Application.MainLoop.Invoke(() => {
                                _inputField.ReadOnly = false;
                                _inputField.Text = "";
                                if (_inputPlaceholder != null) _inputPlaceholder.Visible = true;
                                if (!_isChatMode) EnterChatMode();
                                _inputField.SetFocus();
                            });
                        }
                    });
                }
                args.Handled = true;
            }

            if (key == (Key.CtrlMask | Key.CursorUp))
            {
                if (_promptHistory.Count == 0)
                {
                    args.Handled = true;
                    return;
                }

                _promptIndex = Math.Max(0, _promptIndex - 1);
                _inputField.Text = _promptHistory[_promptIndex];
                if (_inputPlaceholder != null) _inputPlaceholder.Visible = false;
                args.Handled = true;
            }

            if (key == (Key.CtrlMask | Key.CursorDown))
            {
                if (_promptHistory.Count == 0)
                {
                    args.Handled = true;
                    return;
                }

                _promptIndex = Math.Min(_promptHistory.Count, _promptIndex + 1);
                _inputField.Text = _promptIndex >= _promptHistory.Count ? "" : _promptHistory[_promptIndex];
                if (_inputPlaceholder != null) _inputPlaceholder.Visible = string.IsNullOrWhiteSpace(_inputField.Text.ToString());
                args.Handled = true;
            }
        };

        // 标签组：模块化、扁平化
        _agentTag = new Label($" {_currentAgent} ") 
        { 
            X = 2, Y = 3, 
            ColorScheme = new ColorScheme { Normal = Terminal.Gui.Attribute.Make(Color.Black, Color.BrightCyan) } 
        };
        _modelTag = new Label($" {_currentModel} ") 
        { 
            X = Pos.Right(_agentTag) + 1, Y = 3, 
            ColorScheme = new ColorScheme { Normal = Terminal.Gui.Attribute.Make(Color.BrightCyan, Color.Black) } 
        };
        _providerTag = new Label(" default ") 
        { 
            X = Pos.Right(_modelTag) + 1, Y = 3, 
            ColorScheme = mutedScheme
        };

        _inputWrapper.Add(focusIndicator, _inputField, _inputPlaceholder, _agentTag, _modelTag, _providerTag);

        // --- 3. 快捷键提示: 低调、对齐 ---
        _shortcuts = new Label("ctrl+k commands  /  ctrl+l sessions  /  ctrl+n new  /  ctrl+m models  /  ctrl+p providers")
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
        _tipText = new Label(_tips[0]) { 
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

        _pathLabel = new Label($" 󱂵 {_projectContext.Directory}") { X = 2, Y = 0 };
        _mcpLabel = new Label("⊙ 0 MCP") { X = Pos.Center(), Y = 0, ColorScheme = new ColorScheme { Normal = Terminal.Gui.Attribute.Make(Color.BrightGreen, Color.Black) } };
        _versionLabel = new Label($"v{_upgradeService.GetCurrentVersion()} ") { X = Pos.AnchorEnd(12), Y = 0 };
        _statusBar.Add(_pathLabel, _mcpLabel, _versionLabel);

        // --- 6. 聊天容器: 沉浸式阅读体验 ---
        _chatHeader = new View() { 
            X = Pos.Right(_sidebar), Y = 0, Width = Dim.Fill() - 32, Height = 2, 
            Visible = false, 
            ColorScheme = mainScheme 
        };
        var chatAccentBar = new Label("┃") { X = 2, Y = 0, ColorScheme = accentScheme };
        _sessionTitle = new Label("# New Session") { X = 4, Y = 0, ColorScheme = mainScheme };
        _chatStats = new Label("24,067 tokens  ·  9% usage") { X = Pos.AnchorEnd(35), Y = 0, ColorScheme = mutedScheme };
        _chatHeader.Add(chatAccentBar, _sessionTitle, _chatStats);

        _sidebar = new FrameView("Sessions")
        {
            X = 0,
            Y = 2,
            Width = 32,
            Height = Dim.Fill() - 9,
            Visible = false,
            ColorScheme = mainScheme
        };

        _sessionList = new ListView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Percent(60),
            AllowsMarking = false,
            ColorScheme = mainScheme
        };
        _sessionList.OpenSelectedItem += (args) =>
        {
            if (args.Item >= 0 && args.Item < _sessions.Count)
            {
                var selected = _sessions[args.Item];
                _ = Task.Run(async () => await LoadSessionAsync(selected.Id));
            }
        };

        var timelineFrame = new FrameView("Timeline")
        {
            X = 0,
            Y = Pos.Bottom(_sessionList),
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        _timelineList = new ListView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            AllowsMarking = false,
            ColorScheme = mutedScheme
        };
        timelineFrame.Add(_timelineList);
        _sidebar.Add(_sessionList, timelineFrame);

        _chatContainer = new View()
        {
            X = Pos.Right(_sidebar),
            Y = 2,
            Width = Dim.Fill() - 32,
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

        _mainWindow.Add(_header, _inputWrapper, _shortcuts, _tipIcon, _tipText, _statusBar, _chatHeader, _sidebar, _chatContainer);
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
            else if (args.KeyEvent.Key == (Key.CtrlMask | Key.N))
            {
                _ = Task.Run(async () => await NewSessionAsync());
                args.Handled = true;
            }
            else if (args.KeyEvent.Key == (Key.CtrlMask | Key.M))
            {
                ShowModelList();
                args.Handled = true;
            }
            else if (args.KeyEvent.Key == (Key.CtrlMask | Key.P))
            {
                ShowProviderList();
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
            new() { Title = "Sessions", Action = () => ShowSessionList() },
            new() { Title = "Timeline", Action = () => ShowTimeline() },
            new() { Title = "Models", Action = () => ShowModelList() },
            new() { Title = "Providers", Action = () => ShowProviderList() },
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
            var sessions = await _sessionService.ListSessionMetadataAsync();
            Application.MainLoop.Invoke(() =>
            {
                var dialog = new FuzzySearchList<SessionMetadata>("Sessions", sessions, s => $"{s.Title ?? "Untitled"} · {s.Id}");
                dialog.OnItemSelected += (s) =>
                {
                    _dialogService.CloseDialog();
                    _ = Task.Run(async () => await LoadSessionAsync(s.Id));
                };
                dialog.OnCancelled += () => _dialogService.CloseDialog();
                _dialogService.ShowDialog(dialog);
            });
        });
    }

    private void ShowTimeline()
    {
        _ = Task.Run(async () =>
        {
            var timeline = await _sessionService.GetTimelineAsync(_currentSessionId);
            var rows = timeline.Select(entry => $"{entry.Type} · {entry.Id}").ToList();
            Application.MainLoop.Invoke(() =>
            {
                var dialog = new FuzzySearchList<string>("Timeline", rows, s => s);
                dialog.OnItemSelected += _ => _dialogService.CloseDialog();
                dialog.OnCancelled += () => _dialogService.CloseDialog();
                _dialogService.ShowDialog(dialog);
            });
        });
    }

    private void ShowModelList()
    {
        _ = Task.Run(async () =>
        {
            var providers = await _discovery.GetModelsAsync();
            var models = providers.Values
                .SelectMany(p => p.Models.Values.Select(m => $"{p.Id}/{m.Id} · {m.Name}"))
                .ToList();

            if (models.Count == 0)
            {
                AddSystemMessage("No models discovered. Run /auth login to add a provider.");
                return;
            }

            Application.MainLoop.Invoke(() =>
            {
                var dialog = new FuzzySearchList<string>("Models", models, s => s);
                dialog.OnItemSelected += (s) =>
                {
                    _dialogService.CloseDialog();
                    var model = s.Split('·')[0].Trim();
                    _ = Task.Run(async () =>
                    {
                        _configService.Config.Model = model;
                        await _configService.SaveAsync();
                        AddSystemMessage($"Model switched to {model}");
                        await RefreshDataAsync();
                    });
                };
                dialog.OnCancelled += () => _dialogService.CloseDialog();
                _dialogService.ShowDialog(dialog);
            });
        });
    }

    private void ShowProviderList()
    {
        _ = Task.Run(async () =>
        {
            var auths = await _authService.AllAsync();
            var providers = (await _discovery.GetModelsAsync()).Values.Select(p => p.Id).ToHashSet();
            foreach (var key in auths.Keys) providers.Add(key);

            var options = providers
                .OrderBy(x => x)
                .Select(p => $"{p} · {(auths.ContainsKey(p) ? "connected" : "disconnected")}")
                .ToList();

            if (options.Count == 0)
            {
                AddSystemMessage("No providers available. Use /auth login to add one.");
                return;
            }

            Application.MainLoop.Invoke(() =>
            {
                var dialog = new FuzzySearchList<string>("Providers", options, s => s);
                dialog.OnItemSelected += (s) =>
                {
                    _dialogService.CloseDialog();
                    var provider = s.Split('·')[0].Trim();
                    _ = Task.Run(async () =>
                    {
                        AddSystemMessage($"Starting auth flow for {provider}...");
                        if (OnCommandSubmitted != null) await OnCommandSubmitted($"auth login {provider}");
                    });
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
            SetSessionTitle("New Session");
            _chatHistory.Clear();
            UpdateChatView();
            if (!_isChatMode) EnterChatMode();
        });
    }

    private async Task LoadSessionAsync(string sessionId)
    {
        await LoadHistoryAsync(sessionId);
        var meta = await _sessionService.GetMetadataAsync(sessionId);
        Application.MainLoop.Invoke(() =>
        {
            SetSessionId(sessionId);
            if (meta?.Title != null) SetSessionTitle(meta.Title);
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
        if (_sidebar != null) _sidebar.Visible = false;
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
        if (_sidebar != null) _sidebar.Visible = true;
        if (_chatContainer != null) _chatContainer.Visible = true;

        // 3. 悬浮感输入框重定位到底部
        if (_inputWrapper != null)
        {
            _inputWrapper.X = Pos.Center();
            _inputWrapper.Y = Pos.AnchorEnd(7); // 固定到底部上方
            _inputWrapper.Width = Dim.Fill() - 6; // 宽度拉伸
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
        if (_chatStats != null)
        {
            _chatStats.Text = $"{_currentStatus}";
            _chatStats.SetNeedsDisplay();
        }
    }

    public void SetSessionId(string sessionId)
    {
        _currentSessionId = sessionId;
        if (_sessionTitle != null)
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
                var formatted = $"\n{icon} {role} · {_currentModel}\n{delta}";
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
            ? $"\n{icon} {role.ToUpper()} · {_currentModel}\n{message}\n"
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

    public void AddThinkingMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        if (message == _lastThinking) return;
        _lastThinking = message;
        AddSystemMessage($"Thinking: {message}");
    }

    public void AddToolMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        AddSystemMessage($"Tool: {message}");
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
        if (renderable is Table table)
        {
            AddSystemMessage(RenderTable(table));
            return;
        }

        AddSystemMessage(renderable.ToString() ?? "");
    }

    public async Task RefreshDataAsync()
    {
        _sessions.Clear();
        _sessions.AddRange(await _sessionService.ListSessionMetadataAsync());
        if (_currentSessionId != "None")
        {
            var timeline = await _sessionService.GetTimelineAsync(_currentSessionId);
            _timeline.Clear();
            _timeline.AddRange(timeline.Select(e => $"[{FormatTimestamp(e.Timestamp)}] {e.Type}"));
        }
        _stats = await _statsService.AggregateAsync();

        var config = _configService.Config;
        _currentModel = config.Model ?? Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "unknown";
        _currentAgent = config.DefaultAgent ?? "default";
        
        // Update UI components if needed
        Application.MainLoop.Invoke(() => {
            if (_chatStats != null)
            {
                var sessionStats = _sessionService.GetStatsAsync(_currentSessionId).GetAwaiter().GetResult();
                if (sessionStats != null)
                {
                    var tokens = sessionStats.TotalTokens.Input + sessionStats.TotalTokens.Output;
                    _chatStats.Text = $"{_currentStatus} · {tokens} tokens · ${sessionStats.TotalCost:F4}";
                }
                else
                {
                    _chatStats.Text = $"{_currentStatus} · 0 tokens · $0.0000";
                }
            }

            var current = _sessions.FirstOrDefault(s => s.Id == _currentSessionId);
            if (current != null && !string.IsNullOrEmpty(current.Title))
            {
                SetSessionTitle(current.Title);
            }

            if (_sessionList != null)
            {
                var labels = _sessions
                    .Select(s => $"{s.Title ?? "Untitled"} · {DateTimeOffset.FromUnixTimeSeconds(s.UpdatedAt).LocalDateTime:MM-dd HH:mm}")
                    .ToList();
                _sessionList.SetSource(labels);
                var index = _sessions.FindIndex(s => s.Id == _currentSessionId);
                if (index >= 0) _sessionList.SelectedItem = index;
            }

            _timelineList?.SetSource(_timeline);

            if (_mcpLabel != null)
            {
                var count = _mcpService.GetServers().Count;
                _mcpLabel.Text = $"⊙ {count} MCP";
            }

            if (_agentTag != null) _agentTag.Text = $" {_currentAgent} ";
            if (_modelTag != null) _modelTag.Text = $" {_currentModel} ";
            if (_providerTag != null)
            {
                var parts = _currentModel.Split('/', StringSplitOptions.RemoveEmptyEntries);
                _providerTag.Text = parts.Length > 1 ? $" {parts[0]} " : " default ";
            }

            if (_tipText != null)
            {
                var tip = _tips.Count > 0 ? _tips[DateTimeOffset.UtcNow.Second % _tips.Count] : "";
                _tipText.Text = tip;
            }
        });
    }

    private string RenderTable(Table table)
    {
        var headers = table.Columns.Select(c => c.Header.ToString()).ToList();
        var lines = new List<string> { string.Join(" | ", headers) };
        foreach (var row in table.Rows)
        {
            var cells = row.Cells.Select(cell => cell.ToString()).ToList();
            lines.Add(string.Join(" | ", cells));
        }
        return string.Join("\n", lines);
    }

    private static string FormatTimestamp(long timestamp)
    {
        const long threshold = 1_000_000_000_000;
        var value = timestamp > threshold ? timestamp : timestamp * 1000;
        return DateTimeOffset.FromUnixTimeMilliseconds(value).LocalDateTime.ToString("HH:mm:ss");
    }

    public void Render()
    {
        // In Terminal.Gui, we don't manually clear and redraw the whole layout every time.
        // The framework handles it. We just trigger refreshes on components.
        Application.Refresh();
    }
}
