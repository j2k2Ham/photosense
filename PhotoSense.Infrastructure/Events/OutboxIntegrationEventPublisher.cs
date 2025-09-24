using System.Text.Json;
using PhotoSense.Domain.Events;
using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Events;

public class OutboxIntegrationEventPublisher : IIntegrationEventPublisher
{
    private readonly IOutboxStore _outbox;

    public OutboxIntegrationEventPublisher(IOutboxStore outbox) => _outbox = outbox;

    public Task PublishAsync<T>(T evt, CancellationToken ct = default) where T : class
    {
        var message = new OutboxMessage
        {
            Type = typeof(T).FullName ?? typeof(T).Name,
            Payload = JsonSerializer.Serialize(evt),
            OccurredUtc = DateTime.UtcNow
        };
        return _outbox.AddAsync(message, ct);
    }
}