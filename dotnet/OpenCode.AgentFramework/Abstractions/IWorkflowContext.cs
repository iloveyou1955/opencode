namespace OpenCode.AgentFramework.Abstractions;

public interface IWorkflowContext
{
    string WorkflowId { get; }
    Task SendMessageAsync<T>(string targetExecutorId, T message, CancellationToken cancellationToken = default);
    Task YieldOutputAsync<T>(T output, CancellationToken cancellationToken = default);
    T GetOrSetState<T>(string key, Func<T> factory);
}