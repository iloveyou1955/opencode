using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenCode.Core.Contracts;
using OpenCode.Core.Attributes;

namespace OpenCode.Core.Services;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class BootstrapService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BootstrapService> _logger;

    public BootstrapService(IServiceProvider serviceProvider, ILogger<BootstrapService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task BootstrapAsync(Action<string>? onProgress = null)
    {
        await FullBootstrapAsync(onProgress);
    }

    public async Task FullBootstrapAsync(Action<string>? onProgress = null)
    {
        var projectContext = _serviceProvider.GetRequiredService<IProjectContext>();
        onProgress?.Invoke($"正在初始化 OpenCode ({projectContext.Directory})...");
        _logger.LogDebug("Bootstrapping OpenCode in {Directory}", projectContext.Directory);

        // 1. 初始化 VCS
        onProgress?.Invoke("初始化 VCS...");
        var vcs = _serviceProvider.GetRequiredService<VcsService>();
        await vcs.InitializeAsync();

        // 2. 初始化 MCP
        onProgress?.Invoke("初始化 MCP...");
        var mcp = _serviceProvider.GetRequiredService<McpService>();
        await mcp.InitializeAsync();

        // 3. 初始化文件监听
        onProgress?.Invoke("启动文件监听...");
        var watcher = _serviceProvider.GetRequiredService<WatcherService>();
        watcher.Start();

        // 4. 确保格式化服务已加载
        onProgress?.Invoke("加载格式化服务...");
        _ = _serviceProvider.GetRequiredService<FormatService>();

        // 5. 初始化插件
        onProgress?.Invoke("初始化插件系统...");
        var pluginService = _serviceProvider.GetRequiredService<PluginService>();

        // 6. 初始化分享服务
        onProgress?.Invoke("初始化分享服务...");
        var shareService = _serviceProvider.GetRequiredService<ShareService>();
        shareService.Initialize();

        // 7. 启动自动归档检查
        _ = Task.Run(async () =>
        {
            var archiveService = _serviceProvider.GetRequiredService<ArchiveService>();
            while (true)
            {
                try
                {
                    await archiveService.AutoArchiveAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in auto archive task");
                }
                await Task.Delay(TimeSpan.FromHours(24));
            }
        });

        onProgress?.Invoke("启动完成");
        _logger.LogDebug("Bootstrap completed.");
    }

    public async Task LightBootstrapAsync(Action<string>? onProgress = null)
    {
        var projectContext = _serviceProvider.GetRequiredService<IProjectContext>();
        onProgress?.Invoke($"正在初始化环境 ({projectContext.Directory})...");
        
        // 仅初始化最基础的服务
        onProgress?.Invoke("初始化 VCS...");
        var vcs = _serviceProvider.GetRequiredService<VcsService>();
        await vcs.InitializeAsync();

        onProgress?.Invoke("初始化插件系统...");
        _ = _serviceProvider.GetRequiredService<PluginService>();

        onProgress?.Invoke("准备就绪");
    }
}
