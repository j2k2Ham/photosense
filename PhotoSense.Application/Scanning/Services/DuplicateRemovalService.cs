using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Services;

namespace PhotoSense.Application.Scanning.Services;

public class DuplicateRemovalService : IDuplicateRemovalService
{
    private readonly IDuplicateAnalysisService _analysis;
    private readonly IPhotoDeletionService _deleter;

    public DuplicateRemovalService(IDuplicateAnalysisService analysis, IPhotoDeletionService deleter)
    { _analysis = analysis; _deleter = deleter; }

    public async Task<BulkRemovalResult> RemoveDuplicatesAsync(string? groupKey = null, CancellationToken ct = default)
    {
        var groups = (await _analysis.GetAsync(ct)).Duplicates;
        if (groupKey is not null) groups = groups.Where(g => g.Key == groupKey).ToList();
        // Pictures first: a picture takes its Live Photo video with it, and a video that has just gone that
        // way must be seen to be gone before any copy of it is judged redundant.
        groups = groups.OrderBy(g => g.Keeper.IsVideo).ToList();

        int removed = 0, skipped = 0, companions = 0;
        long bytes = 0;
        var problems = new List<string>();
        foreach (var group in groups)
        {
            var removable = group.Removable.ToList();
            // Copies are only redundant while the photo they are copies of is still there, unchanged.
            if (!IsIntact(group.Keeper))
            {
                skipped += removable.Count;
                problems.Add($"Kept photo is missing or has changed, so its copies were left alone: {group.Keeper.SourcePath}");
                continue;
            }
            foreach (var photo in removable)
            {
                ct.ThrowIfCancellationRequested();
                var result = await _deleter.DeleteAsync(photo.Id, deleteFile: true, ct);
                if (result.Succeeded) { removed++; bytes += photo.FileSizeBytes; companions += result.Companions.Count; continue; }
                // Already gone, for instance taken a moment ago together with the picture it accompanied.
                if (result.Outcome == RemovalOutcome.NotFound) continue;
                skipped++;
                problems.Add(result.Outcome == RemovalOutcome.Changed
                    ? $"Changed since the scan, left alone: {photo.SourcePath}"
                    : $"Could not move {photo.SourcePath}: {result.Error}");
            }
        }
        return new BulkRemovalResult(removed, bytes, skipped, problems, companions);
    }

    private static bool IsIntact(Photo photo)
    {
        var file = new FileInfo(photo.SourcePath);
        return file.Exists && file.Length == photo.FileSizeBytes
            && (photo.FileModifiedUtc is not { } scanned || file.LastWriteTimeUtc == scanned);
    }
}
