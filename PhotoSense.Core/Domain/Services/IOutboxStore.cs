using PhotoSense.Core.Domain.Events;

namespace PhotoSense.Core.Domain.Services;

public interface IOutboxStore
{
    Task AddAsync(OutboxMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(int max = 100, CancellationToken ct = default);
    Task MarkProcessedAsync(Guid id, CancellationToken ct = default);
}