using LiteDB;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Infrastructure.Persistence;

/// <summary>The record of what was done to photos, kept in the database, each entry stamped with the time it is kept.</summary>
internal sealed class LiteDbAuditRepository : IAuditRepository
{
    private readonly ILiteCollection<AuditEntry> _col;
    private readonly TimeProvider _time;

    /// <summary>Uses the database the other stores share, which its owner disposes.</summary>
    /// <param name="db">The shared database.</param>
    /// <param name="time">The clock each entry is stamped from.</param>
    public LiteDbAuditRepository(LiteDatabase db, TimeProvider time)
    {
        _time = time;
        LiteDbMapping.Prepare<AuditEntry>();
        _col = db.GetCollection<AuditEntry>("audit");
        _col.EnsureIndex(x => x.UtcTimestamp);
    }

    /// <inheritdoc />
    public Task AddAsync(AuditEntry entry, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _col.Insert(entry with { UtcTimestamp = _time.GetUtcNow().UtcDateTime });
        return Task.CompletedTask;
    }
}
