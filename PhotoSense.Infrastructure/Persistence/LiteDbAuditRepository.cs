using LiteDB;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Infrastructure.Persistence;

/// <summary>The record of what was done to photos, kept in the database, each entry stamped with the time it is kept.</summary>
internal sealed class LiteDbAuditRepository : IAuditRepository, IDisposable
{
    private LiteDatabase? _db;
    private readonly bool _ownsDb;
    private ILiteCollection<AuditEntry>? _col;
    private readonly TimeProvider _time;

    /// <summary>Opens a database of its own, which it disposes.</summary>
    /// <param name="path">The database file.</param>
    /// <param name="time">The clock each entry is stamped from.</param>
    public LiteDbAuditRepository(string path, TimeProvider time) : this(new LiteDatabase(path), ownsDb: true, time) { }

    /// <summary>Uses a database that other stores share; the caller disposes it.</summary>
    /// <param name="db">The shared database.</param>
    /// <param name="time">The clock each entry is stamped from.</param>
    public LiteDbAuditRepository(LiteDatabase db, TimeProvider time) : this(db, ownsDb: false, time) { }

    private LiteDbAuditRepository(LiteDatabase db, bool ownsDb, TimeProvider time)
    {
        _db = db;
        _ownsDb = ownsDb;
        _time = time;
        LiteDbMapping.Prepare<AuditEntry>();
        _col = _db.GetCollection<AuditEntry>("audit");
        _col.EnsureIndex(x => x.UtcTimestamp);
    }

    /// <inheritdoc />
    public Task AddAsync(AuditEntry entry, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _col!.Insert(entry with { UtcTimestamp = _time.GetUtcNow().UtcDateTime });
        return Task.CompletedTask;
    }

    /// <summary>Disposes the database when it opened its own.</summary>
    public void Dispose()
    {
        if (_ownsDb) _db?.Dispose();
        _db = null;
        _col = null;
    }
}
