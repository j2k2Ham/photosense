using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Infrastructure.Metadata;

namespace PhotoSense.Infrastructure.Deletion;

public class CompanionFileFinder : ICompanionFileFinder
{
    private readonly Func<string, string?> _readLivePhotoId;

    public CompanionFileFinder() : this(LivePhotoLink.ReadId) { }

    /// <param name="readLivePhotoId">Reads the Live Photo identifier a picture or video carries, if any.</param>
    public CompanionFileFinder(Func<string, string?> readLivePhotoId) => _readLivePhotoId = readLivePhotoId;

    public IReadOnlyList<string> FindFor(string path)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (folder is null || !Directory.Exists(folder)) return [];

        // Everything else in the folder carrying this item's name: other formats, an edited version, sidecars, a video.
        var item = PhotoNaming.ItemOf(path);
        var self = PhotoPath.Key(path);
        var siblings = Directory.EnumerateFiles(folder)
            .Where(f => PhotoNaming.ItemOf(f) == item && PhotoPath.Key(f) != self)
            .ToList();
        return FindAmong(path, siblings);
    }

    public IReadOnlyList<string> FindAmong(string path, IReadOnlyList<string> siblings)
    {
        // While another picture of the item stays, its sidecars and its Live Photo video still have an owner.
        if (siblings.Any(MediaFiles.IsImage)) return [];

        var companions = new List<string>();
        // A video is this picture's Live Photo half only if the two carry the same identifier. The name is
        // not enough: an unrelated video can have the same number.
        if (MediaFiles.IsImage(path) && _readLivePhotoId(path) is { } id)
            companions.AddRange(siblings.Where(f => MediaFiles.IsVideo(f) && string.Equals(_readLivePhotoId(f), id, StringComparison.OrdinalIgnoreCase)));

        // Sidecars record edits to the item. They go once no picture or video of it is left.
        if (siblings.Where(MediaFiles.IsVideo).All(companions.Contains))
            companions.AddRange(siblings.Where(PhotoNaming.IsSidecar));
        return companions;
    }
}
