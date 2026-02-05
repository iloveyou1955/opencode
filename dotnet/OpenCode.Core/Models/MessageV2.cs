using System.Text.Json.Serialization;

namespace OpenCode.Core.Models;

public record MessageInfo(
    string Id,
    string Role,
    List<MessagePart> Parts,
    MessageMetadata Metadata
);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextPart), "text")]
[JsonDerivedType(typeof(ReasoningPart), "reasoning")]
[JsonDerivedType(typeof(ToolInvocationPart), "tool-invocation")]
[JsonDerivedType(typeof(FilePart), "file")]
public abstract record MessagePart([property: JsonIgnore] string Type);

public record TextPart(string Text) : MessagePart("text");

public record ReasoningPart(string Text, Dictionary<string, object>? ProviderMetadata = null) : MessagePart("reasoning");

public record ToolInvocationPart(ToolInvocation ToolInvocation) : MessagePart("tool-invocation");

public record ToolInvocation(
    string State, // "call", "partial-call", "result"
    string ToolCallId,
    string ToolName,
    object Args,
    string? Result = null,
    int? Step = null
);

public record FilePart(
    string MediaType,
    string Url,
    string? Filename = null
) : MessagePart("file");

public record MessageMetadata(
    long Created,
    string SessionId,
    long? Completed = null,
    string? ModelId = null,
    string? ProviderId = null,
    Dictionary<string, ToolMetadata>? Tool = null,
    double Cost = 0,
    TokenUsage? Tokens = null
);

public record TokenUsage(
    long Input,
    long Output,
    long Reasoning = 0,
    CacheUsage? Cache = null
);

public record CacheUsage(
    long Read,
    long Write
);

public record ToolMetadata(
    string Title,
    long Start,
    long End,
    string? Snapshot = null
);
