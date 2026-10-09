using PhotoSense.Domain.Entities;

namespace PhotoSense.Domain.Repositories;

public interface IOrganizeBatchStore
{
    Task AddAsync(OrganizeBatch batch, CancellationToken ct = default);
    Task<OrganizeBatch?> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>The latest moves and copies that have not been undone, newest first.</summary>
    Task<IReadOnlyList<OrganizeBatch>> RecentAsync(int take = 20, CancellationToken ct = default);
    Task MarkUndoneAsync(Guid id, CancellationToken ct = default);
}
