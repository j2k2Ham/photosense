using PhotoSense.Application.Plans;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Matching;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Application.Removal;

/// <summary>A kind of picture or video Clean up finds beside duplicates and similar shots.</summary>
public enum CleanUpCategory
{
    /// <summary>Videos over 200 MB (<see cref="MediaKinds.LargeVideoBytes"/>).</summary>
    LargeVideos,
    /// <summary>Pictures of the screen.</summary>
    Screenshots,
    /// <summary>The shots of a burst but the one kept.</summary>
    Bursts,
    /// <summary>Pictures whose sharpest part is soft (<see cref="MediaKinds.BlurryBelow"/>).</summary>
    Blurry,
    /// <summary>What WhatsApp and Telegram saved.</summary>
    Messaging,
    /// <summary>The copies of each duplicate group that would go: never the copy kept, nor one marked keep.</summary>
    Duplicates,
}

/// <summary>How many of a category there are, and what they take.</summary>
/// <param name="Category">The category.</param>
/// <param name="Count">How many pictures or videos; for bursts, the shots that would go.</param>
/// <param name="Bytes">What they take.</param>
public sealed record CategorySummary(CleanUpCategory Category, int Count, long Bytes);

/// <summary>What Clean up's categories hold.</summary>
/// <param name="Categories">Each category, largest first.</param>
/// <param name="Unmeasured">Pictures whose sharpness no scan has measured yet, so Blurry cannot say of them.</param>
/// <param name="Storage">The phone's storage; null on a computer.</param>
public sealed record CategoryReport(IReadOnlyList<CategorySummary> Categories, int Unmeasured, DeviceStorage? Storage);

/// <summary>What deleting some of a category did.</summary>
/// <param name="Deleted">How many went.</param>
/// <param name="Bytes">What they took.</param>
/// <param name="Skipped">How many were left, as changed since the scan.</param>
/// <param name="Problems">Why each left was left.</param>
/// <param name="BatchId">On a computer, what Undo puts them back by; null on a phone, where they went to its own bin.</param>
public sealed record CategoryRemoval(int Deleted, long Bytes, int Skipped, IReadOnlyList<string> Problems, Guid? BatchId);

/// <summary>
/// Clean up's categories, from what the scans recorded: large videos, screenshots, bursts, blurry pictures and what
/// messaging apps saved, each without what the person marked to keep. Deleting some sends them, on a phone, to its own
/// Recently Deleted or bin after its question, counting toward the day's free removals; on a computer, into
/// _PhotoSense_Removed as one change, which Undo puts back.
/// </summary>
public sealed class CleanUpCategories
{
    private readonly IPhotoRepository _repo;
    private readonly IPhotoFileSystem _files;
    private readonly ICompanionFileFinder _companions;
    private readonly IDuplicateRemovalService _removal;
    private readonly IDuplicateAnalysisService _analysis;
    private readonly DailyRemovals _removals;
    private readonly IDeviceStatus? _device;

    /// <summary>The categories of what the scans recorded.</summary>
    /// <param name="repo">What the scans recorded.</param>
    /// <param name="files">Where the photos are; on a phone it deletes several together after one question.</param>
    /// <param name="companions">A picture's Live Photo video and sidecars, which go with it.</param>
    /// <param name="removal">On a computer, removes into _PhotoSense_Removed as one change.</param>
    /// <param name="analysis">The duplicate groups, whose copies Free up space deletes.</param>
    /// <param name="removals">The day's free removals, which a phone's deletions count toward.</param>
    /// <param name="device">The phone, for its storage; null on a computer.</param>
    public CleanUpCategories(IPhotoRepository repo, IPhotoFileSystem files, ICompanionFileFinder companions, IDuplicateRemovalService removal, IDuplicateAnalysisService analysis,
        DailyRemovals removals, IDeviceStatus? device)
    {
        _analysis = analysis;
        _repo = repo;
        _files = files;
        _companions = companions;
        _removal = removal;
        _removals = removals;
        _device = device;
    }

    /// <summary>How many each category holds and what they take, largest first, and the phone's storage.</summary>
    public async Task<CategoryReport> ReportAsync(CancellationToken ct)
    {
        var photos = await CandidatesAsync(ct);
        var summaries = new List<CategorySummary>();
        foreach (var category in Enum.GetValues<CleanUpCategory>())
        {
            var found = category switch
            {
                CleanUpCategory.Bursts => Bursts.Of(photos).SelectMany(b => b.Extra).ToList(),
                CleanUpCategory.Duplicates => await CopiesAsync(ct),
                _ => Of(category, photos),
            };
            summaries.Add(new CategorySummary(category, found.Count, found.Sum(p => p.FileSizeBytes)));
        }
        var unmeasured = photos.Count(p => !p.IsVideo && p.Sharpness is null && p.PerceptualHash is not null);
        return new CategoryReport([.. summaries.OrderByDescending(s => s.Bytes)], unmeasured, _device?.Storage);
    }

    /// <summary>The pictures or videos of a category: large videos largest first, the rest latest first.</summary>
    /// <exception cref="ArgumentException">Bursts, which <see cref="BurstsAsync"/> gives.</exception>
    public async Task<IReadOnlyList<Photo>> PhotosAsync(CleanUpCategory category, CancellationToken ct)
    {
        if (category == CleanUpCategory.Bursts) throw new ArgumentException("Bursts are given by burst.", nameof(category));
        return category == CleanUpCategory.Duplicates ? await CopiesAsync(ct) : Of(category, await CandidatesAsync(ct));
    }

    /// <summary>
    /// What Free up space offers: the duplicates' copies, the screenshots and the blurry shots, each photo once though it
    /// be in two of them. The low-storage alert says this, so that it is the number the screen it opens shows.
    /// </summary>
    public async Task<(int Count, long Bytes)> FreeUpSpaceAsync(CancellationToken ct)
    {
        var candidates = await CandidatesAsync(ct);
        var quick = (await CopiesAsync(ct)).Concat(Of(CleanUpCategory.Screenshots, candidates)).Concat(Of(CleanUpCategory.Blurry, candidates))
            .DistinctBy(p => p.Id).ToList();
        return (quick.Count, quick.Sum(p => p.FileSizeBytes));
    }

    /// <summary>The bursts, latest first, each with the shot to keep.</summary>
    public async Task<IReadOnlyList<Burst>> BurstsAsync(CancellationToken ct) => Bursts.Of(await CandidatesAsync(ct));

    /// <summary>
    /// Deletes the pictures and videos chosen, with their Live Photo videos and sidecars: on a phone to its Recently
    /// Deleted or bin, after its own question, each counting toward the day's free removals; on a computer into
    /// _PhotoSense_Removed as one change, which Undo puts back.
    /// </summary>
    /// <param name="ids">The photos.</param>
    /// <param name="ct">Stops it before anything is asked or moved.</param>
    /// <exception cref="InvalidOperationException">On a phone, more than the free plan has left today.</exception>
    /// <exception cref="IOException">On a phone, the person said no, or the phone could not delete them: nothing went.</exception>
    public async Task<CategoryRemoval> DeleteAsync(IReadOnlyCollection<PhotoId> ids, CancellationToken ct)
    {
        if (_files is not IPhotoChangesTogether phone)
        {
            var moved = await _removal.RemovePhotosAsync(ids, ct);
            return new CategoryRemoval(moved.Removed, moved.Bytes, moved.Skipped, moved.Problems, moved.BatchId);
        }
        var going = new List<Photo>();
        foreach (var id in ids)
            if (await _repo.GetAsync(id, ct) is { } photo) going.Add(photo);
        if (going.Count == 0) return new CategoryRemoval(0, 0, 0, [], null);
        var allowance = await _removals.TodayAsync(ct);
        if (!allowance.Allows(going.Count)) throw new InvalidOperationException(DailyRemovals.Refusal(allowance, going.Count));
        var paths = new HashSet<string>(PhotoPath.Comparer);
        foreach (var photo in going)
        {
            paths.Add(photo.SourcePath);
            foreach (var companion in await _companions.FindForAsync(photo.SourcePath, ct)) paths.Add(companion);
        }
        phone.EraseAll([.. paths]);
        // Deleted: counted and forgotten whatever the caller does now.
        await _removals.RecordAsync(going.Count, going.Sum(p => p.FileSizeBytes), allowance, CancellationToken.None);
        var records = new List<PhotoId>();
        foreach (var path in paths)
            if (await _repo.GetByPathAsync(path, CancellationToken.None) is { } record) records.Add(record.Id);
        await _repo.DeleteAllAsync(records, CancellationToken.None);
        return new CategoryRemoval(going.Count, going.Sum(p => p.FileSizeBytes), 0, [], null);
    }

    // The copies the duplicate groups would let go, largest first: never a group's keeper, nor a copy marked keep.
    private async Task<List<Photo>> CopiesAsync(CancellationToken ct)
        => [.. (await _analysis.GetAsync(ct)).Duplicates.SelectMany(g => g.Removable).OrderByDescending(p => p.FileSizeBytes)];

    // What the scans recorded, less what the person marked to keep.
    private async Task<List<Photo>> CandidatesAsync(CancellationToken ct) => [.. (await _repo.GetAllAsync(ct)).Where(p => !p.IsKept)];

    private static List<Photo> Of(CleanUpCategory category, List<Photo> photos) => category switch
    {
        CleanUpCategory.LargeVideos => [.. photos.Where(p => p.IsVideo && p.FileSizeBytes > MediaKinds.LargeVideoBytes).OrderByDescending(p => p.FileSizeBytes)],
        CleanUpCategory.Screenshots => Latest(photos.Where(p => p.IsScreenshot)),
        CleanUpCategory.Blurry => Latest(photos.Where(p => !p.IsVideo && !p.IsScreenshot && p.Sharpness < MediaKinds.BlurryBelow)),
        _ => Latest(photos.Where(p => MediaKinds.IsFromMessaging(p.SourcePath))),
    };

    private static List<Photo> Latest(IEnumerable<Photo> photos) => [.. photos.OrderByDescending(p => p.TakenOn ?? p.FileModifiedUtc)];
}
