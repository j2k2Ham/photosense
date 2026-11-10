using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Application.Removal;

/// <summary>Finds a photo's sidecars and Live Photo video by the names iOS gives them and the identifier they share.</summary>
public sealed class CompanionFileFinder : ICompanionFileFinder
{
    private readonly IPhotoFileSystem _files;
    private readonly Func<string, CancellationToken, Task<string?>> _readLivePhotoId;

    /// <summary>Finds them among the files of <paramref name="files"/>, reading each file's Live Photo identifier as given.</summary>
    /// <param name="files">Where the photos are kept.</param>
    /// <param name="readLivePhotoId">Reads the Live Photo identifier a picture or video carries, if any.</param>
    public CompanionFileFinder(IPhotoFileSystem files, Func<string, CancellationToken, Task<string?>> readLivePhotoId)
    {
        _files = files;
        _readLivePhotoId = readLivePhotoId;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> FindForAsync(string path, CancellationToken ct)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is not { } folder) return [];

        // Everything else in the folder carrying this item's name: other formats, an edited version, sidecars, a video.
        var item = PhotoNaming.ItemOf(path);
        var self = PhotoPath.Key(path);
        var siblings = _files.FilesIn(folder)
            .Where(f => PhotoNaming.ItemOf(f) == item && PhotoPath.Key(f) != self)
            .ToList();
        return await FindAmongAsync(path, siblings, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> FindAmongAsync(string path, IReadOnlyList<string> siblings, CancellationToken ct)
    {
        // Nothing else carries its name, so nothing can go with it. While another picture of the item stays,
        // its sidecars and its Live Photo video still have an owner.
        if (siblings.Count == 0 || siblings.Any(MediaFiles.IsImage)) return [];

        var companions = new List<string>();
        // A video is this picture's Live Photo half only if the two carry the same identifier. The name is
        // not enough: an unrelated video can have the same number.
        if (MediaFiles.IsImage(path) && await _readLivePhotoId(path, ct) is { } id)
            foreach (var video in siblings.Where(MediaFiles.IsVideo))
                if (string.Equals(await _readLivePhotoId(video, ct), id, StringComparison.OrdinalIgnoreCase)) companions.Add(video);

        // Sidecars record edits to the item. They go once no picture or video of it is left.
        if (siblings.Where(MediaFiles.IsVideo).All(companions.Contains))
            companions.AddRange(siblings.Where(PhotoNaming.IsSidecar));
        return companions;
    }
}
