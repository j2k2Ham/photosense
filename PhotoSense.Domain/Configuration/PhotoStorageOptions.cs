namespace PhotoSense.Domain.Configuration;

/// <summary>Which copy to keep when a HEIC and a JPEG show the same shot at the same resolution.</summary>
public enum FormatPreference
{
    /// <summary>Keep what the camera recorded (HEIC): the better and smaller file. Needs HEIC support to view.</summary>
    CameraOriginal,
    /// <summary>Keep the JPEG, which opens on any device.</summary>
    WidelyCompatible
}

/// <summary>
/// Where the service keeps its own data, and the folders a scan starts from when none are named: the
/// <c>PhotoStorage</c> section of the settings.
/// </summary>
public class PhotoStorageOptions
{
    /// <summary>Folder, created inside each scanned root, that removed files are moved into. Never scanned.</summary>
    public const string RemovedFolderName = "_PhotoSense_Removed";

    /// <summary>The folder a scan covers when the request names none.</summary>
    public string PrimaryPath { get; set; } = string.Empty;
    /// <summary>A second folder scanned with the first when the request names none; blank for none.</summary>
    public string SecondaryPath { get; set; } = string.Empty;
    /// <summary>The database file. A path that is not absolute is taken from <see cref="DefaultDataFolder"/>.</summary>
    public string DatabasePath { get; set; } = "photosense.db";
    /// <summary>Where preview images are cached. Defaults to a folder beside the database.</summary>
    public string ThumbnailPath { get; set; } = string.Empty;
    /// <summary>Set to CameraOriginal (PhotoStorage__KeepFormat) to keep HEIC originals instead of their JPEG conversions.</summary>
    public FormatPreference KeepFormat { get; set; } = FormatPreference.WidelyCompatible;

    /// <summary>
    /// Where the database and the thumbnail cache are kept unless an absolute path says otherwise: a folder
    /// of the user's own, never the folder the service runs from. That folder is replaced whenever the program
    /// is built or published again, and the database and thumbnails would go with it.
    /// </summary>
    public static string DefaultDataFolder { get; } = DataFolderIn(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>The PhotoSense folder inside a user's application-data folder, or inside the temporary folder when there is none.</summary>
    /// <param name="userData">The user's application-data folder; empty for an account that has none.</param>
    public static string DataFolderIn(string userData)
        => Path.Combine(string.IsNullOrEmpty(userData) ? Path.GetTempPath() : userData, "PhotoSense");

    /// <summary>The database file's full path.</summary>
    public string ResolveDatabasePath() => Path.GetFullPath(DatabasePath, DefaultDataFolder);

    /// <summary>The folder the database is in.</summary>
    public string ResolveDatabaseFolder() => Path.GetDirectoryName(ResolveDatabasePath()) ?? DefaultDataFolder;

    /// <summary>The full path of the folder previews are cached in.</summary>
    public string ResolveThumbnailPath()
        => string.IsNullOrWhiteSpace(ThumbnailPath)
            ? Path.Combine(ResolveDatabaseFolder(), "photosense-thumbnails")
            : Path.GetFullPath(ThumbnailPath, DefaultDataFolder);
}
