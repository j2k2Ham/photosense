using PhotoSense.Domain.Entities;

namespace PhotoSense.Domain.Repositories;

/// <summary>How long earlier scans took, so the next one can say how long it expects to take.</summary>
public interface IScanHistory
{
    /// <summary>Keeps a finished scan.</summary>
    Task AddAsync(ScanRecord scan, CancellationToken ct);

    /// <summary>The latest scans, newest first.</summary>
    /// <param name="take">How many at most.</param>
    /// <param name="ct">Stops the reading.</param>
    Task<IReadOnlyList<ScanRecord>> RecentAsync(int take, CancellationToken ct);
}
