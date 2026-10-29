using PhotoSense.Domain.Entities;

namespace PhotoSense.Domain.Repositories;

/// <summary>A record of what was done to photos: kept, moved, removed.</summary>
public interface IAuditRepository
{
    /// <summary>Records one thing that was done.</summary>
    Task AddAsync(AuditEntry entry, CancellationToken ct = default);
    /// <summary>The latest things done, newest first.</summary>
    Task<IReadOnlyList<AuditEntry>> RecentAsync(int take = 200, CancellationToken ct = default);
}
