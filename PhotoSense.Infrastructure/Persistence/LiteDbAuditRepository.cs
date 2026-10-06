using LiteDB;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Infrastructure.Persistence;

public sealed class LiteDbAuditRepository : IAuditRepository, IDisposable
{
    private LiteDatabase? _db;
    private readonly bool _ownsDb;
    private ILiteCollection<AuditEntry>? _col;
    public LiteDbAuditRepository(string path = "photosense.db") : this(new LiteDatabase(path), ownsDb: true) { }
    /// <summary>Uses a database that other stores share; the caller disposes it.</summary>
    public LiteDbAuditRepository(LiteDatabase db) : this(db, ownsDb: false) { }
    private LiteDbAuditRepository(LiteDatabase db, bool ownsDb)
    {
        _db = db;
        _ownsDb = ownsDb;
        _col = _db.GetCollection<AuditEntry>("audit");
        _col.EnsureIndex(x => x.UtcTimestamp);
    }
    public Task AddAsync(AuditEntry entry, CancellationToken ct = default)
    { _col!.Insert(entry); return Task.CompletedTask; }
    public Task<IReadOnlyList<AuditEntry>> RecentAsync(int take = 200, CancellationToken ct = default)
    { return Task.FromResult<IReadOnlyList<AuditEntry>>(_col!.Query().OrderByDescending(x=>x.UtcTimestamp).Limit(take).ToList()); }
    public void Dispose(){ if (_ownsDb) _db?.Dispose(); _db=null; _col=null; }
}