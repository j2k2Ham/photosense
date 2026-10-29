using PhotoSense.Domain.Entities;

namespace PhotoSense.Domain.ValueObjects;

/// <summary>What a scan recorded about a file, checked against the file as it is now.</summary>
public static class ScannedFile
{
    /// <summary>True while the file is on disk with the size, and the modified time where one was recorded, that it was scanned with.</summary>
    public static bool IsIntact(Photo photo)
    {
        var file = new FileInfo(photo.SourcePath);
        return file.Exists && file.Length == photo.FileSizeBytes
            && (photo.FileModifiedUtc is not { } scanned || file.LastWriteTimeUtc == scanned);
    }
}
