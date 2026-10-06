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

    public static bool IsImage(string path) => Images.Contains(Path.GetExtension(path));
    public static bool IsVideo(string path) => Videos.Contains(Path.GetExtension(path));
    public static bool IsScanned(string path) => IsImage(path) || IsVideo(path);
}
