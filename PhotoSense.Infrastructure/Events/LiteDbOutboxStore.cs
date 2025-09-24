using LiteDB;
using PhotoSense.Domain.Events;
using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Events;

public class LiteDbOutboxStore : IOutboxStore
{
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<OutboxMessage> _col;

    public LiteDbOutboxStore(LiteDatabase db)
    {
        _db = db;
        _col = _db.GetCollection<OutboxMessage>("outbox");
        _col.EnsureIndex(x => x.ProcessedUtc);
    }

    public Task AddAsync(OutboxMessage message, CancellationToken ct = default)
    {
        _col.Insert(message);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(int max = 100, CancellationToken ct = default)
    {
        var list = _col.Query()
            .Where(x => x.ProcessedUtc == null)
            .OrderBy(x => x.OccurredUtc)
            .Limit(max)
            .ToList();
        return Task.FromResult<IReadOnlyList<OutboxMessage>>(list);
    }

    public Task MarkProcessedAsync(Guid id, CancellationToken ct = default)
    {
        var msg = _col.FindById(id);
        if (msg != null)
        {
            msg.ProcessedUtc = DateTime.UtcNow;
            _col.Update(msg);
        }
        return Task.CompletedTask;
    }
}