using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenCode.Core.Contracts;

using OpenCode.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.Core.Services;

public class PtyInfo
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string[] Args { get; set; } = Array.Empty<string>();
    public string Cwd { get; set; } = string.Empty;
    public string Status { get; set; } = "running";
    public int Pid { get; set; }
}

[ServiceRegistration(ServiceLifetime.Singleton)]
public class PtyService
{
    private readonly IProjectContext _projectContext;
    private readonly BusService _bus;
    private readonly ILogger<PtyService> _logger;
    private readonly Dictionary<string, (PtyInfo Info, Process Process)> _sessions = new();

    public PtyService(IProjectContext projectContext, BusService bus, ILogger<PtyService> logger)
    {
        _projectContext = projectContext;
        _bus = bus;
        _logger = logger;
    }

    public List<PtyInfo> List() => _sessions.Values.Select(v => v.Info).ToList();

    public PtyInfo? Get(string id) => _sessions.TryGetValue(id, out var session) ? session.Info : null;

    public async Task<PtyInfo> CreateAsync(string command, string[] args, string? cwd = null, string? title = null)
    {
        var id = Guid.NewGuid().ToString().Substring(0, 8);
        var finalCwd = cwd ?? _projectContext.Directory;
        
        var psi = new ProcessStartInfo(command)
        {
            WorkingDirectory = finalCwd,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        var process = Process.Start(psi) ?? throw new Exception("Failed to start process");
        
        var info = new PtyInfo
        {
            Id = id,
            Title = title ?? $"Terminal {id}",
            Command = command,
            Args = args,
            Cwd = finalCwd,
            Pid = process.Id
        };

        _sessions[id] = (info, process);

        // 异步读取输出并推送到总线
        _ = Task.Run(async () => {
            var buffer = new char[4096];
            while (!process.HasExited)
            {
                var count = await process.StandardOutput.ReadAsync(buffer, 0, buffer.Length);
                if (count > 0)
                {
                    var data = new string(buffer, 0, count);
                    _bus.Publish("pty.data", new { id, data });
                }
            }
            
            info.Status = "exited";
            _bus.Publish("pty.exited", new { id, exitCode = process.ExitCode });
            _sessions.Remove(id);
        });

        _bus.Publish("pty.created", new { info });
        return info;
    }

    public async Task WriteAsync(string id, string data)
    {
        if (_sessions.TryGetValue(id, out var session))
        {
            await session.Process.StandardInput.WriteAsync(data);
            await session.Process.StandardInput.FlushAsync();
        }
    }

    public void Kill(string id)
    {
        if (_sessions.TryGetValue(id, out var session))
        {
            session.Process.Kill(true);
            _sessions.Remove(id);
        }
    }
}
