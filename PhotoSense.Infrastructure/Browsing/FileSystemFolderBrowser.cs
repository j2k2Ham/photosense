using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Browsing;

public sealed class FileSystemFolderBrowser : IFolderBrowser
{
    private readonly Func<IEnumerable<FolderEntry>> _startingPlaces;

    public FileSystemFolderBrowser() : this(StartingPlaces) { }

    /// <param name="startingPlaces">What is offered before any folder has been opened.</param>
    public FileSystemFolderBrowser(Func<IEnumerable<FolderEntry>> startingPlaces) => _startingPlaces = startingPlaces;

    public FolderListing Browse(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return new FolderListing(null, null, _startingPlaces().ToList());
        // Without a trailing separator, so that the path handed back is the one a person would type.
        var folder = new DirectoryInfo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));
        if (!folder.Exists) throw new DirectoryNotFoundException($"Folder not found: {path}");
        return new FolderListing(folder.FullName, folder.Parent?.FullName, Subfolders(folder));
    }

    private static List<FolderEntry> Subfolders(DirectoryInfo folder)
    {
        try
        {
            return folder.EnumerateDirectories()
                .Where(IsOffered)
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(d => new FolderEntry(d.Name, d.FullName))
                .ToList();
        }
        // A folder that may not be looked into is shown as having nothing in it.
        catch (UnauthorizedAccessException) { return []; }
    }

    // Hidden and system folders hold no photo library, and removed files are never scanned.
    private static bool IsOffered(DirectoryInfo folder)
        => (folder.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0
           && !folder.Name.Equals(PhotoStorageOptions.RemovedFolderName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Where browsing starts: the user's pictures and home folders, then every disk.</summary>
    public static IEnumerable<FolderEntry> StartingPlaces()
        => Existing([
            ("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            ("Home", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
            .. DriveInfo.GetDrives().Where(d => HoldsFiles(d.DriveType)).Select(d => (d.Name, d.Name))
        ]);

    /// <summary>Of the places offered, those that are there to be opened.</summary>
    public static IEnumerable<FolderEntry> Existing(IEnumerable<(string Name, string Path)> places)
        => places.Where(p => Directory.Exists(p.Path)).Select(p => new FolderEntry(p.Name, p.Path));

    /// <summary>Whether a drive is one a person keeps files on, rather than a system's own bookkeeping.</summary>
    public static bool HoldsFiles(DriveType type) => type is DriveType.Fixed or DriveType.Removable or DriveType.Network;
}
