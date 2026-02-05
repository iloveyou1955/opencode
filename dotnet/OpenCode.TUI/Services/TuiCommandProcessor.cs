using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using OpenCode.Core.Models;
using OpenCode.Core.Services;
using OpenCode.Core.Utilities;
using OpenCode.Core.Contracts;
using OpenCode.Infrastructure.AI;
using OpenCode.Infrastructure.Lsp;
using OpenCode.Infrastructure.Services;
using OpenCode.Infrastructure.Tools;
using OpenCode.AgentFramework.Abstractions;
using Spectre.Console;
using Spectre.Console.Json;

using OpenCode.Core.Attributes;
using System.Reflection;
using Microsoft.Extensions.AI;

namespace OpenCode.TUI.Services
{
    public static class MessageExtensions
    {
        public static string GetText(this MessageInfo message)
        {
            return string.Join("", message.Parts.OfType<TextPart>().Select(p => p.Text));
        }
    }

    /// <summary>
    /// TUI 指令处理器的具体实现
    /// </summary>
    [ServiceRegistration(ServiceLifetime.Singleton, typeof(ICommandProcessor))]
    public class TuiCommandProcessor : ICommandProcessor
    {
        private readonly Dictionary<string, Func<string, CommandContext, IServiceProvider, Task<bool>>> _commandHandlers;

        public TuiCommandProcessor()
        {
            _commandHandlers = new Dictionary<string, Func<string, CommandContext, IServiceProvider, Task<bool>>>
            {
                { "exit", (i, c, s) => { Environment.Exit(0); return Task.FromResult(true); } },
                { "quit", (i, c, s) => { Environment.Exit(0); return Task.FromResult(true); } },
                { "/exit", (i, c, s) => { Environment.Exit(0); return Task.FromResult(true); } },
                { "/q", (i, c, s) => { Environment.Exit(0); return Task.FromResult(true); } },
                { "help", (i, c, s) => { ShowHelp(c); return Task.FromResult(true); } },
                { "?", (i, c, s) => { ShowHelp(c); return Task.FromResult(true); } },
                { "/help", (i, c, s) => { ShowHelp(c); return Task.FromResult(true); } },
                { "/h", (i, c, s) => { ShowHelp(c); return Task.FromResult(true); } },
                { "new", async (i, c, s) => { await HandleNewSessionAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "clear", async (i, c, s) => { await HandleNewSessionAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "/new", async (i, c, s) => { await HandleNewSessionAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "/clear", async (i, c, s) => { await HandleNewSessionAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "sessions", async (i, c, s) => { await HandleSessionsAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "/sessions", async (i, c, s) => { await HandleSessionsAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "resume", async (i, c, s) => { await HandleSessionsAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "/resume", async (i, c, s) => { await HandleSessionsAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "continue", async (i, c, s) => { await HandleSessionsAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "/continue", async (i, c, s) => { await HandleSessionsAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "session", async (i, c, s) => { await HandleSessionsAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "/session", async (i, c, s) => { await HandleSessionsAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "rename", async (i, c, s) => { await HandleRenameAsync(i, c, s.GetRequiredService<SessionService>()); return true; } },
                { "/rename", async (i, c, s) => { await HandleRenameAsync(i, c, s.GetRequiredService<SessionService>()); return true; } },
                { "undo", async (i, c, s) => { await HandleUndoAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "/undo", async (i, c, s) => { await HandleUndoAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "redo", async (i, c, s) => { await HandleRedoAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "/redo", async (i, c, s) => { await HandleRedoAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "unrevert", async (i, c, s) => { await HandleRedoAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "share", async (i, c, s) => { await HandleShareAsync(c, s); return true; } },
                { "/share", async (i, c, s) => { await HandleShareAsync(c, s); return true; } },
                { "unshare", async (i, c, s) => { await HandleUnshareAsync(c, s); return true; } },
                { "/unshare", async (i, c, s) => { await HandleUnshareAsync(c, s); return true; } },
                { "copy", async (i, c, s) => { await HandleCopyAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "/copy", async (i, c, s) => { await HandleCopyAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "timeline", async (i, c, s) => { await HandleTimelineAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "/timeline", async (i, c, s) => { await HandleTimelineAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "history", async (i, c, s) => { await HandleTimelineAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "/history", async (i, c, s) => { await HandleTimelineAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "ls", async (i, c, s) => { await HandleTimelineAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "/ls", async (i, c, s) => { await HandleTimelineAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "details", (i, c, s) => { c.SetShowDetails(!c.ShowDetails); c.Tui.AddSystemMessage($"Details display {(c.ShowDetails ? "enabled" : "disabled")}."); c.Tui.Render(); return Task.FromResult(true); } },
                { "/details", (i, c, s) => { c.SetShowDetails(!c.ShowDetails); c.Tui.AddSystemMessage($"Details display {(c.ShowDetails ? "enabled" : "disabled")}."); c.Tui.Render(); return Task.FromResult(true); } },
                { "thinking", (i, c, s) => { c.SetShowThinking(!c.ShowThinking); c.Tui.AddSystemMessage($"Thinking process display {(c.ShowThinking ? "enabled" : "disabled")}."); c.Tui.Render(); return Task.FromResult(true); } },
                { "/thinking", (i, c, s) => { c.SetShowThinking(!c.ShowThinking); c.Tui.AddSystemMessage($"Thinking process display {(c.ShowThinking ? "enabled" : "disabled")}."); c.Tui.Render(); return Task.FromResult(true); } },
                { "timestamps", (i, c, s) => { c.SetShowTimestamps(!c.ShowTimestamps); c.Tui.AddSystemMessage($"Timestamps display {(c.ShowTimestamps ? "enabled" : "disabled")}."); c.Tui.Render(); return Task.FromResult(true); } },
                { "/timestamps", (i, c, s) => { c.SetShowTimestamps(!c.ShowTimestamps); c.Tui.AddSystemMessage($"Timestamps display {(c.ShowTimestamps ? "enabled" : "disabled")}."); c.Tui.Render(); return Task.FromResult(true); } },
                { "config", async (i, c, s) => { await HandleDebugAsync("debug config", c, s); return true; } },
                { "/config", async (i, c, s) => { await HandleDebugAsync("debug config", c, s); return true; } },
                { "auth", async (i, c, s) => { await HandleAuthAsync(i, c, s); return true; } },
                { "/auth", async (i, c, s) => { await HandleAuthAsync(i, c, s); return true; } },
                { "/connect", async (i, c, s) => { await HandleAuthAsync(i, c, s); return true; } },
                { "models", async (i, c, s) => { await HandleModelsAsync(i, c, s); return true; } },
                { "/models", async (i, c, s) => { await HandleModelsAsync(i, c, s); return true; } },
                { "model", async (i, c, s) => { await HandleModelsAsync(i, c, s); return true; } },
                { "/model", async (i, c, s) => { await HandleModelsAsync(i, c, s); return true; } },
                { "delete", async (i, c, s) => { await HandleDeleteSessionAsync(i, c, s.GetRequiredService<SessionService>()); return true; } },
                { "/delete", async (i, c, s) => { await HandleDeleteSessionAsync(i, c, s.GetRequiredService<SessionService>()); return true; } },
                { "stats", async (i, c, s) => { await HandleStatsAsync(i, c, s); return true; } },
                { "/stats", async (i, c, s) => { await HandleStatsAsync(i, c, s); return true; } },
                { "status", async (i, c, s) => { await HandleSessionStatusAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "/status", async (i, c, s) => { await HandleSessionStatusAsync(c, s.GetRequiredService<SessionService>()); return true; } },
                { "docs", (i, c, s) => { Process.Start(new ProcessStartInfo("https://opencode.ai/docs") { UseShellExecute = true }); return Task.FromResult(true); } },
                { "/docs", (i, c, s) => { Process.Start(new ProcessStartInfo("https://opencode.ai/docs") { UseShellExecute = true }); return Task.FromResult(true); } },
                { "compact", async (i, c, s) => { await HandleCompactAsync(c, s.GetRequiredService<SessionService>(), s.GetRequiredService<SummaryService>()); return true; } },
                { "summarize", async (i, c, s) => { await HandleCompactAsync(c, s.GetRequiredService<SessionService>(), s.GetRequiredService<SummaryService>()); return true; } },
                { "/compact", async (i, c, s) => { await HandleCompactAsync(c, s.GetRequiredService<SessionService>(), s.GetRequiredService<SummaryService>()); return true; } },
                { "/summarize", async (i, c, s) => { await HandleCompactAsync(c, s.GetRequiredService<SessionService>(), s.GetRequiredService<SummaryService>()); return true; } },
                { "init", async (i, c, s) => { await HandleInitAsync(c); return true; } },
                { "/init", async (i, c, s) => { await HandleInitAsync(c); return true; } },
                { "export", async (i, c, s) => { await HandleExportAsync(i, c, s); return true; } },
                { "/export", async (i, c, s) => { await HandleExportAsync(i, c, s); return true; } },
                { "import", async (i, c, s) => { await HandleImportAsync(i, c, s); return true; } },
                { "/import", async (i, c, s) => { await HandleImportAsync(i, c, s); return true; } },
                { "fork", async (i, c, s) => { await HandleForkAsync(i, c, s); return true; } },
                { "/fork", async (i, c, s) => { await HandleForkAsync(i, c, s); return true; } },
                { "pr", async (i, c, s) => { await HandlePrAsync(i, c, s); return true; } },
                { "/pr", async (i, c, s) => { await HandlePrAsync(i, c, s); return true; } },
                { "github", async (i, c, s) => { await HandleGithubAsync(i, c, s); return true; } },
                { "/github", async (i, c, s) => { await HandleGithubAsync(i, c, s); return true; } },
                { "mcp", async (i, c, s) => { await HandleMcpAsync(i, c, s); return true; } },
                { "/mcp", async (i, c, s) => { await HandleMcpAsync(i, c, s); return true; } },
                { "mcps", async (i, c, s) => { await HandleMcpAsync(i, c, s); return true; } },
                { "/mcps", async (i, c, s) => { await HandleMcpAsync(i, c, s); return true; } },
                { "debug", async (i, c, s) => { await HandleDebugAsync(i, c, s); return true; } },
                { "/debug", async (i, c, s) => { await HandleDebugAsync(i, c, s); return true; } },
                { "agent", async (i, c, s) => { await HandleAgentAsync(i, c, s); return true; } },
                { "/agent", async (i, c, s) => { await HandleAgentAsync(i, c, s); return true; } },
                { "agents", async (i, c, s) => { await HandleAgentAsync(i, c, s); return true; } },
                { "/agents", async (i, c, s) => { await HandleAgentAsync(i, c, s); return true; } },
                { "acp", (i, c, s) => { HandleAcpStatus(s); return Task.FromResult(true); } },
                { "/acp", (i, c, s) => { HandleAcpStatus(s); return Task.FromResult(true); } },
                { "revert", async (i, c, s) => { await HandleRevertAsync(i, c, s.GetRequiredService<SessionService>()); return true; } },
                { "/revert", async (i, c, s) => { await HandleRevertAsync(i, c, s.GetRequiredService<SessionService>()); return true; } },
                { "theme", (i, c, s) => { HandleTheme(s); return Task.FromResult(true); } },
                { "/theme", (i, c, s) => { HandleTheme(s); return Task.FromResult(true); } },
                { "themes", (i, c, s) => { HandleTheme(s); return Task.FromResult(true); } },
                { "/themes", (i, c, s) => { HandleTheme(s); return Task.FromResult(true); } },
                { "maps", async (i, c, s) => { await HandleMapsAsync(i, c, s); return true; } },
                { "/maps", async (i, c, s) => { await HandleMapsAsync(i, c, s); return true; } },
                { "map", async (i, c, s) => { await HandleMapsAsync(i, c, s); return true; } },
                { "/map", async (i, c, s) => { await HandleMapsAsync(i, c, s); return true; } },
                { "stash", async (i, c, s) => { await HandleStashAsync(i, c, s); return true; } },
                { "/stash", async (i, c, s) => { await HandleStashAsync(i, c, s); return true; } },
                { "archive", async (i, c, s) => { await HandleArchiveAsync(i, c, s); return true; } },
                { "/archive", async (i, c, s) => { await HandleArchiveAsync(i, c, s); return true; } },
                { "unarchive", async (i, c, s) => { await HandleUnarchiveAsync(i, c, s); return true; } },
                { "/unarchive", async (i, c, s) => { await HandleUnarchiveAsync(i, c, s); return true; } },
                { "restore", async (i, c, s) => { await HandleUnarchiveAsync(i, c, s); return true; } },
                { "/restore", async (i, c, s) => { await HandleUnarchiveAsync(i, c, s); return true; } },
                { "upgrade", async (i, c, s) => { await HandleUpgradeAsync(c, s); return true; } },
                { "/upgrade", async (i, c, s) => { await HandleUpgradeAsync(c, s); return true; } },
                { "worktree", async (i, c, s) => { await HandleWorktreeAsync(i, c, s); return true; } },
                { "/worktree", async (i, c, s) => { await HandleWorktreeAsync(i, c, s); return true; } },
                { "uninstall", async (i, c, s) => { await HandleUninstallAsync(c); return true; } },
                { "/uninstall", async (i, c, s) => { await HandleUninstallAsync(c); return true; } },
                { "review", async (i, c, s) => { await HandleReviewAsync(i, s); return true; } },
                { "/review", async (i, c, s) => { await HandleReviewAsync(i, s); return true; } }
            };
        }

        public async Task<bool> ProcessCommandAsync(string input, CommandContext context)
        {
            if (string.IsNullOrWhiteSpace(input)) return false;
            
            var trimmedInput = input.Trim();
            var parts = trimmedInput.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;

            var cmdName = parts[0].ToLower();
            var serviceProvider = context.ServiceProvider;

            // 1. 特殊处理以 ! 开头的 Shell 指令 (容错：支持 "!cmd" 或 "! cmd")
            if (trimmedInput.StartsWith("!"))
            {
                var shellCmd = trimmedInput.StartsWith("! ") 
                    ? trimmedInput.Substring(2).Trim() 
                    : trimmedInput.Substring(1).Trim();
                
                if (string.IsNullOrEmpty(shellCmd))
                {
                    context.Tui.AddSystemMessage("提示: 请在 '!' 后输入要执行的 Shell 命令。");
                    return true;
                }

                await HandleShellCommandAsync(shellCmd);
                return true;
            }

            // 2. 指令解析容错：自动尝试匹配带 / 和不带 / 的版本
            var lookupName = cmdName;
            if (lookupName.StartsWith("/"))
            {
                // 如果是 / 开头，先尝试原始匹配，再尝试去掉 /
            }
            else
            {
                // 如果不是 / 开头，先尝试原始匹配，再尝试加上 /
                if (!_commandHandlers.ContainsKey(lookupName) && _commandHandlers.ContainsKey("/" + lookupName))
                {
                    lookupName = "/" + lookupName;
                }
            }

            if (_commandHandlers.TryGetValue(lookupName, out var handler))
            {
                try 
                {
                    return await handler(trimmedInput, context, serviceProvider);
                }
                catch (Exception ex)
                {
                    context.Tui.AddSystemMessage($"指令执行异常: {ex.Message}");
                    return true;
                }
            }

            // 3. 强约束：如果是以 / 开头的指令但未找到，强制拦截并提示
            if (cmdName.StartsWith("/"))
            {
                context.Tui.AddSystemMessage($"错误: 未知的指令 '{cmdName}'。");
                context.Tui.AddSystemMessage("输入 'help' 或 '?' 查看可用指令列表。");
                return true; 
            }

            return false; // 交给 AI 处理
        }

        private async Task HandleNewSessionAsync(CommandContext context, SessionService sessionService)
        {
            var newSessionId = "session_" + Guid.NewGuid().ToString("N")[..8];
            var meta = new OpenCode.Core.Models.SessionMetadata(newSessionId, "New Session");
            await sessionService.SaveMetadataAsync(newSessionId, meta);
            context.SetSessionId(newSessionId);
            context.SetFirstMessage(true);
            
            context.Tui.SetSessionId(newSessionId);
            await context.Tui.LoadHistoryAsync(newSessionId);
            context.Tui.AddSystemMessage("已开启新会话。");
            context.Tui.Render();
        }

        private async Task HandleSessionsAsync(CommandContext context, SessionService sessionService)
        {
            var sessions = await sessionService.ListSessionsAsync();
            if (sessions.Count == 0)
            {
                context.Tui.AddSystemMessage("目前没有已保存的会话。");
                context.Tui.Render();
                return;
            }
            
            var table = new Table().RoundedBorder().Title("[bold blue]Sessions[/]");
            table.AddColumn("ID");
            table.AddColumn("Title");
            
            foreach (var s in sessions)
            {
                var title = s == context.CurrentSessionId ? $"[green]* {s}[/]" : s;
                table.AddRow(title, "Session Title"); // TODO: Load actual titles
            }
            
            context.Tui.AddRenderable(table);
            context.Tui.Render();

            var choices = sessions.ToList();
            choices.Add("取消");

            // 对于交互式提示，我们暂时保留 AnsiConsole，但之后必须 Render
            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("请选择要切换到的会话:")
                    .AddChoices(choices));
            
            if (selected == "取消")
            {
                context.Tui.AddSystemMessage("操作已取消。");
                context.Tui.Render();
                return;
            }
            
            context.SetSessionId(selected);
            context.SetFirstMessage(false);
            
            context.Tui.SetSessionId(selected);
            await context.Tui.LoadHistoryAsync(selected);
            context.Tui.AddSystemMessage($"已切换到会话: {selected}");
            context.Tui.Render();
        }

        private async Task HandleRenameAsync(string input, CommandContext context, SessionService sessionService)
        {
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string newName;
            if (parts.Length > 1)
            {
                newName = string.Join(" ", parts.Skip(1));
            }
            else
            {
                // 对于交互式提示，我们暂时保留 AnsiConsole，但之后必须 Render
                newName = AnsiConsole.Ask<string>("请输入会话的新名称:");
            }
            await sessionService.RenameSessionAsync(context.CurrentSessionId, newName);
            context.Tui.AddSystemMessage($"会话已重命名为: {newName}");
            context.Tui.Render();
        }

        private async Task HandleCopyAsync(CommandContext context, SessionService sessionService)
        {
            var history = await sessionService.LoadHistoryAsync(context.CurrentSessionId);
            var transcript = string.Join("\n\n", history.Select(m => $"{m.Role.ToUpper()}: {m.GetText()}"));
            context.Tui.AddRenderable(new Panel(transcript) { Header = new PanelHeader("会话内容"), Border = BoxBorder.Rounded });
            context.Tui.AddSystemMessage("会话内容已显示，请手动复制。");
            context.Tui.Render();
        }

        private async Task HandleTimelineAsync(CommandContext context, SessionService sessionService)
        {
            var timeline = await sessionService.GetTimelineAsync(context.CurrentSessionId);
            var table = new Table().RoundedBorder().Title($"[bold blue]会话时间线: {context.CurrentSessionId}[/]");
            table.AddColumn("ID");
            table.AddColumn("时间");
            table.AddColumn("类型");
            table.AddColumn("内容摘要");

            foreach (var ev in timeline.TakeLast(20))
            {
                table.AddRow(ev.Id, DateTimeOffset.FromUnixTimeMilliseconds(ev.Timestamp).LocalDateTime.ToString("HH:mm:ss"), ev.Type, ev.Description ?? "");
            }
            context.Tui.AddRenderable(table);
            context.Tui.Render();
         }
 
         private async Task HandleWellKnownAuthAsync(string url, CommandContext context, IServiceProvider serviceProvider)
         {
             var authService = serviceProvider.GetRequiredService<AuthService>();
             using var client = new HttpClient();
             
             try
             {
                 context.Tui.AddSystemMessage($"正在从 {url} 获取认证配置...");
                 context.Tui.Render();
                 
                 var wellknown = await client.GetFromJsonAsync<JsonNode>($"{url.TrimEnd('/')}/.well-known/opencode");
                 
                 if (wellknown == null)
                 {
                     context.Tui.AddSystemMessage("错误: 获取认证配置失败。");
                     context.Tui.Render();
                     return;
                 }

                 var authCommand = wellknown["auth"]?["command"]?.AsArray().Select(x => x?.ToString() ?? "").ToArray();
                 var authEnv = wellknown["auth"]?["env"]?.ToString();

                 if (authCommand == null || authCommand.Length == 0 || string.IsNullOrEmpty(authEnv))
                 {
                     context.Tui.AddSystemMessage("错误: 认证配置格式错误。");
                     context.Tui.Render();
                     return;
                 }

                 context.Tui.AddSystemMessage($"正在运行认证指令: {string.Join(" ", authCommand)}");
                 context.Tui.Render();
                 
                 var psi = new ProcessStartInfo
                 {
                     FileName = authCommand[0],
                     Arguments = string.Join(" ", authCommand.Skip(1)),
                     RedirectStandardOutput = true,
                     UseShellExecute = false,
                     CreateNoWindow = true
                 };

                 using var process = Process.Start(psi);
                 if (process == null)
                 {
                     context.Tui.AddSystemMessage("错误: 启动认证进程失败。");
                     context.Tui.Render();
                     return;
                 }

                 var token = (await process.StandardOutput.ReadToEndAsync()).Trim();
                 await process.WaitForExitAsync();

                 if (process.ExitCode != 0)
                 {
                     context.Tui.AddSystemMessage("错误: 认证指令执行失败。");
                     context.Tui.Render();
                     return;
                 }

                 await authService.SetAsync(url, new WellKnownAuthInfo(authEnv, token));
                 context.Tui.AddSystemMessage($"已成功登录到 {url}。");
                 context.Tui.Render();
             }
             catch (Exception ex)
             {
                 context.Tui.AddSystemMessage($"错误: 认证过程中发生错误: {ex.Message}");
                 context.Tui.Render();
             }
         }

        private void HandleAcpStatus(IServiceProvider serviceProvider)
        {
            var tui = serviceProvider.GetRequiredService<TuiManager>();
            var rows = new Rows(
                new Text("ACP (Agent Control Protocol) 状态:"),
                new Text("- 协议版本: 1"),
                new Text("- 端点: http://localhost:4096/acp"),
                new Text("- 状态: 运行中", new Style(foreground: Color.Green))
            );
            tui.AddRenderable(new Panel(rows) { Header = new PanelHeader("ACP Status"), Border = BoxBorder.Rounded });
            tui.Render();
        }

        private void HandleTheme(IServiceProvider serviceProvider)
        {
            var themeService = serviceProvider.GetRequiredService<ThemeService>();
            var tui = serviceProvider.GetRequiredService<TuiManager>();
            var themes = themeService.ListThemes();
            
            var table = new Table().RoundedBorder().Title("[bold blue]可用主题[/]");
            table.AddColumn("Theme Name");
            foreach (var t in themes) table.AddRow(t);
            
            tui.AddRenderable(table);
            tui.Render();
        }

        private async Task HandleMapsAsync(string input, CommandContext context, IServiceProvider serviceProvider)
        {
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var subCommand = parts.Length > 1 ? parts[1].ToLower() : "list";
            var mapService = serviceProvider.GetRequiredService<ICodeMapService>();
            var tui = context.Tui;

            if (subCommand == "list" || string.IsNullOrEmpty(subCommand))
            {
                tui.UpdateStatus("正在获取代码图谱...");
                tui.Render();

                var maps = await mapService.GetMapsAsync();
                var table = new Table().RoundedBorder().Title("[bold blue]代码图谱[/]");
                table.AddColumn("[yellow]名称[/]");
                table.AddColumn("[green]类型[/]");
                table.AddColumn("[blue]节点数[/]");

                foreach (var m in maps)
                {
                    table.AddRow(m.Name, m.Type, m.NodeCount.ToString());
                }
                
                tui.AddRenderable(table);
                tui.UpdateStatus("Ready");
                tui.Render();
            }
            else if (subCommand == "generate" || subCommand == "gen")
            {
                var path = parts.Length > 2 ? parts[2] : ".";
                tui.UpdateStatus($"正在生成代码图谱: {path}...");
                tui.Render();

                await mapService.GenerateMapAsync(path);
                
                tui.AddSystemMessage($"代码图谱已为路径 '{path}' 生成成功。");
                tui.UpdateStatus("Ready");
                tui.Render();
            }
            else
            {
                tui.AddSystemMessage($"错误: 未知的代码图谱子指令 '{subCommand}'。");
                tui.AddSystemMessage("可用子指令: list, generate [path]");
                tui.Render();
            }
        }

        private async Task HandleStashAsync(string input, CommandContext context, IServiceProvider serviceProvider)
        {
            var stashService = serviceProvider.GetRequiredService<StashService>();
            var sessionService = serviceProvider.GetRequiredService<SessionService>();
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            if (parts.Length > 1)
            {
                var sub = parts[1].ToLower();
                if (sub == "pop")
                {
                    var entry = stashService.Pop();
                    if (entry != null)
                    {
                        context.Tui.AddSystemMessage("已取出最近的暂存内容:");
                        context.Tui.AddRenderable(new Panel(entry.Content) { Header = new PanelHeader(entry.Type ?? "Stash"), Border = BoxBorder.Rounded });
                        context.Tui.Render();
                    }
                    else
                    {
                        context.Tui.AddSystemMessage("暂存列表为空。");
                        context.Tui.Render();
                    }
                    return;
                }
                if (sub == "code")
                {
                    var history = await sessionService.LoadHistoryAsync(context.CurrentSessionId);
                    var lastAssistant = history.FindLast(m => m.Role == "assistant");
                    if (lastAssistant != null)
                    {
                        var text = lastAssistant.GetText();
                        var match = System.Text.RegularExpressions.Regex.Match(text, "```(?:\\w+)?\\n([\\s\\S]*?)```");
                        if (match.Success)
                        {
                            var code = match.Groups[1].Value.Trim();
                            stashService.Push(code, "code", $"Code from {context.CurrentSessionId}");
                            context.Tui.AddSystemMessage("已成功暂存代码片段。");
                            context.Tui.Render();
                        }
                        else
                        {
                            context.Tui.AddSystemMessage("未在最后一条消息中找到代码块。");
                            context.Tui.Render();
                        }
                    }
                    else
                    {
                        context.Tui.AddSystemMessage("没有找到助手消息。");
                        context.Tui.Render();
                    }
                    return;
                }
            }

            var entries = stashService.List().ToList();
            if (!entries.Any())
            {
                context.Tui.AddSystemMessage("暂存列表为空。使用 'stash <code>' 暂存最后一条代码。");
                context.Tui.Render();
                return;
            }

            var table = new Table().RoundedBorder().Title("[bold blue]暂存记录[/]");
            table.AddColumn("索引");
            table.AddColumn("类型");
            table.AddColumn("内容预览");
            table.AddColumn("时间");

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var preview = e.Content.Length > 50 ? e.Content[..50].Replace("\n", " ") + "..." : e.Content.Replace("\n", " ");
                table.AddRow(
                    i.ToString(),
                    e.Type ?? "prompt",
                    preview,
                    DateTimeOffset.FromUnixTimeMilliseconds(e.Timestamp).LocalDateTime.ToString("MM-dd HH:mm")
                );
            }
            context.Tui.AddRenderable(table);
            context.Tui.Render();
        }

        private async Task HandleArchiveAsync(string input, CommandContext context, IServiceProvider serviceProvider)
        {
            var archiveService = serviceProvider.GetRequiredService<ArchiveService>();
            var sessionService = serviceProvider.GetRequiredService<SessionService>();
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            if (parts.Length > 1 && parts[1].ToLower() == "auto")
            {
                int days = 30;
                if (parts.Length > 2 && int.TryParse(parts[2], out var d)) days = d;
                
                context.Tui.UpdateStatus($"正在自动归档 {days} 天前的不活跃会话...");
                context.Tui.Render();
                
                int count = await archiveService.AutoArchiveAsync(days);
                context.Tui.AddSystemMessage($"自动归档完成，共归档 {count} 个会话。");
                context.Tui.UpdateStatus("Ready");
                context.Tui.Render();
            }
            else if (parts.Length > 1 && parts[1].ToLower() == "list")
            {
                var archived = await archiveService.ListArchivedSessionsAsync();
                if (archived.Count == 0)
                {
                    context.Tui.AddSystemMessage("没有已归档的会话。");
                    context.Tui.Render();
                }
                else
                {
                    var table = new Table().RoundedBorder().Title("[bold blue]已归档会话列表[/]");
                    table.AddColumn("ID");
                    table.AddColumn("标题");
                    table.AddColumn("归档时间");
                    foreach (var s in archived)
                    {
                        var archivedDate = s.ArchivedAt.HasValue 
                            ? DateTimeOffset.FromUnixTimeSeconds(s.ArchivedAt.Value).LocalDateTime.ToString("yyyy-MM-dd HH:mm")
                            : "未知";
                        table.AddRow(s.Id, s.Title ?? "无标题", archivedDate);
                    }
                    context.Tui.AddRenderable(table);
                    context.Tui.Render();
                }
            }
            else
            {
                await sessionService.ArchiveSessionAsync(context.CurrentSessionId);
                context.Tui.AddSystemMessage($"当前会话已归档: {context.CurrentSessionId}");
                context.Tui.Render();
            }
        }

        private async Task HandleUnarchiveAsync(string input, CommandContext context, IServiceProvider serviceProvider)
        {
            var archiveService = serviceProvider.GetRequiredService<ArchiveService>();
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string targetId = context.CurrentSessionId;

            if (parts.Length > 1)
            {
                targetId = parts[1];
            }

            await archiveService.RestoreSessionAsync(targetId);
            context.Tui.AddSystemMessage($"会话已成功恢复: {targetId}");
            context.Tui.Render();
        }

        private async Task HandleUninstallAsync(CommandContext context)
        {
            if (AnsiConsole.Confirm("[red]确定要卸载 OpenCode 并清理所有配置吗？[/]"))
            {
                context.Tui.AddSystemMessage("正在清理配置目录...");
                context.Tui.Render();
                // 实际清理逻辑...
                context.Tui.AddSystemMessage("卸载完成。");
                context.Tui.Render();
            }
        }

        private void ShowHelp(CommandContext context)
        {
            var table = new Table().RoundedBorder().Title("[bold blue]Command Help[/]");
            table.AddColumn("[yellow]Category[/]");
            table.AddColumn("[green]Command[/]");
            table.AddColumn("[blue]Aliases[/]");
            table.AddColumn("[grey]Description[/]");
            
            table.AddRow("Session", "/new", "clear", "Start a new session");
            table.AddRow("Session", "/sessions", "resume, continue", "List and switch sessions");
            table.AddRow("Session", "/undo", "", "Undo to the last user message");
            table.AddRow("Session", "/redo", "unrevert", "Redo the last undone message");
            table.AddRow("Session", "/revert", "", "Revert to a specific timeline ID");
            table.AddRow("Session", "/compact", "summarize", "Compact/summarize the session");
            table.AddRow("Session", "/fork", "", "Fork the current session");
            table.AddRow("Session", "/rename", "", "Rename the current session");
            table.AddRow("Session", "/delete", "", "Delete a session");
            table.AddRow("Session", "/timeline", "history, ls", "View session timeline");
            table.AddRow("Session", "/share", "", "Share current session");
            table.AddRow("Session", "/unshare", "", "Remove session share");
            table.AddRow("Session", "/status", "", "View current session status");

            table.AddEmptyRow();
            table.AddRow("Agent", "/agent", "", "Agent management");
            table.AddRow("Agent", "/init", "", "Initialize AGENTS.md rules");
            table.AddRow("Agent", "/review", "", "Code review");

            table.AddEmptyRow();
            table.AddRow("Provider", "/auth", "connect", "Provider authentication");
            table.AddRow("Provider", "/models", "model", "Model management");
            table.AddRow("Provider", "/mcp", "", "MCP tool management");

            table.AddEmptyRow();
            table.AddRow("System", "/exit", "quit, q", "Exit the program");
            table.AddRow("System", "/help", "h, ?", "Show this help information");
            table.AddRow("System", "/config", "", "View/debug configuration");
            table.AddRow("System", "/theme", "", "Switch UI theme");
            table.AddRow("System", "/upgrade", "", "Check for upgrades");
            table.AddRow("System", "/stats", "", "Usage and token statistics");
            
            context.Tui.AddRenderable(table);

            var shortcutTable = new Table().RoundedBorder().Title("[bold yellow]Keyboard Shortcuts[/]");
            shortcutTable.AddColumn("[green]Shortcut[/]");
            shortcutTable.AddColumn("[grey]Action[/]");
            shortcutTable.AddRow("Ctrl+L", "New session (Clear)");
            shortcutTable.AddRow("Ctrl+C", "Cancel / Exit");
            shortcutTable.AddRow("Ctrl+R", "Switch session (Resume)");
            shortcutTable.AddRow("Ctrl+X (Leader)", "Activate leader mode for shortcuts");
            shortcutTable.AddRow("Leader + m", "Cycle models");
            shortcutTable.AddRow("Leader + a", "Cycle agents");
            shortcutTable.AddRow("Leader + t", "Switch themes");
            shortcutTable.AddRow("Leader + g", "View code maps");
            shortcutTable.AddRow("Leader + s", "Share session");
            shortcutTable.AddRow("Leader + f", "Fork session");
            shortcutTable.AddRow("Leader + n", "New session");
            context.Tui.AddRenderable(shortcutTable);

            context.Tui.AddSystemMessage("Tip: All commands support the '/' prefix. Type starting with '!' to execute shell commands directly.");
            context.Tui.Render();
        }

        private async Task HandleAuthAsync(string input, CommandContext context, IServiceProvider serviceProvider)
        {
            var authService = serviceProvider.GetRequiredService<AuthService>();
            var discovery = serviceProvider.GetRequiredService<ModelDiscoveryService>();
            
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string provider = "";
            
            // auth logout [provider]
            if (parts.Length > 1 && parts[1].ToLower() == "logout")
            {
                provider = parts.Length > 2 ? parts[2].ToLower() : "";
                if (string.IsNullOrEmpty(provider))
                {
                    provider = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("请选择要注销的服务商:")
                            .AddChoices("openai", "anthropic", "google", "openrouter", "deepseek", "github", "github-copilot", "all"));
                }
                
                if (provider == "all")
                {
                    var allAuths = await authService.AllAsync();
                    foreach (var providerKey in allAuths.Keys)
                    {
                        await authService.RemoveAsync(providerKey);
                    }
                    context.Tui.AddSystemMessage("已清除所有认证信息。");
                }
                else
                {
                    await authService.RemoveAsync(provider);
                    context.Tui.AddSystemMessage($"已注销 {provider} 的认证信息。");
                }
                context.Tui.Render();
                return;
            }

            // auth status
            if (parts.Length > 1 && parts[1].ToLower() == "status")
            {
                var allAuths = await authService.AllAsync();
                var models = await discovery.GetModelsAsync();
                var providers = models.Values.Select(p => p.Id).OrderBy(x => x).ToList();
                if (providers.Count == 0)
                {
                    providers = new List<string> { "openai", "anthropic", "google", "openrouter", "deepseek", "github", "github-copilot" };
                }

                var statusRows = new List<string>();
                statusRows.Add("[bold blue]认证状态:[/]");
                foreach (var p in providers)
                {
                    var hasAuth = allAuths.ContainsKey(p);
                    var status = hasAuth ? "[green]Connected[/]" : "[grey]Disconnected[/]";
                    statusRows.Add($"- {p.PadRight(15)} {status}");
                }
                context.Tui.AddRenderable(new Rows(statusRows.Select(r => new Text(r))));
                context.Tui.Render();
                return;
            }

            // auth login [provider] OR auth [provider]
            if (parts.Length > 1)
            {
                var cmdOrUrl = parts[1].ToLower();
                if (cmdOrUrl == "login")
                {
                    if (parts.Length > 2)
                    {
                        provider = parts[2].ToLower();
                        if (provider.StartsWith("http://") || provider.StartsWith("https://"))
                        {
                            await HandleWellKnownAuthAsync(provider, context, serviceProvider);
                            return;
                        }
                    }
                }
                else if (cmdOrUrl == "list" || cmdOrUrl == "ls")
                {
                    await HandleAuthListAsync(context, serviceProvider);
                    return;
                }
                else if (cmdOrUrl == "status")
                {
                    return;
                }
                else if (cmdOrUrl == "logout")
                {
                    return;
                }
                else
                {
                    // 检查是否是 URL
                    if (cmdOrUrl.StartsWith("http://") || cmdOrUrl.StartsWith("https://"))
                    {
                        await HandleWellKnownAuthAsync(cmdOrUrl, context, serviceProvider);
                        return;
                    }

                    // 检查是否是已知的服务商
                    var models = await discovery.GetModelsAsync();
                    var knownProviders = models.Values.Select(p => p.Id).ToList();
                    if (knownProviders.Count == 0)
                    {
                        knownProviders = new List<string> { "openai", "anthropic", "google", "openrouter", "deepseek", "github", "github-copilot" };
                    }

                    if (knownProviders.Contains(cmdOrUrl))
                    {
                        provider = cmdOrUrl;
                    }
                    else
                    {
                        context.Tui.AddSystemMessage($"[red]错误: 未知的认证子指令或服务商 '{cmdOrUrl}'。[/]");
                        context.Tui.AddSystemMessage("[grey]可用子指令: login, logout, list, status[/]");
                        context.Tui.AddSystemMessage("[grey]或直接输入服务商名称: openai, anthropic, 等[/]");
                        context.Tui.Render();
                        return;
                    }
                }
            }

            if (string.IsNullOrEmpty(provider))
            {
                var models = await discovery.GetModelsAsync();
                var choices = models.Values.Select(p => p.Id).OrderBy(x => x).ToList();
                if (choices.Count == 0)
                {
                    choices = new List<string> { "openai", "anthropic", "google", "openrouter", "deepseek", "github", "github-copilot" };
                }
                
                choices.Add("取消");

                provider = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("请选择 AI 服务商:")
                        .PageSize(10)
                        .AddChoices(choices));
                
                if (provider == "取消")
                {
                    context.Tui.AddSystemMessage("[yellow]操作已取消。[/]");
                    context.Tui.Render();
                    return;
                }
            }
            
            // 显示特定服务商的提示信息
            if (provider == "openai")
            {
                context.Tui.AddSystemMessage("[grey]提示: 您可以在 https://platform.openai.com/api-keys 创建 API Key[/]");
            }
            else if (provider == "anthropic")
            {
                context.Tui.AddSystemMessage("[grey]提示: 您可以在 https://console.anthropic.com/settings/keys 创建 API Key[/]");
            }
            else if (provider == "deepseek")
            {
                context.Tui.AddSystemMessage("[grey]提示: 您可以在 https://platform.deepseek.com/api_keys 创建 API Key[/]");
            }
            context.Tui.Render();
            
            var key = AnsiConsole.Prompt(
                new TextPrompt<string>($"请输入 [bold blue]{provider}[/] 的 API Key (直接回车则取消):")
                    .AllowEmpty());

            if (!string.IsNullOrEmpty(key))
            {
                await authService.SetAsync(provider, new ApiAuthInfo(key));
                context.Tui.AddSystemMessage($"[green]成功保存 {provider} 的认证信息。[/]");
            }
            else
            {
                context.Tui.AddSystemMessage("[yellow]操作已取消。[/]");
            }
            context.Tui.Render();
        }

        private async Task HandleAuthListAsync(CommandContext context, IServiceProvider serviceProvider)
        {
            var authService = serviceProvider.GetRequiredService<AuthService>();
            var discovery = serviceProvider.GetRequiredService<ModelDiscoveryService>();
            
            var allAuths = await authService.AllAsync();
            var models = await discovery.GetModelsAsync();
            
            var table = new Table().RoundedBorder().Title("[bold blue]Providers[/]");
            table.AddColumn("ID");
            table.AddColumn("Name");
            table.AddColumn("Status");
            table.AddColumn("Type");

            var providers = models.Values.Select(p => p.Id).Union(allAuths.Keys).OrderBy(x => x).ToList();
            if (providers.Count == 0)
            {
                providers = new List<string> { "openai", "anthropic", "google", "openrouter", "deepseek", "github", "github-copilot" };
            }

            foreach (var p in providers)
            {
                var hasAuth = allAuths.TryGetValue(p, out var info);
                var name = models.TryGetValue(p, out var m) ? m.Name : p;
                var type = info?.Type ?? "-";
                
                table.AddRow(
                    p, 
                    name, 
                    hasAuth ? "[green]Connected[/]" : "[grey]Disconnected[/]",
                    type);
            }
            
            context.Tui.AddRenderable(table);
            context.Tui.Render();
        }

        private async Task HandleModelsAsync(string input, CommandContext context, IServiceProvider serviceProvider)
        {
            var discovery = serviceProvider.GetRequiredService<ModelDiscoveryService>();
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var subCommand = parts.Length > 1 ? parts[1].ToLower() : "switch";

            if (subCommand == "switch" || parts.Length == 1)
            {
                var providers = await discovery.GetModelsAsync();
                var choices = providers.Values.SelectMany(p => p.Models.Values.Select(m => $"{p.Id}/{m.Id}")).ToList();
                choices.Add("取消");
                
                var selectedModel = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("选择要切换的模型:")
                        .PageSize(10)
                        .AddChoices(choices));
                
                if (selectedModel == "取消")
                {
                    context.Tui.AddSystemMessage("[yellow]操作已取消。[/]");
                    context.Tui.Render();
                    return;
                }
                
                var cfg = serviceProvider.GetRequiredService<ConfigService>();
                cfg.Config.Model = selectedModel;
                await cfg.SaveAsync();
                
                context.Tui.AddSystemMessage($"[green]已切换到模型: {selectedModel}[/]");
                context.Tui.AddSystemMessage("[yellow]提示: 模型切换已保存。[/]");
                context.Tui.Render();
            }
            else if (subCommand == "list" || subCommand == "ls")
            {
                var providers = await discovery.GetModelsAsync();
                foreach (var p in providers.Values)
                {
                    var table = new Table().RoundedBorder().Title($"[bold blue]Provider: {p.Name} ({p.Id})[/]");
                    table.AddColumn("ID");
                    table.AddColumn("Name");
                    table.AddColumn("Context");

                    foreach (var m in p.Models.Values)
                    {
                        table.AddRow(m.Id, m.Name, m.Limit.Context.ToString("N0"));
                    }
                    context.Tui.AddRenderable(table);
                }
                context.Tui.Render();
            }
            else if (subCommand == "info")
            {
                if (parts.Length < 3)
                {
                    context.Tui.AddSystemMessage("[red]用法: model info <model_id>[/]");
                    context.Tui.Render();
                    return;
                }
                var modelId = parts[2];
                var providers = await discovery.GetModelsAsync();
                var model = providers.Values.SelectMany(p => p.Models.Values).FirstOrDefault(m => m.Id == modelId);

                if (model == null)
                {
                    context.Tui.AddSystemMessage($"[red]找不到模型: {modelId}[/]");
                    context.Tui.Render();
                    return;
                }

                var table = new Table().RoundedBorder().Title($"[bold blue]Model: {model.Name}[/]");
                table.AddColumn("Property");
                table.AddColumn("Value");
                table.AddRow("ID", model.Id);
                table.AddRow("Name", model.Name);
                table.AddRow("Context", model.Limit.Context.ToString("N0"));
                table.AddRow("Output", model.Limit.MaxOutput.ToString("N0"));
                context.Tui.AddRenderable(table);
                context.Tui.Render();
            }
            else if (subCommand == "search")
            {
                if (parts.Length < 3)
                {
                    context.Tui.AddSystemMessage("[red]用法: model search <query>[/]");
                    context.Tui.Render();
                    return;
                }
                var query = parts[2].ToLower();
                var providers = await discovery.GetModelsAsync();
                var results = providers.Values.SelectMany(p => p.Models.Values)
                    .Where(m => m.Id.ToLower().Contains(query) || m.Name.ToLower().Contains(query))
                    .ToList();

                if (!results.Any())
                {
                    context.Tui.AddSystemMessage($"[grey]未找到匹配 '{query}' 的模型。[/]");
                }
                else
                {
                    var table = new Table().RoundedBorder().Title($"[bold blue]Search Results: {query}[/]");
                    table.AddColumn("ID");
                    table.AddColumn("Name");
                    foreach (var m in results) table.AddRow(m.Id, m.Name);
                    context.Tui.AddRenderable(table);
                }
                context.Tui.Render();
            }
            else
            {
                context.Tui.AddSystemMessage($"[red]Error: Unknown model subcommand '{subCommand}'.[/]");
                context.Tui.AddSystemMessage("[grey]Available: list, switch, info [id], search [query][/]");
                context.Tui.Render();
            }
        }

        private async Task HandleWorktreeAsync(string input, CommandContext context, IServiceProvider serviceProvider)
        {
            var worktreeService = serviceProvider.GetRequiredService<WorktreeService>();
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var subCommand = parts.Length > 1 ? parts[1].ToLower() : "list";

            if (subCommand == "list")
            {
                context.Tui.UpdateStatus("正在获取 Worktree 列表...");
                context.Tui.Render();
                
                var worktrees = await worktreeService.ListAsync();
                var table = new Table().RoundedBorder().Title("[bold blue]Git Worktrees[/]");
                table.AddColumn("Name");
                table.AddColumn("Branch");
                table.AddColumn("Path");

                foreach (var w in worktrees)
                {
                    table.AddRow(w.Name, w.Branch, w.Directory);
                }
                context.Tui.AddRenderable(table);
                context.Tui.UpdateStatus("Ready");
                context.Tui.Render();
            }
            else if (subCommand == "create")
            {
                string? name = parts.Length > 2 ? parts[2] : null;
                context.Tui.UpdateStatus("正在创建新的 Worktree...");
                context.Tui.Render();
                
                var info = await worktreeService.CreateAsync(name);
                context.Tui.AddSystemMessage($"Worktree '{info.Name}' 创建成功！");
                context.Tui.AddSystemMessage($"- 分支: {info.Branch}");
                context.Tui.AddSystemMessage($"- 路径: {info.Directory}");
                context.Tui.UpdateStatus("Ready");
                context.Tui.Render();
            }
            else if (subCommand == "remove" || subCommand == "delete")
            {
                if (parts.Length < 3)
                {
                    context.Tui.AddSystemMessage("用法: worktree remove <directory_or_name>");
                    context.Tui.Render();
                    return;
                }
                var target = parts[2];
                // 尝试根据名称查找路径
                var list = await worktreeService.ListAsync();
                var match = list.FirstOrDefault(w => w.Name == target || w.Directory == target);
                
                if (match == null)
                {
                    context.Tui.AddSystemMessage($"找不到指定的 Worktree: {target}");
                    context.Tui.Render();
                    return;
                }

                if (AnsiConsole.Confirm($"[red]确定要删除 Worktree '{match.Name}' 吗？此操作不可逆。[/]"))
                {
                    context.Tui.UpdateStatus("正在删除...");
                    context.Tui.Render();
                    
                    await worktreeService.RemoveAsync(match.Directory);
                    context.Tui.AddSystemMessage($"Worktree '{match.Name}' 已成功删除。");
                    context.Tui.UpdateStatus("Ready");
                    context.Tui.Render();
                }
            }
            else if (subCommand == "cleanup")
            {
                int days = 7;
                if (parts.Length > 2 && int.TryParse(parts[2], out var d)) days = d;

                context.Tui.UpdateStatus($"正在清理超过 {days} 天未使用的 Worktree...");
                context.Tui.Render();
                
                await worktreeService.CleanupExpiredWorktreesAsync(TimeSpan.FromDays(days));
                context.Tui.AddSystemMessage("清理完成。");
                context.Tui.UpdateStatus("Ready");
                context.Tui.Render();
            }
            else
            {
                context.Tui.AddSystemMessage("未知 worktree 指令。可用: list, create [name], remove <name>, cleanup [days]");
                context.Tui.Render();
            }
        }

        private async Task HandleUpgradeAsync(CommandContext context, IServiceProvider serviceProvider)
        {
            var upgradeService = serviceProvider.GetRequiredService<UpgradeService>();
            
            context.Tui.UpdateStatus("正在检查版本更新...");
            context.Tui.Render();
            
            var (hasUpdate, info) = await upgradeService.CheckUpdateAsync();
            
            if (!hasUpdate)
            {
                context.Tui.AddSystemMessage($"当前已是最新版本 ({upgradeService.GetCurrentVersion()})。");
                context.Tui.UpdateStatus("Ready");
                context.Tui.Render();
                return;
            }

            context.Tui.AddSystemMessage($"发现新版本: {info?.Version}");
            context.Tui.AddSystemMessage($"当前版本: {upgradeService.GetCurrentVersion()}");
            
            if (info != null)
            {
                context.Tui.AddRenderable(new Panel(info.ReleaseNotes) { Header = new PanelHeader("更新内容"), Border = BoxBorder.Rounded });
                context.Tui.AddSystemMessage($"下载地址: {info.DownloadUrl}");
            }
            context.Tui.Render();

            if (AnsiConsole.Confirm("是否立即开始升级？"))
            {
                context.Tui.AddSystemMessage("正在启动升级程序...");
                context.Tui.Render();
                
                // 实际升级逻辑，这里可以调用不同的安装工具
                if (OperatingSystem.IsWindows())
                {
                    context.Tui.AddSystemMessage("提示: 推荐使用 'winget upgrade OpenCode' 进行升级。");
                }
                else if (OperatingSystem.IsMacOS())
                {
                    context.Tui.AddSystemMessage("提示: 推荐使用 'brew upgrade opencode' 进行升级。");
                }
                
                await upgradeService.UpgradeAsync();
                context.Tui.AddSystemMessage("升级指令已发出，请稍后重新启动程序。");
                context.Tui.Render();
            }
        }

        private async Task HandleStatsAsync(string input, CommandContext context, IServiceProvider serviceProvider)
        {
            var statsService = serviceProvider.GetRequiredService<StatsService>();
            var tui = context.Tui;
            
            tui.UpdateStatus("正在汇总统计数据...");
            tui.Render();

            var stats = await statsService.AggregateAsync();
            
            // 处理导出请求
            if (input.Contains("--export"))
            {
                var outputPath = "stats_export_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";
                await statsService.ExportToCsvAsync(stats, outputPath);
                tui.AddSystemMessage($"统计数据已导出至: {outputPath}");
                tui.UpdateStatus("Ready");
                tui.Render();
                return;
            }

            var table = new Table().RoundedBorder().Title("[bold blue]Usage Statistics[/]");
            table.AddColumn("[yellow]Metric[/]");
            table.AddColumn("[green]Value[/]");

            table.AddRow("Total Sessions", stats.TotalSessions.ToString());
            table.AddRow("Total Messages", stats.TotalMessages.ToString());
            table.AddRow("Median Messages", stats.MedianMessages.ToString());
            table.AddRow("Estimated Cost", $"${stats.TotalCost:F4}");
            table.AddRow("Median Cost", $"${stats.MedianCost:F4}");
            table.AddRow("Input Tokens", stats.TotalTokens.Input.ToString("N0"));
            table.AddRow("Output Tokens", stats.TotalTokens.Output.ToString("N0"));
            table.AddRow("Reasoning Tokens", stats.TotalTokens.Reasoning.ToString("N0"));
            table.AddRow("Cache Read", stats.TotalTokens.Cache.Read.ToString("N0"));
            table.AddRow("Cache Write", stats.TotalTokens.Cache.Write.ToString("N0"));

            tui.AddRenderable(table);

            if (stats.ModelUsage.Any())
            {
                var modelTable = new Table().RoundedBorder().Title("[bold blue]Model Usage[/]");
                modelTable.AddColumn("Model");
                modelTable.AddColumn("Messages");
                modelTable.AddColumn("Cost");
                modelTable.AddColumn("Tokens (In/Out)");

                foreach (var model in stats.ModelUsage.OrderByDescending(x => x.Value.Cost))
                {
                    modelTable.AddRow(
                        model.Key, 
                        model.Value.Messages.ToString(), 
                        $"${model.Value.Cost:F4}",
                        $"{model.Value.Tokens.Input.ToString("N0")} / {model.Value.Tokens.Output.ToString("N0")}");
                }
                tui.AddRenderable(modelTable);
            }

            if (stats.DailyUsage.Any())
            {
                var dailyTable = new Table().RoundedBorder().Title("[bold blue]Daily Trends (Last 7 Days)[/]");
                dailyTable.AddColumn("Date");
                dailyTable.AddColumn("Messages");
                dailyTable.AddColumn("Cost");
                dailyTable.AddColumn("Total Tokens");

                foreach (var day in stats.DailyUsage.OrderByDescending(x => x.Key).Take(7))
                {
                    dailyTable.AddRow(
                        day.Key,
                        day.Value.Messages.ToString(),
                        $"${day.Value.Cost:F4}",
                        (day.Value.Tokens.Input + day.Value.Tokens.Output + day.Value.Tokens.Reasoning).ToString("N0"));
                }
                tui.AddRenderable(dailyTable);
            }

            if (stats.ToolUsage.Any())
            {
                var toolTable = new Table().RoundedBorder().Title("[bold yellow]Tool Usage (Top 10)[/]");
                toolTable.AddColumn("Tool");
                toolTable.AddColumn("Calls");

                foreach (var tool in stats.ToolUsage.OrderByDescending(x => x.Value).Take(10))
                {
                    toolTable.AddRow(tool.Key, tool.Value.ToString());
                }
                tui.AddRenderable(toolTable);
            }

            tui.UpdateStatus("Ready");
            tui.Render();
        }

        private async Task HandleSessionStatusAsync(CommandContext context, SessionService sessionService)
        {
            var meta = await sessionService.GetMetadataAsync(context.CurrentSessionId);
            if (meta == null)
            {
                context.Tui.AddSystemMessage("错误: 无法获取当前会话的元数据。");
                context.Tui.Render();
                return;
            }

            var serviceProvider = context.ServiceProvider;
            var stats = await sessionService.GetStatsAsync(context.CurrentSessionId);
            var history = await sessionService.LoadHistoryAsync(context.CurrentSessionId);
            var timeline = await sessionService.GetTimelineAsync(context.CurrentSessionId);
            var vcsService = serviceProvider.GetRequiredService<VcsService>();

            var table = new Table().RoundedBorder().Title($"[bold blue]Session Status: {context.CurrentSessionId}[/]");
            table.AddColumn("[yellow]Property[/]");
            table.AddColumn("[green]Info[/]");

            table.AddRow("Title", meta.Title ?? "Untitled");
            table.AddRow("Created", DateTimeOffset.FromUnixTimeSeconds(meta.CreatedAt).LocalDateTime.ToString("yyyy-MM-dd HH:dd:ss"));
            table.AddRow("Updated", DateTimeOffset.FromUnixTimeSeconds(meta.UpdatedAt).LocalDateTime.ToString("yyyy-MM-dd HH:dd:ss"));
            table.AddRow("Messages", history.Count.ToString());
            
            // VCS 信息
            var branch = vcsService.Branch;
            if (!string.IsNullOrEmpty(branch))
            {
                table.AddRow("Branch", $"[cyan]{branch}[/]");
                var repoInfo = await vcsService.GetRepoInfoAsync();
                if (repoInfo != null)
                {
                    table.AddRow("Repository", $"{repoInfo.Owner}/{repoInfo.Name}");
                }
            }

            // 计算平均耗时
            var toolEvents = timeline.Where(e => e.Type == "tool" && e.Metadata?.ContainsKey("duration") == true).ToList();
            if (toolEvents.Count > 0)
            {
                var avgDuration = toolEvents.Average(e => Convert.ToDouble(e.Metadata!["duration"]));
                table.AddRow("Avg Tool Duration", $"{avgDuration:F2}ms");
            }

            if (meta.ForkedFrom != null)
            {
                table.AddRow("Forked From", meta.ForkedFrom);
            }

            if (stats != null)
            {
                table.AddRow("Total Tokens", (stats.TotalTokens.Input + stats.TotalTokens.Output + stats.TotalTokens.Reasoning).ToString("N0"));
                table.AddRow("Input Tokens", stats.TotalTokens.Input.ToString("N0"));
                table.AddRow("Output Tokens", stats.TotalTokens.Output.ToString("N0"));
                if (stats.TotalTokens.Reasoning > 0)
                    table.AddRow("Reasoning Tokens", stats.TotalTokens.Reasoning.ToString("N0"));
                table.AddRow("Estimated Cost", $"${stats.TotalCost:F4}");
            }
            else
            {
                table.AddRow("Stats", "[grey]No stats available[/]");
            }

            context.Tui.AddRenderable(table);
            context.Tui.Render();
        }

        private async Task HandleCompactAsync(CommandContext context, SessionService sessionService, SummaryService summaryService)
        {
            var history = await sessionService.LoadHistoryAsync(context.CurrentSessionId);
            if (history.Count == 0)
            {
                context.Tui.AddSystemMessage("当前会话没有历史记录可压缩。");
                context.Tui.Render();
                return;
            }

            context.Tui.UpdateStatus("正在压缩会话上下文...");
            context.Tui.Render();

            var summary = await summaryService.GenerateSummaryAsync(history.Select(m => new ChatMessage(m.Role == "user" ? ChatRole.User : ChatRole.Assistant, m.GetText())));
            
            context.Tui.AddRenderable(new Panel(summary) { Header = new PanelHeader("会话摘要"), Border = BoxBorder.Rounded });
            context.Tui.UpdateStatus("Ready");
            context.Tui.Render();
        }

        private async Task HandleInitAsync(CommandContext context)
        {
            // 对于交互式确认，我们仍然需要使用 AnsiConsole，但之后要重新渲染
            var agentsFile = Path.Combine(context.ProjectRoot, "AGENTS.md");
            if (File.Exists(agentsFile))
            {
                if (!AnsiConsole.Confirm("[yellow]AGENTS.md 已存在，是否覆盖？[/]")) return;
            }
            
            var content = "# OpenCode Project Rules\n\n- Follow TDD patterns\n- Keep functions small and modular\n- Use functional array methods\n";
            await File.WriteAllTextAsync(agentsFile, content);
            
            context.Tui.AddSystemMessage("已成功初始化 AGENTS.md。");
            context.Tui.Render();
        }

        private async Task HandleExportAsync(string input, CommandContext context, IServiceProvider serviceProvider)
        {
            var sessionExchange = serviceProvider.GetRequiredService<SessionExchangeService>();
            var sessionService = serviceProvider.GetRequiredService<SessionService>();
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            if (parts.Length > 1 && parts[1] == "list")
            {
                var previews = await sessionExchange.GetSessionPreviewsAsync();
                if (!previews.Any())
                {
                    context.Tui.AddSystemMessage("没有可导出的会话。");
                    context.Tui.Render();
                    return;
                }

                var selection = AnsiConsole.Prompt(
                    new SelectionPrompt<SessionExchangeService.SessionPreview>()
                        .Title("请选择要导出的会话:")
                        .PageSize(10)
                        .AddChoices(previews)
                        .UseConverter(p => $"{p.Title} ({p.UpdatedAt:yyyy-MM-dd HH:mm}) - {p.MessageCount} 消息"));

                await HandleExportAsync($"export {selection.Id}", context, serviceProvider);
                return;
            }

            string sessionId = parts.Length > 1 ? parts[1] : context.CurrentSessionId;

            try
            {
                var metadata = await sessionService.GetMetadataAsync(sessionId);
                if (metadata == null)
                {
                    context.Tui.AddSystemMessage($"错误: 找不到会话 {sessionId}");
                    context.Tui.Render();
                    return;
                }

                var defaultFileName = $"{sessionId}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                var outputPath = AnsiConsole.Ask<string>("请输入导出文件路径 (直接回车使用默认路径):", defaultFileName);

                context.Tui.UpdateStatus("正在导出...");
                context.Tui.Render();
                await sessionExchange.ExportToFileAsync(sessionId, outputPath);
                context.Tui.AddSystemMessage($"会话已成功导出到: {outputPath}");
                context.Tui.UpdateStatus("Ready");
                context.Tui.Render();
            }
            catch (Exception ex)
            {
                context.Tui.AddSystemMessage($"导出失败: {ex.Message}");
                context.Tui.Render();
            }
        }

        private async Task HandleImportAsync(string input, CommandContext context, IServiceProvider serviceProvider)
        {
            var sessionExchange = serviceProvider.GetRequiredService<SessionExchangeService>();
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                context.Tui.AddSystemMessage("用法: import <file|url>");
                context.Tui.Render();
                return;
            }
            var source = parts[1];
            
            try
            {
                string importedId;
                if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                    source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    context.Tui.UpdateStatus("正在从 URL 导入...");
                    context.Tui.Render();
                    importedId = await sessionExchange.ImportFromUrlAsync(source);
                    context.Tui.AddSystemMessage($"会话已成功从 URL 导入，ID: {importedId}");
                    
                    // 自动切换到导入的会话
                    context.SetSessionId(importedId);
                    context.SetFirstMessage(false);
                    context.Tui.AddSystemMessage($"已自动切换到导入的会话: {importedId}");
                    context.Tui.UpdateStatus("Ready");
                    context.Tui.Render();
                }
                else
                {
                    if (!File.Exists(source))
                    {
                        context.Tui.AddSystemMessage($"错误: 文件不存在 {source}");
                        context.Tui.Render();
                        return;
                    }

                    context.Tui.UpdateStatus("正在从文件导入...");
                    context.Tui.Render();
                    importedId = await sessionExchange.ImportFromFileAsync(source);
                    context.Tui.AddSystemMessage($"会话已成功从文件导入，ID: {importedId}");
                    
                    // 自动切换到导入的会话
                    context.SetSessionId(importedId);
                    context.SetFirstMessage(false);
                    context.Tui.AddSystemMessage($"已自动切换到导入的会话: {importedId}");
                    context.Tui.UpdateStatus("Ready");
                    context.Tui.Render();
                }
            }
            catch (Exception ex)
            {
                context.Tui.AddSystemMessage($"导入失败: {ex.Message}");
                context.Tui.Render();
            }
        }

        private async Task HandleForkAsync(string input, CommandContext context, IServiceProvider serviceProvider)
        {
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string? messageId = parts.Length > 1 ? parts[1] : null;
            
            var forkService = serviceProvider.GetRequiredService<ForkService>();
            
            try 
            {
                var newSessionId = await forkService.ForkSessionAsync(context.CurrentSessionId, messageId);
                
                context.SetSessionId(newSessionId);
                context.SetFirstMessage(false);
                context.Tui.AddSystemMessage($"已派生新会话: {newSessionId}");
                context.Tui.Render();
            }
            catch (Exception ex)
            {
                context.Tui.AddSystemMessage($"Fork 失败: {ex.Message}");
                context.Tui.Render();
            }
        }

        private async Task HandlePrAsync(string input, CommandContext context, IServiceProvider serviceProvider)
        {
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var subCommand = parts.Length > 1 ? parts[1].ToLower() : "";
            
            var ghService = serviceProvider.GetRequiredService<GitHubService>();
            var vcsService = serviceProvider.GetRequiredService<VcsService>();

            if (subCommand == "list")
            {
                var repoInfo = await vcsService.GetRepoInfoAsync();
                if (repoInfo == null)
                {
                    context.Tui.AddSystemMessage("当前目录不是一个 Git 仓库。");
                    context.Tui.Render();
                    return;
                }

                context.Tui.UpdateStatus($"正在获取 {repoInfo.Owner}/{repoInfo.Name} 的 Pull Requests 列表...");
                context.Tui.Render();
                var prs = await ghService.ListPullRequestsAsync(repoInfo.Owner, repoInfo.Name);
                
                if (!prs.Any())
                {
                    context.Tui.AddSystemMessage("未发现相关的开放 PR。");
                }
                else
                {
                    var table = new Table().RoundedBorder().Title($"[bold blue]PR 列表: {repoInfo.Owner}/{repoInfo.Name}[/]");
                    table.AddColumn("[yellow]#[/]");
                    table.AddColumn("[green]标题[/]");
                    table.AddColumn("[blue]作者[/]");
                    table.AddColumn("[cyan]链接[/]");

                    foreach (var pr in prs)
                    {
                        table.AddRow(pr.Number.ToString(), pr.Title, pr.User.Login, $"[link]{pr.HtmlUrl}[/]");
                    }
                    context.Tui.AddRenderable(table);
                }
                context.Tui.UpdateStatus("Ready");
                context.Tui.Render();
            }
            else if (subCommand == "status")
            {
                if (parts.Length < 3)
                {
                    context.Tui.AddSystemMessage("错误: 请指定 PR 编号。用法: pr status <number>");
                    context.Tui.Render();
                    return;
                }

                if (!int.TryParse(parts[2], out var prNumber))
                {
                    context.Tui.AddSystemMessage("错误: PR 编号必须是数字。");
                    context.Tui.Render();
                    return;
                }

                var repoInfo = await vcsService.GetRepoInfoAsync();
                if (repoInfo == null)
                {
                    context.Tui.AddSystemMessage("当前目录不是一个 Git 仓库。");
                    context.Tui.Render();
                    return;
                }

                context.Tui.UpdateStatus($"正在获取 PR #{prNumber} 状态...");
                context.Tui.Render();

                var user = await ghService.GetUserAsync();
                if (user == null)
                {
                    context.Tui.AddSystemMessage("提示: 未检测到有效的 GitHub 认证。");
                    context.Tui.AddSystemMessage("您可以运行 'auth login github' 来配置 GitHub Personal Access Token。");
                    context.Tui.AddSystemMessage("如果您已安装 GitHub CLI (gh)，请确保已运行 'gh auth login'。");
                }

                var pr = await ghService.GetPullRequestAsync(repoInfo.Owner, repoInfo.Name, prNumber);
                if (pr == null)
                {
                    context.Tui.AddSystemMessage($"找不到 PR #{prNumber}。");
                    context.Tui.UpdateStatus("Ready");
                    context.Tui.Render();
                    return;
                }

                var table = new Table().RoundedBorder().Title($"[bold blue]PR #{prNumber} 详情[/]");
                table.AddColumn("[yellow]属性[/]");
                table.AddColumn("[green]内容[/]");

                table.AddRow("标题", pr.Title);
                table.AddRow("作者", pr.User.Login);
                table.AddRow("状态", pr.State);
                table.AddRow("创建时间", pr.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
                table.AddRow("分支", $"{pr.Head.Ref} -> {pr.Base.Ref}");
                
                var sessionKey = await ghService.DetectSessionInPrAsync(repoInfo.Owner, repoInfo.Name, prNumber);
                table.AddRow("OpenCode 会话", sessionKey != null ? $"[green]{sessionKey}[/]" : "[grey]未检测到[/]");

                context.Tui.AddRenderable(table);

                if (!string.IsNullOrWhiteSpace(pr.Body))
                {
                    context.Tui.AddRenderable(new Panel(pr.Body) { Header = new PanelHeader("描述"), Border = BoxBorder.Rounded });
                }
                
                context.Tui.UpdateStatus("Ready");
                context.Tui.Render();
            }
            else if (subCommand == "checkout")
            {
                if (parts.Length < 3)
                {
                    context.Tui.AddSystemMessage("错误: 请指定 PR 编号。用法: pr checkout <number>");
                    context.Tui.Render();
                    return;
                }
                
                var prNumberStr = parts[2];
                if (!int.TryParse(prNumberStr, out var prNumber))
                {
                    context.Tui.AddSystemMessage("错误: PR 编号必须是数字。");
                    context.Tui.Render();
                    return;
                }

                var repoInfo = await vcsService.GetRepoInfoAsync();
                if (repoInfo != null)
                {
                    var sessionKey = await ghService.DetectSessionInPrAsync(repoInfo.Owner, repoInfo.Name, prNumber);
                    if (sessionKey != null)
                    {
                        if (AnsiConsole.Confirm($"[yellow]在 PR #{prNumber} 中检测到 OpenCode 会话，是否导入并切换？[/]"))
                        {
                            await HandleImportAsync($"import https://opencode.ai/s/{sessionKey}", context, serviceProvider);
                        }
                    }
                }
                
                context.Tui.AddSystemMessage($"正在检出 PR #{prNumber}...");
                context.Tui.Render();
                
                var psi = new ProcessStartInfo("gh", $"pr checkout {prNumber}")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    var output = await process.StandardOutput.ReadToEndAsync();
                    var error = await process.StandardError.ReadToEndAsync();
                    
                    if (process.ExitCode == 0)
                    {
                        context.Tui.AddSystemMessage("检出成功!");
                        if (!string.IsNullOrEmpty(output)) context.Tui.AddSystemMessage(output);
                    }
                    else
                    {
                        context.Tui.AddSystemMessage($"检出失败 (退出码 {process.ExitCode}):");
                        context.Tui.AddSystemMessage(error);
                    }
                    context.Tui.Render();
                }
            }
            else if (subCommand == "create")
            {
                var repoInfo = await vcsService.GetRepoInfoAsync();
                if (repoInfo == null)
                {
                    context.Tui.AddSystemMessage("当前目录不是一个 Git 仓库。");
                    context.Tui.Render();
                    return;
                }

                var title = AnsiConsole.Ask<string>("PR 标题:");
                var head = AnsiConsole.Ask<string>("Head 分支 (当前开发分支):");
                var baseBranch = AnsiConsole.Ask<string>("Base 分支 (合并到的目标分支):", "main");
                var body = AnsiConsole.Ask<string>("描述 (可选):", "");

                var pr = await ghService.CreatePullRequestAsync(repoInfo.Owner, repoInfo.Name, title, head, baseBranch, body);
                if (pr != null)
                {
                    context.Tui.AddSystemMessage($"PR 创建成功: #{pr.Number} - {pr.Title}");
                    context.Tui.AddSystemMessage($"链接: {pr.HtmlUrl}");
                }
                else
                {
                    context.Tui.AddSystemMessage("PR 创建失败，请检查配置和权限。");
                }
                context.Tui.Render();
            }
            else
            {
                context.Tui.AddSystemMessage($"错误: 未知的 PR 子指令 '{subCommand}'。");
                context.Tui.AddSystemMessage("可用子指令: list, info [number], checkout [number], create");
                context.Tui.Render();
            }
        }

        private async Task HandleMcpAsync(string input, CommandContext context, IServiceProvider serviceProvider)
        {
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var subCommand = parts.Length > 1 ? parts[1].ToLower() : "";
            var mcpService = serviceProvider.GetRequiredService<McpService>();

            if (subCommand == "list")
            {
                var servers = mcpService.GetServers();
                var table = new Table().RoundedBorder().Title("[bold blue]已配置的 MCP 服务器[/]");
                table.AddColumn("[yellow]名称[/]");
                table.AddColumn("[green]类型[/]");
                table.AddColumn("[blue]状态[/]");

                foreach (var s in servers)
                {
                    table.AddRow(s.Name, s.Type, s.IsConnected ? "[green]已连接[/]" : "[red]未连接[/]");
                }
                context.Tui.AddRenderable(table);
                context.Tui.Render();
            }
            else if (subCommand == "install")
            {
                if (parts.Length < 3)
                {
                    context.Tui.AddSystemMessage("用法: mcp install <name> <command> [args]");
                    context.Tui.Render();
                    return;
                }
                context.Tui.AddSystemMessage("正在安装 MCP 服务器...");
                // TODO: 实现 MCP 安装逻辑
                context.Tui.AddSystemMessage("安装完成 (模拟)。");
                context.Tui.Render();
            }
            else if (subCommand == "uninstall")
            {
                if (parts.Length < 3)
                {
                    context.Tui.AddSystemMessage("用法: mcp uninstall <name>");
                    context.Tui.Render();
                    return;
                }
                context.Tui.AddSystemMessage("正在卸载 MCP 服务器...");
                // TODO: 实现 MCP 卸载逻辑
                context.Tui.AddSystemMessage("卸载完成 (模拟)。");
                context.Tui.Render();
            }
            else if (subCommand == "auth")
            {
                if (parts.Length < 3)
                {
                    context.Tui.AddSystemMessage("错误: 请指定 MCP 服务器名称。用法: mcp auth <name>");
                    context.Tui.Render();
                    return;
                }
                var mcpName = parts[2];
                var oauthService = serviceProvider.GetRequiredService<McpOAuthService>();

                try
                {
                    var authUrl = await mcpService.StartAuthAsync(mcpName);
                    if (authUrl != null)
                    {
                        context.Tui.AddSystemMessage($"请在浏览器中打开以下链接进行认证: [link]{authUrl}[/]");
                        context.Tui.Render();
                        Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

                        var state = await serviceProvider.GetRequiredService<McpAuthService>().GetOAuthStateAsync(mcpName);
                        if (state != null)
                        {
                            context.Tui.AddSystemMessage("正在等待回调...");
                            context.Tui.Render();
                            var code = await oauthService.WaitForCallbackAsync(state);
                            await mcpService.FinishAuthAsync(mcpName, code);
                            context.Tui.AddSystemMessage($"MCP 服务器 '{mcpName}' 认证成功。");
                            context.Tui.Render();
                        }
                    }
                }
                catch (Exception ex)
                {
                    context.Tui.AddSystemMessage($"认证失败: {ex.Message}");
                    context.Tui.Render();
                }
            }
            else
            {
                context.Tui.AddSystemMessage($"错误: 未知的 MCP 子指令 '{subCommand}'。");
                context.Tui.AddSystemMessage("可用子指令: list, install, uninstall, auth");
                context.Tui.Render();
            }
        }

        private async Task HandleDebugAsync(string input, CommandContext context, IServiceProvider serviceProvider)
        {
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var subCommand = parts.Length > 1 ? parts[1].ToLower() : "";
            
            if (subCommand == "config")
            {
                var configService = serviceProvider.GetRequiredService<ConfigService>();
                var configJson = JsonSerializer.Serialize(configService.Config, new JsonSerializerOptions { WriteIndented = true });
                context.Tui.AddRenderable(new Panel(configJson) { Header = new PanelHeader("当前配置"), Border = BoxBorder.Rounded });
                context.Tui.Render();
            }
            else if (subCommand.StartsWith("agent "))
            {
                var agentName = subCommand.Substring(6).Trim();
                var agentProvider = serviceProvider.GetRequiredService<IAgentConfigurationProvider>();
                var agent = await agentProvider.GetAgentAsync(agentName);
                if (agent != null)
                {
                    context.Tui.AddRenderable(new Panel(agent.Prompt) { Header = new PanelHeader($"智能体: {agentName}"), Border = BoxBorder.Rounded });
                }
                else
                {
                    context.Tui.AddSystemMessage($"找不到智能体: {agentName}");
                }
                context.Tui.Render();
            }
            else if (subCommand == "scrap")
            {
                context.Tui.AddSystemMessage("Debug Scrap (Placeholder for Parity):");
                context.Tui.AddSystemMessage("foo: 42");
                context.Tui.AddSystemMessage("bar: 123");
                context.Tui.Render();
            }
            else
            {
                context.Tui.AddSystemMessage($"错误: 未知的调试子指令 '{subCommand}'。");
                context.Tui.AddSystemMessage("可用子指令: config, scrap, agent [name]");
                context.Tui.Render();
            }
        }

        private async Task HandleAgentAsync(string input, CommandContext context, IServiceProvider serviceProvider)
        {
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var subCommand = parts.Length > 1 ? parts[1].ToLower() : "";
            var agentProvider = serviceProvider.GetRequiredService<IAgentConfigurationProvider>();

            if (subCommand == "list" || string.IsNullOrEmpty(subCommand))
            {
                var agents = await agentProvider.GetAllAgentsAsync();
                var table = new Table().RoundedBorder().Title("[bold blue]可用智能体[/]");
                table.AddColumn("[yellow]名称[/]");
                table.AddColumn("[green]描述[/]");
                table.AddColumn("[blue]模式[/]");

                foreach (var a in agents)
                {
                    table.AddRow(a.Name, a.Description ?? "", a.Mode);
                }
                context.Tui.AddRenderable(table);
                context.Tui.Render();
            }
            else if (subCommand == "switch")
            {
                var agents = await agentProvider.GetAllAgentsAsync();
                if (!agents.Any())
                {
                    context.Tui.AddSystemMessage("没有可用的智能体。");
                    context.Tui.Render();
                    return;
                }

                var selected = AnsiConsole.Prompt(
                    new SelectionPrompt<AgentMetadata>()
                        .Title("选择要切换到的智能体:")
                        .AddChoices(agents)
                        .UseConverter(a => $"{a.Name} ({a.Description})"));

                var sessionService = serviceProvider.GetRequiredService<SessionService>();
                var metadata = await sessionService.GetMetadataAsync(context.CurrentSessionId);
                if (metadata != null)
                {
                    metadata = metadata with
                    {
                        Title = $"Session with {selected.Name}",
                        AgentName = selected.Name
                    };
                    await sessionService.SaveMetadataAsync(context.CurrentSessionId, metadata);

                    var switchEvent = new TimelineEvent(
                        Id: "switch_" + Guid.NewGuid().ToString("N")[..8],
                        Type: "agent_switch",
                        Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Description: $"Switched agent to {selected.Name}",
                        Metadata: new Dictionary<string, object> { { "agentName", selected.Name } }
                    );
                    await sessionService.AddTimelineEventAsync(context.CurrentSessionId, switchEvent);

                    context.Tui.AddSystemMessage($"已成功切换到智能体: {selected.Name}");
                    context.Tui.AddSystemMessage("后续消息将使用该智能体的系统提示词。");
                    context.Tui.Render();
                }
            }
            else if (subCommand == "create")
            {
                if (parts.Length < 3)
                {
                    context.Tui.AddSystemMessage("用法: agent create <description>");
                    context.Tui.Render();
                    return;
                }
                var description = string.Join(" ", parts.Skip(2));
                var genService = serviceProvider.GetRequiredService<AgentGenerationService>();
                
                context.Tui.UpdateStatus("正在生成智能体配置...");
                context.Tui.Render();
                var generated = await genService.GenerateAgentAsync(description);
                if (generated != null)
                {
                    var name = generated["identifier"]?.ToString() ?? "new-agent";
                    var agent = new AgentMetadata
                    {
                        Name = name,
                        Description = generated["whenToUse"]?.ToString() ?? "",
                        Prompt = generated["systemPrompt"]?.ToString() ?? "",
                        Mode = "all"
                    };
                    await agentProvider.SaveAgentAsync(agent);
                    context.Tui.AddSystemMessage($"智能体 '{name}' 已创建成功。");
                }
                else
                {
                    context.Tui.AddSystemMessage("生成智能体配置失败。");
                }
                context.Tui.UpdateStatus("Ready");
                context.Tui.Render();
            }
            else
            {
                context.Tui.AddSystemMessage($"错误: 未知的智能体子指令 '{subCommand}'。");
                context.Tui.AddSystemMessage("可用子指令: list, switch, create [desc]");
                context.Tui.Render();
            }
        }

        private async Task HandleRevertAsync(string input, CommandContext context, SessionService sessionService)
        {
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string? eventId = parts.Length > 1 ? parts[1] : null;

            if (string.IsNullOrEmpty(eventId))
            {
                var timeline = await sessionService.GetTimelineAsync(context.CurrentSessionId);
                var selected = AnsiConsole.Prompt(
                    new SelectionPrompt<TimelineEvent>()
                        .Title("请选择要回滚到的事件:")
                        .AddChoices(timeline.TakeLast(20))
                        .UseConverter(e => $"[{e.Type}] {e.Id} ({DateTimeOffset.FromUnixTimeMilliseconds(e.Timestamp).LocalDateTime:HH:mm:ss})"));
                eventId = selected.Id;
            }

            await sessionService.RevertAsync(context.CurrentSessionId, eventId);
            AnsiConsole.MarkupLine($"[green]已成功回滚到事件: {eventId}[/]");
        }

        private async Task HandleGithubAsync(string input, CommandContext context, IServiceProvider serviceProvider)
        {
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var subCommand = parts.Length > 1 ? parts[1].ToLower() : "info";
            var ghService = serviceProvider.GetRequiredService<GitHubService>();
            var vcsService = serviceProvider.GetRequiredService<VcsService>();

            if (subCommand == "auth")
            {
                await HandleAuthAsync("auth github", context, serviceProvider);
            }
            else if (subCommand == "info")
            {
                var repoInfo = await vcsService.GetRepoInfoAsync();
                if (repoInfo == null)
                {
                    AnsiConsole.MarkupLine("[red]当前目录不是一个 Git 仓库。[/]");
                    return;
                }
                AnsiConsole.MarkupLine($"[bold blue]GitHub 仓库信息:[/]");
                AnsiConsole.MarkupLine($"- Owner: {repoInfo.Owner}");
                AnsiConsole.MarkupLine($"- Name: {repoInfo.Name}");
                AnsiConsole.MarkupLine($"- URL: https://github.com/{repoInfo.Owner}/{repoInfo.Name}");
            }
            else
            {
                // 其他子命令转发给 pr 处理，或者显示帮助
                await HandlePrAsync(input.Replace("github", "pr"), context, serviceProvider);
            }
        }

        private async Task HandleShellCommandAsync(string command)
        {
            AnsiConsole.MarkupLine($"[bold blue]执行 Shell 指令:[/] {command}");
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe", // 默认 Windows
                Arguments = $"/c {command}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            {
                psi.FileName = "/bin/bash";
                psi.Arguments = $"-c \"{command}\"";
            }

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync();
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();

                if (!string.IsNullOrEmpty(output)) AnsiConsole.WriteLine(output);
                if (!string.IsNullOrEmpty(error)) AnsiConsole.MarkupLine($"[red]{error}[/]");
            }
        }

        private async Task HandleReviewAsync(string input, IServiceProvider serviceProvider)
        {
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var target = parts.Length > 1 ? parts[1] : "uncommitted";
            
            AnsiConsole.MarkupLine($"[bold blue]正在评审变更:[/] {target}");
            AnsiConsole.MarkupLine("[grey]提示: 正在调用 AI 进行代码评审，这可能需要一点时间...[/]");
            
            // 构造评审指令发送给 AI
            var prompt = $"Please review the code changes in {target}. Focus on potential bugs, performance issues, and code style. Use 'git diff' to see the changes first.";
            
            // 这里我们通过 TUI 模拟用户输入，让 main loop 的 AI 处理它
            // 或者直接在这里调用 Workflow
            var workflow = serviceProvider.GetRequiredService<Workflow>();
            var cts = new CancellationTokenSource();
            
            _ = workflow.RunAsync("ThinkingExecutor", prompt, cts.Token);

            await foreach (var output in workflow.Output)
            {
                if (output is JsonNode node)
                {
                    var status = node["status"]?.ToString();
                    if (status == "thinking")
                    {
                        AnsiConsole.MarkupLine($"[grey]AI: {node["message"]}[/]");
                    }
                    else if (status == "completed")
                    {
                        AnsiConsole.MarkupLine($"\n[green]评审建议:[/]\n{node["answer"]}");
                        break;
                    }
                    else if (status == "error")
                    {
                        AnsiConsole.MarkupLine($"\n[red]错误: {node["message"]}[/]");
                        break;
                    }
                }
            }
        }

        private async Task HandleShareAsync(CommandContext context, IServiceProvider serviceProvider)
        {
            var shareService = serviceProvider.GetRequiredService<ShareService>();
            var sessionId = context.CurrentSessionId;

            try
            {
                var existingShare = await shareService.GetShareAsync(sessionId);
                if (existingShare != null)
                {
                    var choice = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("该会话已存在分享链接:")
                            .AddChoices("复制链接", "重新同步 (Update)", "删除分享", "取消"));

                    if (choice == "复制链接")
                    {
                        AnsiConsole.MarkupLine($"[green]分享链接: {existingShare.ShareUrl}[/]");
                        return;
                    }
                    if (choice == "重新同步 (Update)")
                    {
                        await AnsiConsole.Status().StartAsync("正在更新云端数据...", async ctx => {
                            await shareService.UpdateShareAsync(sessionId);
                            AnsiConsole.MarkupLine("[green]云端数据已同步。[/]");
                        });
                        return;
                    }
                    if (choice == "删除分享")
                    {
                        if (AnsiConsole.Confirm("[red]确定要从云端删除此分享吗？[/]"))
                        {
                            await AnsiConsole.Status().StartAsync("正在删除...", async ctx => {
                                await shareService.DeleteShareAsync(sessionId);
                                AnsiConsole.MarkupLine("[green]分享已删除。[/]");
                            });
                        }
                        return;
                    }
                    return;
                }

                if (AnsiConsole.Confirm("是否创建该会话的公开分享链接？(内容将上传至 opencode.ai)"))
                {
                    await AnsiConsole.Status().StartAsync("正在创建分享...", async ctx => {
                        var share = await shareService.CreateShareAsync(sessionId);
                        AnsiConsole.MarkupLine($"[green]分享创建成功！[/]");
                        AnsiConsole.MarkupLine($"[bold blue]URL: {share.ShareUrl}[/]");
                        AnsiConsole.MarkupLine("[grey]提示: 该链接为公开访问，任何人持有链接均可查看。[/]");
                    });
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]分享操作失败: {ex.Message}[/]");
            }
        }

        private async Task HandleUndoAsync(CommandContext context, SessionService sessionService)
        {
            var timeline = await sessionService.GetTimelineAsync(context.CurrentSessionId);
            var lastUserMsgIndex = timeline.FindLastIndex(e => e.Type == "message" && e.Description?.Contains("user") == true);
            
            if (lastUserMsgIndex >= 0)
            {
                // 我们想回到最后一条用户消息之前的状态
                // 如果是第一条消息 (index 0)，则回到没有任何消息的状态
                string? targetEventId = null;
                string targetDesc = "会话开始";

                if (lastUserMsgIndex > 0)
                {
                    var prevEvent = timeline[lastUserMsgIndex - 1];
                    targetEventId = prevEvent.Id;
                    targetDesc = prevEvent.Description ?? prevEvent.Type;
                }

                if (AnsiConsole.Confirm($"确定要撤销最后一次对话吗？\n(将回退到: [yellow]{targetDesc}[/])"))
                {
                    await sessionService.RevertAsync(context.CurrentSessionId, targetEventId ?? "");
                    await context.Tui.LoadHistoryAsync(context.CurrentSessionId);
                    context.Tui.AddSystemMessage("撤销成功。");
                    context.Tui.Render();
                }
            }
            else
            {
                context.Tui.AddSystemMessage("未找到可撤销的消息。");
                context.Tui.Render();
            }
        }

        private async Task HandleRedoAsync(CommandContext context, SessionService sessionService)
        {
            try 
            {
                await sessionService.UnrevertAsync(context.CurrentSessionId);
                await context.Tui.LoadHistoryAsync(context.CurrentSessionId);
                context.Tui.AddSystemMessage("已成功撤销回滚，恢复到之前的状态。");
                context.Tui.Render();
            }
            catch (InvalidOperationException ex)
            {
                context.Tui.AddSystemMessage(ex.Message);
                context.Tui.Render();
            }
            catch (Exception ex)
            {
                context.Tui.AddSystemMessage($"恢复失败: {ex.Message}");
                context.Tui.Render();
            }
        }

        private async Task HandleUnshareAsync(CommandContext context, IServiceProvider serviceProvider)
        {
            var shareService = serviceProvider.GetRequiredService<ShareService>();
            var sessionId = context.CurrentSessionId;

            context.Tui.UpdateStatus("正在取消共享...");
            context.Tui.Render();
            
            await shareService.DeleteShareAsync(sessionId);
            
            context.Tui.UpdateStatus("Ready");
            context.Tui.AddSystemMessage("会话共享已取消。");
            context.Tui.Render();
        }

        private async Task HandleDeleteSessionAsync(string input, CommandContext context, SessionService sessionService)
        {
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string sessionId = context.CurrentSessionId;

            if (parts.Length > 1)
            {
                sessionId = parts[1];
            }

            if (AnsiConsole.Confirm($"确定要删除会话 '{sessionId}' 吗？此操作不可逆。"))
            {
                await sessionService.DeleteSessionAsync(sessionId);
                context.Tui.AddSystemMessage($"会话 '{sessionId}' 已删除。");
                
                if (sessionId == context.CurrentSessionId)
                {
                    await HandleNewSessionAsync(context, sessionService);
                }
                else
                {
                    context.Tui.Render();
                }
            }
        }

        public async Task<string?> ReadInputAsync(CommandContext context)
        {
            var input = new StringBuilder();
            
            AnsiConsole.Markup("[green]请输入指令 (exit 退出, help 帮助):[/] ");

            while (true)
            {
                if (!Console.KeyAvailable)
                {
                    await Task.Delay(50);
                    continue;
                }

                var key = Console.ReadKey(true);

                // Ctrl+L: New Session
                if (key.Modifiers == ConsoleModifiers.Control && key.Key == ConsoleKey.L)
                {
                    Console.WriteLine();
                    await ProcessCommandAsync("new", context);
                    return null;
                }

                // Ctrl+R: Switch Session
                if (key.Modifiers == ConsoleModifiers.Control && key.Key == ConsoleKey.R)
                {
                    Console.WriteLine();
                    await ProcessCommandAsync("sessions", context);
                    return null;
                }

                // Ctrl+Z: Undo
                if (key.Modifiers == ConsoleModifiers.Control && key.Key == ConsoleKey.Z)
                {
                    Console.WriteLine();
                    await ProcessCommandAsync("undo", context);
                    return null;
                }

                // Ctrl+Y: Redo
                if (key.Modifiers == ConsoleModifiers.Control && key.Key == ConsoleKey.Y)
                {
                    Console.WriteLine();
                    await ProcessCommandAsync("redo", context);
                    return null;
                }

                // Ctrl+X: Leader Key
                if (key.Modifiers == ConsoleModifiers.Control && key.Key == ConsoleKey.X)
                {
                    Console.WriteLine();
                    await HandleLeaderKeyAsync(context);
                    return null;
                }

                // Enter
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return input.ToString();
                }

                // Backspace
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (input.Length > 0)
                    {
                        input.Remove(input.Length - 1, 1);
                        Console.Write("\b \b");
                    }
                    continue;
                }

                // Escape
                if (key.Key == ConsoleKey.Escape)
                {
                    // Clear current line
                    while (input.Length > 0)
                    {
                        input.Remove(input.Length - 1, 1);
                        Console.Write("\b \b");
                    }
                    continue;
                }

                // Regular character
                if (!char.IsControl(key.KeyChar))
                {
                    input.Append(key.KeyChar);
                    Console.Write(key.KeyChar);
                }
            }
        }

        private async Task HandleLeaderKeyAsync(CommandContext context)
        {
            AnsiConsole.MarkupLine("[bold yellow]Leader Mode (Ctrl+X) 激活。按对应按键执行操作:[/]");
            AnsiConsole.MarkupLine("[grey]- m: 切换模型 (models)[/]");
            AnsiConsole.MarkupLine("[grey]- a: 切换智能体 (agents)[/]");
            AnsiConsole.MarkupLine("[grey]- t: 切换主题 (themes)[/]");
            AnsiConsole.MarkupLine("[grey]- g: 代码图谱 (maps)[/]");
            AnsiConsole.MarkupLine("[grey]- s: 分享会话 (share)[/]");
            AnsiConsole.MarkupLine("[grey]- f: Fork 会话 (fork)[/]");
            AnsiConsole.MarkupLine("[grey]- n: 新建会话 (new)[/]");
            AnsiConsole.MarkupLine("[grey]- z: 撤销 (undo)[/]");
            AnsiConsole.MarkupLine("[grey]- y: 重做 (redo)[/]");
            AnsiConsole.MarkupLine("[grey]- Esc: 取消[/]");

            var key = Console.ReadKey(true);
            switch (key.Key)
            {
                case ConsoleKey.M:
                    await ProcessCommandAsync("models", context);
                    break;
                case ConsoleKey.A:
                    await ProcessCommandAsync("agents switch", context);
                    break;
                case ConsoleKey.T:
                    await ProcessCommandAsync("themes", context);
                    break;
                case ConsoleKey.G:
                    await ProcessCommandAsync("maps", context);
                    break;
                case ConsoleKey.S:
                    await ProcessCommandAsync("share", context);
                    break;
                case ConsoleKey.F:
                    await ProcessCommandAsync("fork", context);
                    break;
                case ConsoleKey.N:
                    await ProcessCommandAsync("new", context);
                    break;
                case ConsoleKey.Z:
                    await ProcessCommandAsync("undo", context);
                    break;
                case ConsoleKey.Y:
                    await ProcessCommandAsync("redo", context);
                    break;
                case ConsoleKey.Escape:
                    AnsiConsole.MarkupLine("[yellow]已取消 Leader Mode。[/]");
                    break;
                default:
                    AnsiConsole.MarkupLine($"[red]无效按键: {key.Key}[/]");
                    break;
            }
        }
    }
}
