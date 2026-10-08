using PhotoSense.Domain.Entities;

namespace PhotoSense.Domain.Repositories;

public interface IScanHistory
{
    Task AddAsync(ScanRecord scan, CancellationToken ct = default);

    /// <summary>The latest scans, newest first.</summary>
    Task<IReadOnlyList<ScanRecord>> RecentAsync(int take = 10, CancellationToken ct = default);
}
