using Microsoft.Extensions.Logging;
using OpenCode.Core.Models;

namespace OpenCode.Core.Services;

public class AcpService
{
    private readonly ILogger<AcpService> _logger;
    private readonly BusService _bus;

    public AcpService(BusService bus, ILogger<AcpService> logger)
    {
        _bus = bus;
        _logger = logger;

        // Subscribe to internal events to push to ACP clients
        _bus.SubscribeAll(HandleBusEvent);
    }

    public AcpInitializeResponse Initialize(AcpInitializeRequest request)
    {
        _logger.LogInformation("ACP Initialize from {Client}", request.ClientInfo.Name);

        return new AcpInitializeResponse(
            ProtocolVersion: 1,
            AgentCapabilities: new AcpAgentCapabilities(
                LoadSession: true,
                McpCapabilities: new AcpMcpCapabilities(true, true),
                PromptCapabilities: new AcpPromptCapabilities(true, true),
                SessionCapabilities: new AcpSessionCapabilities(new { }, new { }, new { })
            ),
            AuthMethods: new List<AcpAuthMethod> { 
                new("opencode-token", "OpenCode Token", "Use your OpenCode API token") 
            },
            AgentInfo: new AcpAgentInfo("OpenCode-DotNet", "1.0.0")
        );
    }

    private async Task HandleBusEvent(BusEvent e)
    {
        // Translate internal events to ACP updates
        // This would normally find active ACP connections for the sessionId and push
        switch (e.Type)
        {
            case "thinking_stream":
                // Send agent_message_chunk
                break;
            case "tool_call":
                // Send tool_call
                break;
        }
        await Task.CompletedTask;
    }
}
