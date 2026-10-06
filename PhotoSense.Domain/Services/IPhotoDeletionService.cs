using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Domain.Services;

public enum RemovalOutcome
{
    Removed,
    /// <summary>No record with that id.</summary>
    NotFound,
    /// <summary>The file on disk no longer matches what was scanned, so it was left alone.</summary>
    Changed,
    /// <summary>The file could not be moved; the record was left in place.</summary>
    Failed
}

/// <param name="HeldAt">Where the file now is, when it was moved.</param>
public sealed record RemovalResult(RemovalOutcome Outcome, string? HeldAt = null, string? Error = null)
{
    public bool Succeeded => Outcome == RemovalOutcome.Removed;

    /// <summary>Sidecars and Live Photo videos that belonged to the removed file alone and were moved with it.</summary>
    public IReadOnlyList<string> Companions { get; init; } = [];
}

public interface IPhotoDeletionService
{
    /// <summary>
    /// Forgets a photo. With <paramref name="deleteFile"/> the file is first moved into the holding folder
    /// under the folder it was scanned from, together with the files that only accompanied it; nothing is erased from disk.
    /// </summary>
    Task<RemovalResult> DeleteAsync(PhotoId id, bool deleteFile, CancellationToken ct = default);
}
