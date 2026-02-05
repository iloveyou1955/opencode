using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using OpenCode.Core.Attributes;

namespace OpenCode.Core.Services;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class Scheduler : IDisposable
{
    private readonly ILogger<Scheduler> _logger;
    private readonly Dictionary<string, Timer> _timers = new();
    private readonly Dictionary<string, Func<Task>> _tasks = new();
    private bool _disposed;

    public Scheduler(ILogger<Scheduler> logger)
    {
        _logger = logger;
    }

    public void Register(string id, TimeSpan interval, Func<Task> task)
    {
        lock (_timers)
        {
            if (_timers.TryGetValue(id, out var existingTimer))
            {
                existingTimer.Dispose();
            }

            _tasks[id] = task;
            
            // 立即运行一次
            _ = RunTaskAsync(id, task);

            var timer = new Timer(_ => 
            {
                _ = RunTaskAsync(id, task);
            }, null, interval, interval);

            _timers[id] = timer;
        }
    }

    private async Task RunTaskAsync(string id, Func<Task> task)
    {
        try
        {
            _logger.LogInformation("Executing scheduled task: {TaskId}", id);
            await task();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled task {TaskId} failed", id);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_timers)
        {
            foreach (var timer in _timers.Values)
            {
                timer.Dispose();
            }
            _timers.Clear();
            _tasks.Clear();
        }
    }
}
