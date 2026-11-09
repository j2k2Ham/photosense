using LiteDB;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Infrastructure.Persistence;

/// <summary>Kept apart from the photo records: clearing the results does not forget how long scans take.</summary>
/// <remarks>Each method answers at once; its token is checked first, as nothing it does takes long enough to stop part way.</remarks>
internal sealed class LiteDbScanHistory : IScanHistory
{
    private readonly ILiteCollection<ScanRecord> _col;

    /// <summary>Uses a database that other stores share; the caller disposes it.</summary>
    public LiteDbScanHistory(LiteDatabase db)
    {
        LiteDbMapping.Prepare<ScanRecord>();
        _col = db.GetCollection<ScanRecord>("scans");
        _col.EnsureIndex(x => x.StartedUtc);
    }

    /// <inheritdoc />
    public Task AddAsync(ScanRecord scan, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _col.Insert(scan);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScanRecord>> RecentAsync(int take, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ScanRecord>>(_col.Query().OrderByDescending(x => x.StartedUtc).Limit(take).ToList());
    }
}
