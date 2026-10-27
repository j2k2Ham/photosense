using PhotoSense.Domain.Entities;

namespace PhotoSense.Domain.Repositories;

/// <summary>What each move, copy or removal did, kept so that it can be undone after a restart.</summary>
public interface IOrganizeBatchStore
{
    /// <summary>Writes a change down as it stands: before any file is touched, and again whenever it moves on.</summary>
    Task SaveAsync(OrganizeBatch batch, CancellationToken ct = default);
    /// <summary>One move, copy or removal, undone or not; null when there is none with that id.</summary>
    Task<OrganizeBatch?> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>The latest changes that were carried out in full and have not been undone, newest first.</summary>
    Task<IReadOnlyList<OrganizeBatch>> RecentAsync(int take = 20, CancellationToken ct = default);
    /// <summary>Changes that were still being carried out or undone when the service stopped.</summary>
    Task<IReadOnlyList<OrganizeBatch>> UnfinishedAsync(CancellationToken ct = default);
    /// <summary>Notes that it was undone, so it is no longer offered; an id it does not know changes nothing.</summary>
    Task MarkUndoneAsync(Guid id, CancellationToken ct = default);
    /// <summary>Keeps what scans recorded of the files a removal takes, so that undoing it brings the records back too.</summary>
    Task KeepRecordsAsync(Guid id, IReadOnlyList<Photo> records, CancellationToken ct = default);
    /// <summary>The records kept for a removal; none for one that kept none.</summary>
    Task<IReadOnlyList<Photo>> RecordsAsync(Guid id, CancellationToken ct = default);
}
