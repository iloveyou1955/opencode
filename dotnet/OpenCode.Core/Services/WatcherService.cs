using Microsoft.Extensions.Logging;
using OpenCode.Core.Contracts;

using OpenCode.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.Core.Services;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class WatcherService : IDisposable
{
    private readonly string _projectRoot;
    private readonly BusService _bus;
    private readonly ILogger<WatcherService> _logger;
    private FileSystemWatcher? _watcher;
    private readonly HashSet<string> _ignoredPatterns = new() { ".git", "node_modules", "bin", "obj", ".opencode" };

    public WatcherService(IProjectContext projectContext, BusService bus, ILogger<WatcherService> logger)
    {
        _projectRoot = projectContext.Directory;
        _bus = bus;
        _logger = logger;
    }

    public void Start()
    {
        if (!Directory.Exists(_projectRoot)) return;

        _logger.LogDebug("Starting file watcher for {ProjectRoot}", _projectRoot);

        _watcher = new FileSystemWatcher(_projectRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };

        _watcher.Created += (s, e) => OnChanged(e.FullPath, "add");
        _watcher.Changed += (s, e) => OnChanged(e.FullPath, "change");
        _watcher.Deleted += (s, e) => OnChanged(e.FullPath, "unlink");
        _watcher.Renamed += (s, e) => {
            OnChanged(e.OldFullPath, "unlink");
            OnChanged(e.FullPath, "add");
        };
    }

    private void OnChanged(string fullPath, string eventType)
    {
        var relativePath = Path.GetRelativePath(_projectRoot, fullPath);
        
        // 简单忽略逻辑
        if (_ignoredPatterns.Any(p => relativePath.Contains(Path.DirectorySeparatorChar + p + Path.DirectorySeparatorChar) || relativePath.StartsWith(p + Path.DirectorySeparatorChar)))
        {
            return;
        }

        _logger.LogDebug("File {Event}: {Path}", eventType, relativePath);
        _ = _bus.PublishAsync("file.watcher.updated", new { file = fullPath, @event = eventType });
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
