using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Services;
using Directory = MetadataExtractor.Directory;

namespace PhotoSense.Infrastructure.Metadata;

/// <summary>
/// Reads when and where a file was taken, and how large its picture is, from the file's own record of
/// itself. Nothing is decoded, so a folder can be listed without scanning it.
/// </summary>
public sealed class ExifMediaDetailsReader : IMediaDetailsReader
{
    // What the formats call the size of their picture, the plainest name first.
    private static readonly (string Width, string Height)[] SizeTags = [("Image Width", "Image Height"), ("Exif Image Width", "Exif Image Height"), ("Width", "Height")];

    public MediaDetails Read(string path)
    {
        var photo = new Photo { SourcePath = path, FileName = Path.GetFileName(path) };
        try
        {
            // Opened so that the file can still be moved while it is being read.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var directories = ImageMetadataReader.ReadMetadata(stream);
            BasicExifMetadataExtractor.Read(photo, directories);
            if (photo.Width == 0) (photo.Width, photo.Height) = PixelSize(directories);
        }
        // Not a file this can read: it is listed by its name and the file's own date.
        catch (Exception ex) when (ex is IOException or ImageProcessingException or UnauthorizedAccessException) { }
        return new MediaDetails(photo.TakenOn, photo.Width, photo.Height, photo.DurationSeconds, photo.Latitude, photo.Longitude, photo.LivePhotoId);
    }

    /// <summary>The size of the picture as it is shown: a camera held upright records sideways and notes the turn.</summary>
    public static (int Width, int Height) PixelSize(IReadOnlyList<Directory> directories)
    {
        foreach (var (widthTag, heightTag) in SizeTags)
            foreach (var directory in directories)
            {
                if (Number(directory, widthTag) is not { } width || Number(directory, heightTag) is not { } height) continue;
                var turned = directories.OfType<ExifIfd0Directory>().FirstOrDefault() is { } ifd0
                    && ifd0.TryGetInt32(ExifDirectoryBase.TagOrientation, out var orientation) && orientation >= 5;
                return turned ? (height, width) : (width, height);
            }
        return (0, 0);
    }

    private static int? Number(Directory directory, string tagName)
        => directory.Tags.FirstOrDefault(t => t.Name == tagName) is { } tag && directory.TryGetInt32(tag.Type, out var value) && value > 0 ? value : null;
}
