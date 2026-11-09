using MetadataExtractor;
using MetadataExtractor.Formats.Exif.Makernotes;
using MetadataExtractor.Formats.QuickTime;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;
using Directory = MetadataExtractor.Directory;

namespace PhotoSense.Infrastructure.Metadata;

/// <summary>
/// An iPhone writes the same identifier into both halves of a Live Photo: into the picture's Apple
/// maker note, and into the video's QuickTime metadata. A matching identifier is what ties the two
/// together; a shared file name is not (two unrelated items can end up with the same number).
/// </summary>
internal static class LivePhotoLink
{
    private const int AppleContentIdentifier = 0x0011;

    /// <summary>The identifier in what was read from a file, or null when it has none.</summary>
    /// <param name="directories">What was read from the file.</param>
    /// <param name="isVideo">The file is the video half, whose identifier is kept elsewhere than a picture's.</param>
    public static string? ReadId(IEnumerable<Directory> directories, bool isVideo)
    {
        var id = isVideo
            ? directories.OfType<QuickTimeMetadataHeaderDirectory>().SelectMany(d => d.Tags).FirstOrDefault(t => t.Name == "Content Identifier")?.Description
            : directories.OfType<AppleMakernoteDirectory>().FirstOrDefault()?.GetString(AppleContentIdentifier);
        return string.IsNullOrWhiteSpace(id) ? null : id.Trim();
    }

    /// <summary>The identifier in a file, or null when it has none or cannot be read.</summary>
    public static string? ReadId(IPhotoFileSystem files, string path)
    {
        string? id = null;
        _ = MetadataFiles.TryRead(files, path, directories => id = ReadId(directories, MediaFiles.IsVideo(path)));
        return id;
    }
}
