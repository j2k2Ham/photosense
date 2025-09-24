using PhotoSense.Domain.Services;
using System.Collections.Concurrent;

namespace PhotoSense.Infrastructure.Events;

public class InMemoryIntegrationEventPublisher : IIntegrationEventPublisher
{
    private static readonly ConcurrentQueue<object> _events = new();
    public Task PublishAsync<T>(T evt, CancellationToken ct = default) where T : class
    {
        _events.Enqueue(evt);
        return Task.CompletedTask;
    }

    public static IReadOnlyList<object> Drain() => _events.ToList();
}
