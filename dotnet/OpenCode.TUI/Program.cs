using OpenCode.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.AI;
using OpenCode.AgentFramework.Abstractions;
using OpenCode.AgentFramework.Executors;
using OpenCode.AgentFramework;
using OpenCode.Infrastructure.Mock;
using OpenCode.Infrastructure.AI;
using OpenCode.Infrastructure.Plugins;
using OpenCode.Core.Contracts;
using OpenCode.Core.Models;
using OpenCode.Infrastructure.Tools;
using OpenCode.Core.Utilities;
using OpenCode.Infrastructure.Lsp;
using OpenCode.Core.Lsp;
using OpenCode.Core.Services;
using OpenCode.Server;
using OpenCode.TUI.Services;
using Spectre.Console;
using Spectre.Console.Json;
using Microsoft.AspNetCore.Builder;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;

using Microsoft.Extensions.Hosting;
using OpenCode.Infrastructure.Extensions;
using Terminal.Gui;

namespace OpenCode.TUI;

public class Program
{
    public static async Task Main(string[] args)
    {
        var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        LoadEnv(projectRoot);

        var host = HostExtensions.CreateOpenCodeHost(args, projectRoot).Build();
        var serviceProvider = host.Services;

        // 加载配置
        var configService = serviceProvider.GetRequiredService<ConfigService>();
        await configService.LoadAsync();
        var config = configService.Config;

        // 启动 TUI
        AnsiConsole.Write(new FigletText("OpenCode.Net").Color(Spectre.Console.Color.Blue));
        
        var input = args.Length > 0 ? string.Join(" ", args).Trim().ToLower() : "";
        var isSetupCommand = input.StartsWith("auth") || input.StartsWith("config") || input.StartsWith("help") || input == "version";

        // 如果是 auth login 这种带有子命令的，也判定为设置命令
        if (args.Length > 0)
        {
            var firstArg = args[0].ToLower();
            if (firstArg == "auth" || firstArg == "config" || firstArg == "help" || firstArg == "version")
            {
                isSetupCommand = true;
            }
        }

        var bootstrapService = serviceProvider.GetRequiredService<BootstrapService>();
        await AnsiConsole.Status()
            .StartAsync(isSetupCommand ? "正在准备环境..." : "正在启动 AI 编程助手...", async ctx => 
            {
                if (isSetupCommand)
                {
                    await bootstrapService.LightBootstrapAsync(progress => ctx.Status(progress));
                }
                else
                {
                    await bootstrapService.FullBootstrapAsync(progress => ctx.Status(progress));
                }
            });

        if (!isSetupCommand)
        {
            AnsiConsole.MarkupLine("[green]AI 编程助手已就绪。[/]");
        }

        // 启动 Server (仅在交互模式或运行模式下)
        if (!isSetupCommand)
        {
            _ = Task.Run(() => {
                var serverBuilder = WebApplication.CreateBuilder();
                serverBuilder.Services.AddCors();
                // 共享服务
                foreach (var descriptor in serviceProvider.GetRequiredService<IServiceCollection>()) 
                    serverBuilder.Services.Add(descriptor);
                var serverApp = serverBuilder.Build();
                serverApp.UseCors(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
                OpenCode.Server.ServerExtensions.MapOpenCodeRoutes(serverApp);
                serverApp.Run("http://localhost:4096");
            });
        }
        
        var titleService = serviceProvider.GetRequiredService<TitleService>();
        var summaryService = serviceProvider.GetRequiredService<SummaryService>();
        var tui = serviceProvider.GetRequiredService<TuiManager>();
        
        var commandProcessor = serviceProvider.GetRequiredService<ICommandProcessor>();
        var defaultModel = config.Model ?? Environment.GetEnvironmentVariable("OPENAI_MODEL");
        
        // 仅在非设置命令时显示模型连接状态
        if (!isSetupCommand)
        {
            if (string.IsNullOrEmpty(defaultModel))
            {
                AnsiConsole.MarkupLine("[yellow]提示: 尚未配置默认模型。[/]");
                AnsiConsole.MarkupLine("[grey]请运行 'config set model <provider>/<model>' 进行设置，例如 'config set model openai/gpt-4o'。[/]");
            }
            else
            {
                // 检查模型配置状态
                var providerId = defaultModel.Split('/')[0];
                 ProviderConfig? providerConfig = null;
                 config.Providers?.TryGetValue(providerId, out providerConfig);
                 var envKey = $"{providerId.ToUpper()}_API_KEY";
                 var apiKey = providerConfig?.ApiKey ?? Environment.GetEnvironmentVariable(envKey);
                 
                 if (string.IsNullOrEmpty(apiKey))
                 {
                     var authService = serviceProvider.GetRequiredService<AuthService>();
                     var authInfo = await authService.GetAsync(providerId);
                     if (authInfo is ApiAuthInfo apiAuth)
                     {
                         apiKey = apiAuth.Key;
                     }
                 }
                 
                 if (string.IsNullOrEmpty(apiKey))
                 {
                     AnsiConsole.MarkupLine($"[yellow]提示: 默认模型 {defaultModel} 尚未配置 API Key。[/]");
                     AnsiConsole.MarkupLine("[grey]请运行 'auth login' 指令进行配置，或在 .env 文件中设置 API Key。[/]");
                 }
                 else
                 {
                     AnsiConsole.MarkupLine($"[green]已连接到模型: {defaultModel}[/]");
                 }
            }
        }

        // 处理非交互式命令 (run 模式)
        if (args.Length > 0)
        {
            var rawInput = string.Join(" ", args);
            
            // 先尝试作为 TUI 指令处理
            var dummyContext = new CommandContext 
            { 
                ServiceProvider = serviceProvider,
                ProjectRoot = projectRoot,
                CurrentSessionId = "cli_session",
                Tui = tui,
                SetSessionId = _ => {},
                SetFirstMessage = _ => {},
                SetShowDetails = _ => {},
                SetShowThinking = _ => {},
                SetShowTimestamps = _ => {}
            };

            if (await commandProcessor.ProcessCommandAsync(input, dummyContext))
            {
                return;
            }

            // 如果不是指令，则作为 AI 聊天处理
            await RunOnceAsync(input, serviceProvider);
            return;
        }

        string currentSessionId = "session_" + Guid.NewGuid().ToString("N")[..8];
        bool isFirstMessage = true;
        bool showDetails = false;
        bool showThinking = true;
        bool showTimestamps = false;

        var commandContext = new CommandContext
        {
            CurrentSessionId = currentSessionId,
            IsFirstMessage = isFirstMessage,
            ShowDetails = showDetails,
            ShowThinking = showThinking,
            ShowTimestamps = showTimestamps,
            ProjectRoot = projectRoot,
            ServiceProvider = serviceProvider,
            Tui = tui,
            SetSessionId = (id) => currentSessionId = id,
            SetFirstMessage = (val) => isFirstMessage = val,
            SetShowDetails = (val) => showDetails = val,
            SetShowThinking = (val) => showThinking = val,
            SetShowTimestamps = (val) => showTimestamps = val
        };
        tui.SetSessionId(currentSessionId);

        tui.OnCommandSubmitted += async (promptText) => 
        {
            // 更新上下文状态
            commandContext.CurrentSessionId = currentSessionId;
            commandContext.IsFirstMessage = isFirstMessage;
            commandContext.ShowDetails = showDetails;
            commandContext.ShowThinking = showThinking;
            commandContext.ShowTimestamps = showTimestamps;

            // 处理指令
            if (await commandProcessor.ProcessCommandAsync(promptText, commandContext))
            {
                await tui.RefreshDataAsync();
                return;
            }

            if (isFirstMessage)
            {
                _ = Task.Run(async () => 
                {
                    try 
                    {
                        var title = await titleService.GenerateTitleAsync(promptText);
                        if (!string.IsNullOrEmpty(title))
                        {
                            var sessionService = serviceProvider.GetRequiredService<SessionService>();
                            await sessionService.RenameSessionAsync(currentSessionId, title);
                            
                            Application.MainLoop.Invoke(() => {
                                tui.SetSessionTitle(title);
                                tui.AddSystemMessage($"已生成会话标题: {title}");
                            });
                        }
                    } 
                    catch (Exception ex) 
                    {
                        Application.MainLoop.Invoke(() => {
                            tui.AddSystemMessage($"无法生成会话标题: {ex.Message}");
                        });
                    }
                });
                isFirstMessage = false;
            }

            var cts = new CancellationTokenSource();
            try 
            {
                var currentWorkflow = serviceProvider.GetRequiredService<Workflow>();
                tui.AddChatMessage("user", promptText);
                
                // 异步启动工作流
                _ = Task.Run(async () => {
                    _ = currentWorkflow.RunAsync("ThinkingExecutor", promptText, cts.Token);
                    await foreach (var output in currentWorkflow.Output.WithCancellation(cts.Token))
                    {
                        if (output is JsonNode node)
                        {
                            var status = node["status"]?.ToString();
                            if (status == "thinking") {
                                if (showThinking) {
                                    var msg = node["message"]?.ToString();
                                    tui.UpdateStatus(msg ?? "Thinking");
                                }
                            } else if (status == "thinking_stream") {
                                if (node["delta"] != null) {
                                    var delta = node["delta"]?.ToString();
                                    tui.AppendAssistantDelta(delta ?? "");
                                }
                            } else if (status == "completed") {
                                var answer = node["answer"]?.ToString();
                                tui.AddChatMessage("assistant", answer ?? "");
                                tui.UpdateStatus("Ready");
                                
                                var thinkingState = currentWorkflow.GetOrSetState("thinking", () => new ThinkingExecutor.ThinkingState());
                                try 
                                {
                                    var summary = await summaryService.GenerateSummaryAsync(thinkingState.History);
                                    if (!string.IsNullOrEmpty(summary))
                                    {
                                        tui.AddSystemMessage($"摘要: {summary}");
                                    }
                                } catch { }
                                break; 
                            } else if (status == "error") {
                                tui.AddChatMessage("system", $"错误: {node["message"]}");
                                tui.UpdateStatus("Error");
                                break;
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                tui.AddChatMessage("system", $"发生异常: {ex.Message}");
            }
        };

        // 启动后台数据刷新
        _ = Task.Run(async () => {
            while (true) {
                await tui.RefreshDataAsync();
                await Task.Delay(5000);
            }
        });

        tui.Run();
    }

    private static async Task RunOnceAsync(string input, IServiceProvider serviceProvider)
    {
        var configService = serviceProvider.GetRequiredService<ConfigService>();
        var config = configService.Config;
        var defaultModel = config.Model ?? Environment.GetEnvironmentVariable("OPENAI_MODEL");
        
        if (string.IsNullOrEmpty(defaultModel))
        {
            AnsiConsole.MarkupLine("[red]错误: 尚未配置默认模型。[/]");
            AnsiConsole.MarkupLine("[yellow]请运行 'config set model <provider>/<model>' 进行设置。[/]");
            return;
        }

        var providerId = defaultModel.Split('/')[0];
        ProviderConfig? providerConfig = null;
        config.Providers?.TryGetValue(providerId, out providerConfig);
        var apiKey = providerConfig?.ApiKey ?? Environment.GetEnvironmentVariable($"{providerId.ToUpper()}_API_KEY");

        if (string.IsNullOrEmpty(apiKey))
        {
            var authService = serviceProvider.GetRequiredService<AuthService>();
            var authInfo = await authService.GetAsync(providerId);
            if (authInfo is ApiAuthInfo apiAuth)
            {
                apiKey = apiAuth.Key;
            }
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            AnsiConsole.MarkupLine($"[red]错误: 无法执行 AI 指令。模型 {defaultModel} 的 API Key 尚未配置。[/]");
            AnsiConsole.MarkupLine("[yellow]请运行 'auth login' 进行配置。[/]");
            return;
        }

        var workflow = serviceProvider.GetRequiredService<Workflow>();
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)); // 5分钟超时
        
        AnsiConsole.MarkupLine($"[bold blue]正在执行 AI 指令:[/] {input}");
        
        try 
        {
            _ = workflow.RunAsync("ThinkingExecutor", input, cts.Token);

            await foreach (var output in workflow.Output.WithCancellation(cts.Token))
            {
                if (output is JsonNode node)
                {
                    var status = node["status"]?.ToString();
                    if (status == "thinking")
                    {
                        AnsiConsole.MarkupLine($"[grey]AI: {node["message"]}[/]");
                    }
                    else if (status == "thinking_stream")
                    {
                        var delta = node["delta"]?.ToString();
                        if (!string.IsNullOrEmpty(delta)) Console.Write(delta);
                    }
                    else if (status == "acting")
                    {
                        AnsiConsole.MarkupLine($"\n[blue]执行工具: {node["tool"]}[/]");
                    }
                    else if (status == "completed")
                    {
                        var answer = node["answer"]?.ToString();
                        AnsiConsole.MarkupLine($"\n[green]AI 回复: {answer}[/]");
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
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("\n[red]错误: 指令执行超时。[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[red]运行时错误: {ex.Message}[/]");
        }
    }

    private static void LoadEnv(string root)
    {
        var envPath = Path.Combine(root, ".env");
        if (File.Exists(envPath))
        {
            foreach (var line in File.ReadAllLines(envPath))
            {
                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                {
                    Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
                }
            }
        }
    }
}
