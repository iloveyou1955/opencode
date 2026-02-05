using Microsoft.Extensions.Logging;
using OpenCode.Core.Models;

using OpenCode.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.Core.Services;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class LogService
{
    private readonly BusService _bus;

    public LogService(BusService bus)
    {
        _bus = bus;
    }

    public void Log(string level, string message, object? metadata = null)
    {
        _bus.Publish("log.stream", new
        {
            Level = level,
            Message = message,
            Metadata = metadata,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });
    }
}
