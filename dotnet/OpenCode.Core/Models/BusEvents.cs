namespace OpenCode.Core.Models;

public static class BusEvents
{
    public const string SessionCreated = "session.created";
    public const string SessionUpdated = "session.updated";
    public const string SessionDeleted = "session.deleted";
    public const string SessionError = "session.error";
    
    public const string MessageUpdated = "message.updated";
    public const string MessageRemoved = "message.removed";
    
    public const string PartUpdated = "part.updated";
    public const string PartRemoved = "part.removed";
    
    public const string ThinkingStream = "thinking_stream";
    public const string ToolCall = "tool_call";
    public const string ToolResult = "tool_result";
    
    public const string ConfigUpdated = "config.updated";
    public const string PermissionRequested = "permission.requested";
    public const string PermissionResponded = "permission.responded";
}
