namespace PhotoSense.Domain.Configuration;

public class PhotoStorageOptions
{
    /// <summary>Folder, created inside each scanned root, that removed files are moved into. Never scanned.</summary>
    public const string RemovedFolderName = "_PhotoSense_Removed";

    public string PrimaryPath { get; set; } = string.Empty;
    public string SecondaryPath { get; set; } = string.Empty;
    public string DatabasePath { get; set; } = "photosense.db";
    /// <summary>Where preview images are cached. Defaults to a folder beside the database.</summary>
    public string ThumbnailPath { get; set; } = string.Empty;

    public string ResolveThumbnailPath()
    {
        if (!string.IsNullOrWhiteSpace(ThumbnailPath)) return Path.GetFullPath(ThumbnailPath);
        var dbDirectory = Path.GetDirectoryName(Path.GetFullPath(DatabasePath)) ?? Directory.GetCurrentDirectory();
        return Path.Combine(dbDirectory, "photosense-thumbnails");
    }
}
