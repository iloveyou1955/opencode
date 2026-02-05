using OpenCode.Core.Models;
using Microsoft.AspNetCore.Mvc;
using OpenCode.Core.Services;
using OpenCode.Core.Contracts;
using OpenCode.Core.Lsp;
using OpenCode.Core.Utilities;
using OpenCode.AgentFramework.Abstractions;
using OpenCode.Infrastructure.AI;
using OpenCode.Infrastructure.Tools;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using System.Runtime.Versioning;

namespace OpenCode.Server;

public static class ServerExtensions
{
    [SupportedOSPlatform("windows")]
    public static void AddOpenCodeServices(this IServiceCollection services)
    {
        services.AddSingleton<ISecureStorage, SecureStorageService>();
        services.AddSingleton<AuthService>();
        services.AddSingleton<WorktreeService>();
        services.AddSingleton<AcpService>();
        services.AddSingleton<StatsService>();
        services.AddSingleton<TuiControlService>();
        services.AddSingleton<WorktreeTool>();
        services.AddSingleton<ExternalDirectoryTool>();
        services.AddSingleton<InvalidTool>();
    }

    public static void MapOpenCodeRoutes(this IEndpointRouteBuilder app)
    {
        // Global
        app.MapGet("/global", () => new { version = "0.0.1", status = "ok" });

        // Config
        app.MapGet("/config", async (ConfigService configService) => {
            return Results.Ok(configService.Config);
        });

        // VCS
        app.MapGet("/vcs", (VcsService vcsService) => {
            return Results.Ok(new { branch = vcsService.Branch });
        });

        // Snapshot
        app.MapPost("/snapshot", async (SnapshotService snapshotService) => {
            var hash = await snapshotService.TrackAsync();
            return Results.Ok(new { hash });
        });

        app.MapPost("/snapshot/restore", async ([FromBody] JsonObject body, SnapshotService snapshotService) => {
            var hash = body["hash"]?.ToString();
            if (string.IsNullOrEmpty(hash)) return Results.BadRequest("Hash is required");
            var success = await snapshotService.RestoreAsync(hash);
            return Results.Ok(new { success });
        });

        // PTY
        app.MapGet("/pty", (PtyService ptyService) => {
            return Results.Ok(ptyService.List());
        });

        app.MapPost("/pty", async ([FromBody] JsonObject body, PtyService ptyService) => {
            var command = body["command"]?.ToString() ?? "cmd.exe";
            var argsNode = body["args"] as JsonArray;
            var args = argsNode?.Select(n => n?.ToString() ?? "").ToArray() ?? Array.Empty<string>();
            var cwd = body["cwd"]?.ToString();
            var title = body["title"]?.ToString();
            
            var info = await ptyService.CreateAsync(command, args, cwd, title);
            return Results.Ok(info);
        });

        // Storage
        app.MapGet("/storage/{*key}", async (string key, StorageService storageService) => {
            var keyParts = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var result = await storageService.ReadAsync<object>(keyParts);
            return result != null ? Results.Ok(result) : Results.NotFound();
        });

        app.MapPost("/storage/{*key}", async (string key, [FromBody] JsonObject body, StorageService storageService) => {
            var keyParts = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
            await storageService.WriteAsync(body, keyParts);
            return Results.Ok();
        });

        app.MapDelete("/storage/{*key}", async (string key, StorageService storageService) => {
            var keyParts = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
            await storageService.RemoveAsync(keyParts);
            return Results.Ok();
        });

        // Permission
        app.MapGet("/permission/pending", (IPermissionService permissionService) => {
            if (permissionService is PermissionService ps)
            {
                return Results.Ok(ps.GetPending());
            }
            return Results.Ok(new List<PermissionInfo>());
        });

        app.MapPost("/permission/respond", async ([FromBody] JsonObject body, IPermissionService permissionService) => {
            var id = body["id"]?.ToString();
            var responseStr = body["response"]?.ToString();
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(responseStr)) return Results.BadRequest("ID and Response are required");

            if (Enum.TryParse<PermissionResponse>(responseStr, true, out var response))
            {
                if (permissionService is PermissionService ps)
                {
                    ps.Respond(id, response);
                    return Results.Ok();
                }
            }
            return Results.BadRequest("Invalid response");
        });

        // MCP
        app.MapGet("/mcp/tools", async (McpService mcpService) => {
            var tools = await mcpService.ListToolsAsync();
            return Results.Ok(tools);
        });

        app.MapPost("/mcp/call", async ([FromBody] JsonObject body, McpService mcpService) => {
            var toolName = body["tool"]?.ToString();
            var args = body["args"] as JsonObject ?? new JsonObject();
            if (string.IsNullOrEmpty(toolName)) return Results.BadRequest("Tool name is required");

            try
            {
                var result = await mcpService.CallToolAsync(toolName, args);
                return Results.Ok(new { result });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // Experimental
        var experimental = app.MapGroup("/experimental");
        experimental.MapGet("/status", () => Results.Ok(Flag.GetStatus()));
        experimental.MapPost("/toggle", (JsonObject body) => {
            var key = body["key"]?.ToString();
            var value = body["value"]?.GetValue<bool>() ?? false;
            if (!string.IsNullOrEmpty(key))
            {
                var envKey = "OPENCODE_" + key.ToUpper();
                Flag.SetTruthy(envKey, value);
            }
            return Results.Ok(Flag.GetStatus());
        });

        // Auth
        app.MapGet("/auth", async (AuthService authService) => {
            var all = await authService.AllAsync();
            return Results.Ok(all);
        });

        app.MapGet("/auth/{providerId}", async (string providerId, AuthService authService) => {
            var info = await authService.GetAsync(providerId);
            return info != null ? Results.Ok(info) : Results.NotFound();
        });

        app.MapPost("/auth/{providerId}", async (string providerId, [FromBody] JsonObject body, AuthService authService) => {
            AuthInfo? info = null;
            var type = body["type"]?.ToString();
            
            if (type == "api") {
                info = new ApiAuthInfo(body["key"]?.ToString() ?? "");
            } else if (type == "oauth") {
                info = new OAuthAuthInfo(
                    body["refresh"]?.ToString() ?? "",
                    body["access"]?.ToString() ?? "",
                    body["expires"]?.GetValue<long>() ?? 0,
                    body["accountId"]?.ToString(),
                    body["enterpriseUrl"]?.ToString()
                );
            } else if (type == "wellknown") {
                info = new WellKnownAuthInfo(
                    body["key"]?.ToString() ?? "",
                    body["token"]?.ToString() ?? ""
                );
            }

            if (info != null) {
                await authService.SetAsync(providerId, info);
                return Results.Ok();
            }
            return Results.BadRequest("Invalid auth type or data");
        });

        app.MapDelete("/auth/{providerId}", async (string providerId, AuthService authService) => {
            await authService.RemoveAsync(providerId);
            return Results.Ok();
        });

        // Worktree
        app.MapGet("/worktree", async (WorktreeService worktreeService) => {
            var list = await worktreeService.ListAsync();
            return Results.Ok(list);
        });

        app.MapPost("/worktree", async ([FromBody] JsonObject body, WorktreeService worktreeService) => {
            var name = body["name"]?.ToString();
            var startCommand = body["startCommand"]?.ToString();
            var info = await worktreeService.CreateAsync(name, startCommand);
            return Results.Ok(info);
        });

        app.MapDelete("/worktree", async ([FromBody] JsonObject body, WorktreeService worktreeService) => {
            var directory = body["directory"]?.ToString();
            if (string.IsNullOrEmpty(directory)) return Results.BadRequest("Directory is required");
            await worktreeService.RemoveAsync(directory);
            return Results.Ok();
        });

        app.MapPost("/worktree/reset", async ([FromBody] JsonObject body, WorktreeService worktreeService) => {
            var directory = body["directory"]?.ToString();
            if (string.IsNullOrEmpty(directory)) return Results.BadRequest("Directory is required");
            await worktreeService.ResetAsync(directory);
            return Results.Ok();
        });

        // ACP
        app.MapPost("/acp/initialize", ([FromBody] AcpInitializeRequest request, AcpService acpService) => {
            var response = acpService.Initialize(request);
            return Results.Ok(response);
        });

        // Project
        app.MapGet("/project/current", (IProjectContext projectContext) => {
            return Results.Ok(new {
                directory = projectContext.Directory,
                worktree = projectContext.Worktree
            });
        });

        // Agent
        app.MapGet("/agent", async (IAgentConfigurationProvider agentProvider) => {
            // 这里简单返回所有 agent，TS 版有更复杂的过滤
            return Results.Ok(new[] { "ThinkingExecutor" });
        });

        // Session
        app.MapGet("/session", async (SessionService sessionService) => {
            var sessions = await sessionService.ListSessionsAsync();
            return Results.Ok(sessions);
        });

        app.MapGet("/session/{sessionId}", async (string sessionId, SessionService sessionService) => {
            var history = await sessionService.LoadHistoryAsync(sessionId);
            return Results.Ok(history);
        });

        app.MapPost("/session", async ([FromBody] JsonObject body, Workflow workflow) => {
            var prompt = body["prompt"]?.ToString();
            if (string.IsNullOrEmpty(prompt)) return Results.BadRequest("Prompt is required");

            // 异步运行，TS 版这里会返回流或初始状态
            _ = workflow.RunAsync("ThinkingExecutor", prompt, CancellationToken.None);
            return Results.Accepted();
        });

        // Event (SSE)
        app.MapGet("/event", async (HttpContext context, BusService bus) => {
            context.Response.ContentType = "text/event-stream";
            var writer = new StreamWriter(context.Response.Body);

            await writer.WriteAsync("data: {\"type\": \"server.connected\"}\n\n");
            await writer.FlushAsync();

            using var unsub = bus.SubscribeAll(async (e) => {
                var json = System.Text.Json.JsonSerializer.Serialize(e);
                await writer.WriteAsync($"data: {json}\n\n");
                await writer.FlushAsync();
            });

            // 保持连接直到客户端断开
            var tcs = new TaskCompletionSource();
            context.RequestAborted.Register(() => tcs.SetResult());
            await tcs.Task;
        });

        // Stats
        app.MapGet("/stats", async (int? days, string? project, StatsService statsService) => {
            var stats = await statsService.AggregateAsync(days, project);
            return Results.Ok(stats);
        });

        // Path
        app.MapGet("/path", (IProjectContext projectContext) => {
            return Results.Ok(new {
                directory = projectContext.Directory,
                worktree = projectContext.Worktree,
                home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            });
        });

        // Find
        var find = app.MapGroup("/find");
        find.MapGet("/file", async (string q, [FromQuery] string? root, [FromQuery] string? glob, IProjectContext projectContext) => {
            var searchRoot = root ?? projectContext.Directory;
            var results = await Filesystem.GlobUpAsync(q, searchRoot);
            return Results.Ok(results);
        });

        find.MapGet("/symbol", async (string q, ILspManager lsp, CancellationToken ct) => {
            var symbols = await lsp.SearchSymbolsAsync(q, ct);
            return Results.Ok(symbols);
        });

        // TUI
        var tui = app.MapGroup("/tui");
        
        tui.MapPost("/append-prompt", (JsonObject body, BusService bus) => {
            bus.Publish("tui.prompt.append", body);
            return Results.Ok(true);
        });

        tui.MapPost("/open-help", (BusService bus) => {
            bus.Publish("tui.command.execute", new { command = "help.show" });
            return Results.Ok(true);
        });

        tui.MapPost("/open-sessions", (BusService bus) => {
            bus.Publish("tui.command.execute", new { command = "session.list" });
            return Results.Ok(true);
        });

        tui.MapPost("/open-themes", (BusService bus) => {
            bus.Publish("tui.command.execute", new { command = "theme.list" });
            return Results.Ok(true);
        });

        tui.MapPost("/open-models", (BusService bus) => {
            bus.Publish("tui.command.execute", new { command = "model.list" });
            return Results.Ok(true);
        });

        tui.MapPost("/submit-prompt", (BusService bus) => {
            bus.Publish("tui.command.execute", new { command = "prompt.submit" });
            return Results.Ok(true);
        });

        tui.MapPost("/clear-prompt", (BusService bus) => {
            bus.Publish("tui.command.execute", new { command = "prompt.clear" });
            return Results.Ok(true);
        });

        tui.MapPost("/execute-command", (JsonObject body, BusService bus) => {
            var command = body["command"]?.ToString();
            var mappedCommand = command switch {
                "session_new" => "session.new",
                "session_share" => "session.share",
                "session_interrupt" => "session.interrupt",
                "session_compact" => "session.compact",
                "agent_cycle" => "agent.cycle",
                _ => command
            };
            bus.Publish("tui.command.execute", new { command = mappedCommand });
            return Results.Ok(true);
        });

        tui.MapPost("/show-toast", (JsonObject body, BusService bus) => {
            bus.Publish("tui.toast.show", body);
            return Results.Ok(true);
        });

        tui.MapPost("/publish", (JsonObject body, BusService bus) => {
            var type = body["type"]?.ToString();
            var props = body["properties"] as JsonObject;
            if (!string.IsNullOrEmpty(type)) bus.Publish(type, props ?? new JsonObject());
            return Results.Ok(true);
        });

        tui.MapPost("/select-session", (JsonObject body, BusService bus) => {
            var sessionId = body["sessionId"]?.ToString();
            bus.Publish("tui.session.select", new { sessionId });
            return Results.Ok(true);
        });

        var control = tui.MapGroup("/control");
        
        control.MapGet("/next", async (TuiControlService tuiControl, CancellationToken ct) => {
            var req = await tuiControl.GetNextRequestAsync(ct);
            return Results.Ok(req);
        });

        control.MapPost("/response", (JsonObject body, TuiControlService tuiControl) => {
            tuiControl.PushResponse(body);
            return Results.Ok(true);
        });
    }
}
