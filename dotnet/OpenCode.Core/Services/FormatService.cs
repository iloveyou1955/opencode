using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using OpenCode.Core.Contracts;

using OpenCode.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.Core.Services;

public class FormatterInfo
{
    public required string Name { get; init; }
    public required string[] Command { get; init; }
    public required string[] Extensions { get; init; }
    public Func<IProjectContext, Task<bool>> Enabled { get; init; } = _ => Task.FromResult(true);
}

[ServiceRegistration(ServiceLifetime.Singleton)]
public class FormatService
{
    private readonly IProjectContext _projectContext;
    private readonly ILogger<FormatService> _logger;
    private readonly List<FormatterInfo> _formatters = new();

    public FormatService(IProjectContext projectContext, BusService bus, ILogger<FormatService> logger)
    {
        _projectContext = projectContext;
        _logger = logger;

        // Subscribe to file edited event
        bus.Subscribe("file.edited", async (e) => {
            if (e.Properties is JsonObject props && props.TryGetPropertyValue("file", out var fileNode))
            {
                var filePath = fileNode?.ToString();
                if (!string.IsNullOrEmpty(filePath))
                {
                    await FormatAsync(filePath);
                }
            }
        });

        // 初始化内置格式化器
        _formatters.Add(new FormatterInfo
        {
            Name = "dotnet-format",
            Command = new[] { "dotnet", "format", "$FILE" },
            Extensions = new[] { ".cs", ".vb" },
            Enabled = async _ => await IsCommandAvailable("dotnet")
        });

        _formatters.Add(new FormatterInfo
        {
            Name = "prettier",
            Command = new[] { "npx", "prettier", "--write", "$FILE" },
            Extensions = new[] { ".js", ".ts", ".tsx", ".jsx", ".json", ".css", ".html", ".md" },
            Enabled = async ctx => {
                var pkgJson = Path.Combine(ctx.Directory, "package.json");
                return File.Exists(pkgJson) && (await File.ReadAllTextAsync(pkgJson)).Contains("prettier");
            }
        });

        _formatters.Add(new FormatterInfo
        {
            Name = "gofmt",
            Command = new[] { "gofmt", "-w", "$FILE" },
            Extensions = new[] { ".go" },
            Enabled = async _ => await IsCommandAvailable("gofmt")
        });

        _formatters.Add(new FormatterInfo
        {
            Name = "rustfmt",
            Command = new[] { "rustfmt", "$FILE" },
            Extensions = new[] { ".rs" },
            Enabled = async _ => await IsCommandAvailable("rustfmt")
        });

        _formatters.Add(new FormatterInfo
        {
            Name = "black",
            Command = new[] { "black", "$FILE" },
            Extensions = new[] { ".py" },
            Enabled = async _ => await IsCommandAvailable("black")
        });

        _formatters.Add(new FormatterInfo
        {
            Name = "clang-format",
            Command = new[] { "clang-format", "-i", "$FILE" },
            Extensions = new[] { ".cpp", ".c", ".h", ".hpp", ".cc" },
            Enabled = async _ => await IsCommandAvailable("clang-format")
        });
    }

    public async Task FormatAsync(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLower();
        var formatter = _formatters.FirstOrDefault(f => f.Extensions.Contains(ext));

        if (formatter == null) return;

        if (!await formatter.Enabled(_projectContext)) return;

        _logger.LogInformation("Formatting {File} using {Formatter}", filePath, formatter.Name);

        try
        {
            var args = formatter.Command.Select(a => a == "$FILE" ? filePath : a).ToList();
            var cmd = args[0];
            var cmdArgs = string.Join(" ", args.Skip(1));

            var psi = new ProcessStartInfo(cmd, cmdArgs)
            {
                WorkingDirectory = _projectContext.Directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync();
                if (process.ExitCode != 0)
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    _logger.LogWarning("Formatter {Name} failed: {Error}", formatter.Name, error);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running formatter {Name}", formatter.Name);
        }
    }

    private async Task<bool> IsCommandAvailable(string command)
    {
        try
        {
            var psi = new ProcessStartInfo(OperatingSystem.IsWindows() ? "where" : "which", command)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
