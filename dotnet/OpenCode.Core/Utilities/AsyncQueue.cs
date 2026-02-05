using System.Collections.Concurrent;

namespace OpenCode.Core.Utilities;

public class AsyncQueue<T>
{
    private readonly ConcurrentQueue<T> _queue = new();
    private readonly ConcurrentQueue<TaskCompletionSource<T>> _waiting = new();

    public void Push(T item)
    {
        if (_waiting.TryDequeue(out var tcs))
        {
            tcs.SetResult(item);
        }
        else
        {
            _queue.Enqueue(item);
        }
    }

    public async Task<T> NextAsync(CancellationToken ct = default)
    {
        if (_queue.TryDequeue(out var item))
        {
            return item;
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (ct.Register(() => tcs.TrySetCanceled()))
        {
            _waiting.Enqueue(tcs);
            return await tcs.Task;
        }
    }
}
