using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Application.Scanning.Services;

public class DuplicateRemovalService : IDuplicateRemovalService
{
    private readonly IDuplicateAnalysisService _analysis;
    private readonly IPhotoDeletionService _deleter;

    public DuplicateRemovalService(IDuplicateAnalysisService analysis, IPhotoDeletionService deleter)
    { _analysis = analysis; _deleter = deleter; }

    public async Task<BulkRemovalResult> RemoveDuplicatesAsync(string? groupKey = null, bool similar = false, CancellationToken ct = default)
    {
        var analysis = await _analysis.GetAsync(ct);
        // A photo can head a group of each kind under the one key, so the kind is asked for, never guessed.
        var groups = similar ? analysis.Similar : analysis.Duplicates;
        if (groupKey is not null || similar) groups = groups.Where(g => g.Key == groupKey).ToList();
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
            if (!ScannedFile.IsIntact(group.Keeper))
            {
                skipped += removable.Count;
                problems.Add($"Kept photo is missing or has changed, so its copies were left alone: {group.Keeper.SourcePath}");
                continue;
            }
            foreach (var photo in removable)
            {
                ct.ThrowIfCancellationRequested();
                var result = await _deleter.DeleteAsync(photo.Id, deleteFile: true, [group.Keeper], ct);
                if (result.Succeeded) { removed++; bytes += photo.FileSizeBytes; companions += result.Companions.Count; continue; }
                // Already gone, for instance taken a moment ago together with the picture it accompanied.
                if (result.Outcome == RemovalOutcome.NotFound) continue;
                skipped++;
                problems.Add(result.Outcome switch
                {
                    RemovalOutcome.Changed => $"Changed since the scan, left alone: {photo.SourcePath}",
                    RemovalOutcome.LastCopy => $"The same file as the photo kept, under another path, so it was left alone: {photo.SourcePath}",
                    _ => $"Could not move {photo.SourcePath}: {result.Error}"
                });
            }
        }
        return new BulkRemovalResult(removed, bytes, skipped, problems, companions);
    }

    public async Task<RemovalResult> RemoveAsync(PhotoId id, bool deleteFile, CancellationToken ct = default)
    {
        var group = (await _analysis.GetAsync(ct)).Duplicates.FirstOrDefault(g => g.Keeper.Id == id || g.Members.Any(m => m.Photo.Id == id));
        // A copy is a copy of the photo kept. The photo kept may go too, while one of its copies stays in its
        // place. A photo in no duplicate group is nobody's copy, and removing it is the user's own decision.
        IReadOnlyList<Photo>? copyOf = group is null ? null
            : group.Keeper.Id == id ? group.Members.Select(m => m.Photo).ToList() : [group.Keeper];
        return await _deleter.DeleteAsync(id, deleteFile, copyOf, ct);
    }
}
