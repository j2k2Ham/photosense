namespace PhotoSense.Domain.ValueObjects;

/// <summary>Which files a scan takes in, by extension.</summary>
public static class MediaFiles
{
    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".heic", ".heif"
    };

    // Videos are matched only as identical files; they are never decoded.
    private static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mov", ".mp4", ".m4v", ".3gp"
    };

    /// <summary>Whether the file is a picture, by its extension.</summary>
    public static bool IsImage(string path) => Images.Contains(Path.GetExtension(path));

    /// <summary>Whether the file is a video, by its extension.</summary>
    public static bool IsVideo(string path) => Videos.Contains(Path.GetExtension(path));

    /// <summary>Whether a scan takes the file in: a picture or a video.</summary>
    public static bool IsScanned(string path) => IsImage(path) || IsVideo(path);
}
