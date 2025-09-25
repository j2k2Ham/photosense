using PhotoSense.Domain.Entities;

namespace PhotoSense.Domain.Repositories;

public interface IAuditRepository
{
    Task AddAsync(AuditEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<AuditEntry>> RecentAsync(int take = 200, CancellationToken ct = default);
}