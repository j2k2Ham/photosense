namespace PhotoSense.Domain.Services;

public interface ICompanionFileFinder
{
    /// <summary>
    /// Files that exist only to accompany the given photo or video and would be left without a purpose if it
    /// were removed: its edit sidecars and, for a picture, its Live Photo video. Empty while anything else in
    /// the folder still uses them. Must be called while the file is still in place.
    /// </summary>
    IReadOnlyList<string> FindFor(string path);
}
