using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Application.Scanning.Interfaces;

/// <summary>What became of a photo asked to be removed.</summary>
public enum RemovalOutcome
{
    /// <summary>It went: its file into the folder that holds removed files, or only its record.</summary>
    Removed,
    /// <summary>No record with that id.</summary>
    NotFound,
    /// <summary>The file on disk no longer matches what was scanned, so it was left alone.</summary>
    Changed,
    /// <summary>The file could not be moved; the record was left in place.</summary>
    Failed,
    /// <summary>Nothing it is a copy of is left on disk as it was scanned, so it was left alone: it may be the only copy.</summary>
    LastCopy
}

/// <summary>What removing one photo did.</summary>
/// <param name="Outcome">What became of it.</param>
/// <param name="HeldAt">Where the file now is, when it was moved.</param>
/// <param name="Error">Why it could not be moved, when it could not.</param>
public sealed record RemovalResult(RemovalOutcome Outcome, string? HeldAt = null, string? Error = null)
{
    /// <summary>The photo went.</summary>
    public bool Succeeded => Outcome == RemovalOutcome.Removed;

    /// <summary>Sidecars and Live Photo videos that belonged to the removed file alone and were moved with it.</summary>
    public IReadOnlyList<string> Companions { get; init; } = [];

    /// <summary>What to undo the removal by; absent when no file was moved.</summary>
    public Guid? BatchId { get; init; }
}

/// <summary>What removing the copies of one or more groups did.</summary>
/// <param name="Removed">Files moved to the holding folder.</param>
/// <param name="Bytes">Their combined size.</param>
/// <param name="Skipped">Files left alone because removing them could not be shown to be safe, or, when a file could not be moved, every file.</param>
/// <param name="Problems">Why each file left alone was left, naming it.</param>
/// <param name="Companions">Sidecars and Live Photo videos moved along with the files they belonged to.</param>
/// <param name="BatchId">What to undo the removal by; absent when no file was moved.</param>
public sealed record BulkRemovalResult(int Removed, long Bytes, int Skipped, IReadOnlyList<string> Problems, int Companions = 0, Guid? BatchId = null);

/// <summary>Removes the copies and look-alikes a scan found, into the folder that holds removed files.</summary>
public interface IDuplicateRemovalService
{
    /// <summary>
    /// Removes the duplicates of one group, or of every group when <paramref name="groupKey"/> is null, as
    /// one change: when one file cannot be moved, none is. Photos marked keep are never taken, and neither is
    /// the photo a group is built around.
    /// </summary>
    /// <param name="groupKey">The group, by its key; null for every group of duplicates.</param>
    /// <param name="similar">
    /// Take the look-alikes of the group named instead of its duplicates. Look-alikes are different shots, so
    /// they go one group at a time, when asked for by name: with no group named, nothing is removed.
    /// </param>
    /// <param name="ct">Stops the removal before any file has moved.</param>
    Task<BulkRemovalResult> RemoveDuplicatesAsync(string? groupKey, bool similar, CancellationToken ct);

    /// <summary>
    /// Removes the duplicates of each of the groups named, or with <paramref name="similar"/> their
    /// look-alikes, as <see cref="RemoveDuplicatesAsync"/> does for one, all as one change. The groups are
    /// worked out once for all of them; a name that is no group's is passed over.
    /// </summary>
    /// <param name="groupKeys">The groups, by key.</param>
    /// <param name="similar">Take their look-alikes instead of their duplicates.</param>
    /// <param name="ct">Stops the removal before any file has moved.</param>
    Task<BulkRemovalResult> RemoveGroupsAsync(IReadOnlyCollection<string> groupKeys, bool similar, CancellationToken ct);

    /// <summary>
    /// Removes one photo. A photo in a duplicate group goes only while another file of the group stays
    /// behind as it was scanned: the photo kept for one of its copies, a copy for the photo kept. Without
    /// <paramref name="deleteFile"/> only the record is forgotten and the file stays where it is.
    /// </summary>
    /// <param name="id">The photo.</param>
    /// <param name="deleteFile">Move its file into the folder that holds removed files, rather than only forgetting its record.</param>
    /// <param name="ct">Stops the removal before the file has moved.</param>
    Task<RemovalResult> RemoveAsync(PhotoId id, bool deleteFile, CancellationToken ct);
}
