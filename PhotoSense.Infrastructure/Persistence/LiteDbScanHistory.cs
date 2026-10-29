using LiteDB;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Infrastructure.Persistence;

/// <summary>Kept apart from the photo records: clearing the results does not forget how long scans take.</summary>
public sealed class LiteDbScanHistory : IScanHistory
{
    private readonly ILiteCollection<ScanRecord> _col;

    /// <summary>Uses a database that other stores share; the caller disposes it.</summary>
    public LiteDbScanHistory(LiteDatabase db)
    {
        LiteDbMapping.Prepare<ScanRecord>();
        _col = db.GetCollection<ScanRecord>("scans");
        _col.EnsureIndex(x => x.StartedUtc);
    }

    public Task AddAsync(ScanRecord scan, CancellationToken ct = default)
    { _col.Insert(scan); return Task.CompletedTask; }

    public Task<IReadOnlyList<ScanRecord>> RecentAsync(int take = 10, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ScanRecord>>(_col.Query().OrderByDescending(x => x.StartedUtc).Limit(take).ToList());
}
