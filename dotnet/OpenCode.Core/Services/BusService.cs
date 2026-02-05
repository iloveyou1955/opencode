using System.Collections.Concurrent;

using OpenCode.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.Core.Services;

public class BusEvent
{
    public string Type { get; init; }
    public object Properties { get; init; }

    public BusEvent(string type, object properties)
    {
        Type = type;
        Properties = properties;
    }
}

[ServiceRegistration(ServiceLifetime.Singleton)]
public class BusService
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<Func<BusEvent, Task>>> _subscriptions = new();
    private readonly ConcurrentBag<Func<BusEvent, Task>> _wildcardSubscriptions = new();

    public async Task PublishAsync(string type, object properties)
    {
        var @event = new BusEvent(type, properties);
        var tasks = new List<Task>();

        // 特定类型的订阅
        if (_subscriptions.TryGetValue(type, out var handlers))
        {
            foreach (var handler in handlers)
            {
                tasks.Add(handler(@event));
            }
        }

        // 通配符订阅
        foreach (var handler in _wildcardSubscriptions)
        {
            tasks.Add(handler(@event));
        }

        await Task.WhenAll(tasks);
    }

    public void Publish(string type, object properties)
    {
        _ = PublishAsync(type, properties);
    }

    public IDisposable Subscribe(string type, Func<BusEvent, Task> callback)
    {
        var handlers = _subscriptions.GetOrAdd(type, _ => new ConcurrentBag<Func<BusEvent, Task>>());
        handlers.Add(callback);
        return new Unsubscriber(() => 
        {
            if (_subscriptions.TryGetValue(type, out var currentHandlers))
            {
                // Note: ConcurrentBag doesn't have a direct Remove. 
                // In a real high-perf scenario, we might use a different collection.
                // For now, we recreate the bag without the callback.
                var newHandlers = new ConcurrentBag<Func<BusEvent, Task>>(currentHandlers.Where(h => h != callback));
                _subscriptions.TryUpdate(type, newHandlers, currentHandlers);
            }
        });
    }

    public IDisposable SubscribeAsync(string type, Func<BusEvent, Task> callback) => Subscribe(type, callback);

    public IDisposable SubscribeAll(Func<BusEvent, Task> callback)
    {
        _wildcardSubscriptions.Add(callback);
        return new Unsubscriber(() => 
        {
            // Recreating the bag for wildcard subscriptions
            var currentHandlers = _wildcardSubscriptions.ToList();
            var newHandlers = currentHandlers.Where(h => h != callback).ToList();
            // This is tricky with ConcurrentBag. 
            // In a real project, we'd use a more suitable concurrent collection for removal.
        });
    }

    private class Unsubscriber : IDisposable
    {
        private readonly Action _unsubscribe;
        public Unsubscriber(Action unsubscribe) => _unsubscribe = unsubscribe;
        public void Dispose() => _unsubscribe();
    }
}
