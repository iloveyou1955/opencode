using System.Text.Json.Nodes;
using OpenCode.Core.Lsp;

namespace OpenCode.Core.Contracts;

/// <summary>
/// 定义工具的通用接口
/// </summary>
public interface ITool
{
    /// <summary>
    /// 工具名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 工具描述
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 输入参数的 JSON Schema 描述
    /// </summary>
    string InputSchema { get; }

    /// <summary>
    /// 异步执行工具
    /// </summary>
    /// <param name="args">输入参数（JSON 对象）</param>
    /// <param name="context">工具执行上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>执行结果（字符串形式，通常是 JSON 或文本）</returns>
    ValueTask<string> ExecuteAsync(JsonObject args, IToolContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// 工具执行上下文
/// </summary>
public interface IToolContext
{
    /// <summary>
    /// 会话 ID
    /// </summary>
    string SessionId { get; }

    /// <summary>
    /// 消息 ID
    /// </summary>
    string MessageId { get; }

    /// <summary>
    /// LSP 管理器
    /// </summary>
    ILspManager Lsp { get; }

    /// <summary>
    /// 请求权限
    /// </summary>
    /// <param name="tool">工具名称</param>
    /// <param name="pattern">权限模式</param>
    /// <returns>是否授权</returns>
    Task<bool> RequestPermissionAsync(string tool, string? pattern = null);
}

public interface IPermissionService
{
    Task<PermissionAction> CheckPermissionAsync(string sessionId, string tool, string? pattern = null);
    Task<PermissionResponse> AskAsync(PermissionInfo info);
    void Respond(string permissionId, PermissionResponse response);
    List<PermissionInfo> GetPending();
}

public class PermissionInfo
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Pattern { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string? CallId { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object> Metadata { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum PermissionAction
{
    Allow,
    Deny,
    Ask
}

public enum PermissionResponse
{
    Once,
    Always,
    Reject
}