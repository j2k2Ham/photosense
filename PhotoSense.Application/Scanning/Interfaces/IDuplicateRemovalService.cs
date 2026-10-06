namespace PhotoSense.Application.Scanning.Interfaces;

/// <param name="Removed">Files moved to the holding folder.</param>
/// <param name="Bytes">Their combined size.</param>
/// <param name="Skipped">Files left alone because removing them could not be shown to be safe.</param>
/// <param name="Companions">Sidecars and Live Photo videos moved along with the files they belonged to.</param>
public sealed record BulkRemovalResult(int Removed, long Bytes, int Skipped, IReadOnlyList<string> Problems, int Companions = 0);

public interface IDuplicateRemovalService
{
    /// <summary>
    /// Removes the duplicates of one group, or of every group when <paramref name="groupKey"/> is null.
    /// Only identical files and confirmed copies are taken; kept photos and look-alikes never are.
    /// </summary>
    Task<BulkRemovalResult> RemoveDuplicatesAsync(string? groupKey = null, CancellationToken ct = default);
}
