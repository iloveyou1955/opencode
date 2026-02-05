using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace OpenCode.AgentFramework.Abstractions;

public class Workflow : IWorkflowContext
{
    public string WorkflowId { get; } = "wf_" + Guid.NewGuid().ToString("N")[..8];
    private readonly Dictionary<string, Executor> _executors = new();
    private readonly Channel<(string TargetId, object Message)> _messageQueue = Channel.CreateUnbounded<(string, object)>();
    private readonly Channel<object> _outputChannel = Channel.CreateUnbounded<object>();
    private readonly ILogger<Workflow>? _logger;

    private readonly Dictionary<string, object> _state = new();

    public T GetOrSetState<T>(string key, Func<T> factory)
    {
        if (_state.TryGetValue(key, out var value))
        {
            return (T)value;
        }
        var newValue = factory();
        _state[key] = newValue!;
        return newValue;
    }

    public Workflow(ILogger<Workflow>? logger = null)
    {
        _logger = logger;
    }

    public void AddExecutor(Executor executor)
    {
        _executors[executor.Id] = executor;
    }

    public async Task RunAsync(string startExecutorId, object initialInput, CancellationToken ct)
    {
        // 初始消息
        await _messageQueue.Writer.WriteAsync((startExecutorId, initialInput), ct);

        // 消息处理循环
        try
        {
            await foreach (var (targetId, message) in _messageQueue.Reader.ReadAllAsync(ct))
            {
                if (_executors.TryGetValue(targetId, out var executor))
                {
                    // 并发执行 Executor
                    // 使用 Task.Run 确保不阻塞消息循环
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await executor.ProcessAsync(message, this, ct);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Error in executor {TargetId}", targetId);
                            // 发送错误到 Output Channel，以便 TUI 显示
                            try 
                            {
                                await YieldOutputAsync(new { status = "error", message = $"Executor {targetId} failed: {ex.Message}" }, ct);
                            }
                            catch { /* Ignore channel write errors during failure */ }
                        }
                    }, ct);
                }
                else
                {
                    Console.WriteLine($"Executor {targetId} not found.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        finally
        {
            _outputChannel.Writer.TryComplete();
        }
    }

    public async Task SendMessageAsync<T>(string targetExecutorId, T message, CancellationToken cancellationToken = default)
    {
        await _messageQueue.Writer.WriteAsync((targetExecutorId, message!), cancellationToken);
    }

    public async Task YieldOutputAsync<T>(T output, CancellationToken cancellationToken = default)
    {
        await _outputChannel.Writer.WriteAsync(output!, cancellationToken);
    }

    public IAsyncEnumerable<object> Output => _outputChannel.Reader.ReadAllAsync();
}