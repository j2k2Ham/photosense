using PhotoSense.Domain.Events;

namespace PhotoSense.Domain.Services;

/// <summary>Events waiting to be sent, kept so that none is lost if the service stops first.</summary>
public interface IOutboxStore
{
    /// <summary>Keeps an event until it has been sent.</summary>
    Task AddAsync(OutboxMessage message, CancellationToken ct = default);
    /// <summary>Events not yet sent, oldest first.</summary>
    Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(int max = 100, CancellationToken ct = default);
    /// <summary>Notes that an event has been sent; an id it does not know changes nothing.</summary>
    Task MarkProcessedAsync(Guid id, CancellationToken ct = default);
}
