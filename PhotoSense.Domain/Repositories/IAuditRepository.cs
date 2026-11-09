using PhotoSense.Domain.Entities;

namespace PhotoSense.Domain.Repositories;

/// <summary>A record of what was done to photos: kept, moved, removed.</summary>
public interface IAuditRepository
{
    /// <summary>Records one thing that was done, stamped with the time it is kept.</summary>
    Task AddAsync(AuditEntry entry, CancellationToken ct);
}
