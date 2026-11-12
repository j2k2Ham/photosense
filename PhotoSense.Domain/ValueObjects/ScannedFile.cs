using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Services;

namespace PhotoSense.Domain.ValueObjects;

/// <summary>What a scan recorded about a file, checked against the file as it is now.</summary>
public static class ScannedFile
{
    /// <summary>True while the file is there with the size, and the modified time where one was recorded, that it was scanned with.</summary>
    public static bool IsIntact(Photo photo, IPhotoFileSystem files)
        => files.Describe(photo.SourcePath) is { } file && file.Length == photo.FileSizeBytes
            && (photo.FileModifiedUtc is not { } scanned || file.ModifiedUtc == scanned);
}
