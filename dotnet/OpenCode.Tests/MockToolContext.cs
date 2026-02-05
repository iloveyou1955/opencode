using OpenCode.Core.Contracts;
using OpenCode.Core.Lsp;

namespace OpenCode.Tests;

public class MockToolContext : IToolContext
{
    public string SessionId => "test-session";
    public string MessageId => "test-message";
    public ILspManager Lsp { get; set; } = null!;

    public Task<bool> RequestPermissionAsync(string tool, string? pattern = null)
    {
        return Task.FromResult(true); // Default allow for tests
    }
}
