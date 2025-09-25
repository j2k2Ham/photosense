using LiteDB;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Infrastructure.Persistence;

public sealed class LiteDbAuditRepository : IAuditRepository, IDisposable
{
    private LiteDatabase? _db;
    private ILiteCollection<AuditEntry>? _col;
    public LiteDbAuditRepository(string path = "photosense.db")
    {
        _db = new LiteDatabase(path);
        _col = _db.GetCollection<AuditEntry>("audit");
        _col.EnsureIndex(x => x.UtcTimestamp);
    }
    public Task AddAsync(AuditEntry entry, CancellationToken ct = default)
    { _col!.Insert(entry); return Task.CompletedTask; }
    public Task<IReadOnlyList<AuditEntry>> RecentAsync(int take = 200, CancellationToken ct = default)
    { return Task.FromResult<IReadOnlyList<AuditEntry>>(_col!.Query().OrderByDescending(x=>x.UtcTimestamp).Limit(take).ToList()); }
    public void Dispose(){ _db?.Dispose(); _db=null; _col=null; }
}