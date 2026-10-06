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
            Type = NameOf(typeof(T)),
            Payload = JsonSerializer.Serialize(evt),
            OccurredUtc = DateTime.UtcNow
        };
        return _outbox.AddAsync(message, ct);
    }

    /// <summary>The name an event is filed under: its full name, or its short one for a type that has no full name.</summary>
    public static string NameOf(Type type) => type.FullName ?? type.Name;
}