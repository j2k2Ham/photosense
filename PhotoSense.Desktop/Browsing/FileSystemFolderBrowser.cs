using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Desktop.Browsing;

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

    // Hidden and system folders hold no photo library of the user's, removed files are never scanned, and a
    // photo program's own library is that program's to change.
    private static bool IsOffered(DirectoryInfo folder)
        => (folder.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0
           && !folder.Name.Equals(PhotoStorageOptions.RemovedFolderName, StringComparison.OrdinalIgnoreCase)
           && !PhotoLibraries.IsLibrary(folder.Name);

    /// <summary>Where browsing starts: the user's pictures and home folders, then every disk.</summary>
    public static IEnumerable<FolderEntry> StartingPlaces()
        => Existing([
            ("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            ("Home", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
            .. Disks(DriveInfo.GetDrives().Select(d => (d.Name, d.DriveType)), OperatingSystem.IsWindows())
        ]);

    /// <summary>The disks a person would look for photos on, each with the name it is offered under.</summary>
    /// <param name="drives">Every disk the system has mounted, where it is mounted, and what kind it is.</param>
    /// <param name="windows">On Windows each disk has a letter; elsewhere the system's own volumes are mounted beside the person's.</param>
    public static IEnumerable<(string Name, string Path)> Disks(IEnumerable<(string Mount, DriveType Type)> drives, bool windows)
        => drives.Where(d => HoldsFiles(d.Type) && (windows || d.Mount == "/" || IsPluggedIn(d.Mount)))
            .Select(d => (windows ? d.Mount : d.Mount == "/" ? "Computer" : Path.GetFileName(d.Mount.TrimEnd('/')), d.Mount));

    // Where a Mac or a Linux machine mounts a disk that is plugged in or shared: "/Volumes/Photos".
    private static bool IsPluggedIn(string mount)
        => new[] { "/Volumes/", "/media/", "/mnt/", "/run/media/" }.Any(at => mount.StartsWith(at, StringComparison.Ordinal));

    /// <summary>Of the places offered, those that are there to be opened.</summary>
    public static IEnumerable<FolderEntry> Existing(IEnumerable<(string Name, string Path)> places)
        => places.Where(p => Directory.Exists(p.Path)).Select(p => new FolderEntry(p.Name, p.Path));

    /// <summary>Whether a drive is one a person keeps files on, rather than a system's own bookkeeping.</summary>
    public static bool HoldsFiles(DriveType type) => type is DriveType.Fixed or DriveType.Removable or DriveType.Network;
}
