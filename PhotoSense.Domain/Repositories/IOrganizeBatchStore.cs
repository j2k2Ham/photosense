using PhotoSense.Domain.Entities;

namespace PhotoSense.Domain.Repositories;

/// <summary>What each Organize move, copy or removal did, kept so that it can be undone after a restart.</summary>
public interface IOrganizeBatchStore
{
    /// <summary>Keeps a move, copy or removal that has just been carried out.</summary>
    Task AddAsync(OrganizeBatch batch, CancellationToken ct = default);
    /// <summary>One move, copy or removal, undone or not; null when there is none with that id.</summary>
    Task<OrganizeBatch?> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>The latest moves and copies that have not been undone, newest first.</summary>
    Task<IReadOnlyList<OrganizeBatch>> RecentAsync(int take = 20, CancellationToken ct = default);
    /// <summary>Notes that it was undone, so it is no longer offered; an id it does not know changes nothing.</summary>
    Task MarkUndoneAsync(Guid id, CancellationToken ct = default);
}
