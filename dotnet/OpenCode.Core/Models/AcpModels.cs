using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace OpenCode.Core.Models;

public record AcpInitializeRequest(
    int ProtocolVersion,
    JsonNode? ClientCapabilities,
    AcpClientInfo ClientInfo
);

public record AcpClientInfo(string Name, string Version);

public record AcpInitializeResponse(
    int ProtocolVersion,
    AcpAgentCapabilities AgentCapabilities,
    List<AcpAuthMethod> AuthMethods,
    AcpAgentInfo AgentInfo
);

public record AcpAgentCapabilities(
    bool LoadSession,
    AcpMcpCapabilities McpCapabilities,
    AcpPromptCapabilities PromptCapabilities,
    AcpSessionCapabilities SessionCapabilities
);

public record AcpMcpCapabilities(bool Http, bool Sse);
public record AcpPromptCapabilities(bool EmbeddedContext, bool Image);
public record AcpSessionCapabilities(object Fork, object List, object Resume);

public record AcpAuthMethod(string Id, string Name, string Description);
public record AcpAgentInfo(string Name, string Version);

public record AcpSessionUpdate(
    string SessionId,
    AcpUpdate Update
);

public record AcpUpdate(
    [property: JsonPropertyName("sessionUpdate")] string Type, // "agent_message_chunk", "tool_call", etc.
    JsonNode? Content = null,
    string? ToolCallId = null,
    string? Title = null,
    string? Kind = null,
    string? Status = null
);
