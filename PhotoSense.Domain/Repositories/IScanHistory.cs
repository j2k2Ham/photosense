using PhotoSense.Domain.Entities;

namespace PhotoSense.Domain.Repositories;

/// <summary>How long earlier scans took, so the next one can say how long it expects to take.</summary>
public interface IScanHistory
{
    /// <summary>Keeps a finished scan.</summary>
    Task AddAsync(ScanRecord scan, CancellationToken ct = default);

    /// <summary>The latest scans, newest first.</summary>
    Task<IReadOnlyList<ScanRecord>> RecentAsync(int take = 10, CancellationToken ct = default);
}
