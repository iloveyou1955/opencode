using OpenCode.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.Core.Services;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class TokenService
{
    private const double CharsPerToken = 3.8;

    public int Estimate(string? input)
    {
        if (string.IsNullOrEmpty(input)) return 0;
        return (int)Math.Max(1, Math.Ceiling(input.Length / CharsPerToken));
    }

    public int Estimate(IEnumerable<string> inputs)
    {
        return inputs.Sum(i => Estimate(i));
    }
}
