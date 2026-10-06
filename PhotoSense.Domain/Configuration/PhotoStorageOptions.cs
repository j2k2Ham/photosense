namespace PhotoSense.Domain.Configuration;

/// <summary>Which copy to keep when a HEIC and a JPEG show the same shot at the same resolution.</summary>
public enum FormatPreference
{
    /// <summary>Keep what the camera recorded (HEIC): the better and smaller file. Needs HEIC support to view.</summary>
    CameraOriginal,
    /// <summary>Keep the JPEG, which opens on any device.</summary>
    WidelyCompatible
}

public class PhotoStorageOptions
{
    /// <summary>Folder, created inside each scanned root, that removed files are moved into. Never scanned.</summary>
    public const string RemovedFolderName = "_PhotoSense_Removed";

    public string PrimaryPath { get; set; } = string.Empty;
    public string SecondaryPath { get; set; } = string.Empty;
    public string DatabasePath { get; set; } = "photosense.db";
    /// <summary>Where preview images are cached. Defaults to a folder beside the database.</summary>
    public string ThumbnailPath { get; set; } = string.Empty;
    /// <summary>Set to CameraOriginal (PhotoStorage__KeepFormat) to keep HEIC originals instead of their JPEG conversions.</summary>
    public FormatPreference KeepFormat { get; set; } = FormatPreference.WidelyCompatible;

    public string ResolveThumbnailPath()
    {
        if (!string.IsNullOrWhiteSpace(ThumbnailPath)) return Path.GetFullPath(ThumbnailPath);
        var dbDirectory = Path.GetDirectoryName(Path.GetFullPath(DatabasePath)) ?? Directory.GetCurrentDirectory();
        return Path.Combine(dbDirectory, "photosense-thumbnails");
    }
}
