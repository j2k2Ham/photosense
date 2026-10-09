namespace PhotoSense.Domain.Services;

public interface ICompanionFileFinder
{
    /// <summary>
    /// Files that exist only to accompany the given photo or video and would be left without a purpose if it
    /// were removed: its edit sidecars and, for a picture, its Live Photo video. Empty while anything else in
    /// the folder still uses them. Must be called while the file is still in place.
    /// </summary>
    IReadOnlyList<string> FindFor(string path);

    /// <summary>
    /// The same, for a caller that has already looked through the folder: <paramref name="siblings"/> are the
    /// other files there that carry the item's name. Asking about many files of one folder this way reads
    /// the folder once instead of once for each.
    /// </summary>
    IReadOnlyList<string> FindAmong(string path, IReadOnlyList<string> siblings);
}
