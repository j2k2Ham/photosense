using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Desktop.Browsing;

/// <summary>The folders of the computer the service runs on, for the page to offer to scan or organize.</summary>
public sealed class FileSystemFolderBrowser : IFolderBrowser
{
    private readonly Func<Environment.SpecialFolder, string> _folder;
    private readonly Func<IEnumerable<(string Mount, DriveType Type)>> _drives;
    private readonly bool _windows;
    private readonly Func<string, bool> _exists;

    /// <summary>Starts from this computer's pictures and home folders and every disk it has.</summary>
    public FileSystemFolderBrowser()
        : this(Environment.GetFolderPath, () => DriveInfo.GetDrives().Select(d => (d.Name, d.DriveType)), OperatingSystem.IsWindows(), Directory.Exists) { }

    /// <summary>Starts from what a computer says it has; a test answers for another computer than its own.</summary>
    /// <param name="folder">Where one of the person's own folders is, or empty when there is none.</param>
    /// <param name="drives">Every disk the system has mounted, where it is mounted, and what kind it is.</param>
    /// <param name="windows">On Windows each disk has a letter; elsewhere the system's own volumes are mounted beside the person's.</param>
    /// <param name="exists">Whether a folder is there to be opened.</param>
    public FileSystemFolderBrowser(Func<Environment.SpecialFolder, string> folder, Func<IEnumerable<(string Mount, DriveType Type)>> drives, bool windows,
        Func<string, bool> exists)
    {
        _folder = folder;
        _drives = drives;
        _windows = windows;
        _exists = exists;
    }

    /// <inheritdoc />
    public FolderListing Browse(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return new FolderListing(null, null, StartingPlaces().ToList());
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

    // Where browsing starts: the person's pictures and home folders, then every disk, each when it is there.
    private IEnumerable<FolderEntry> StartingPlaces()
        => new[] { ("Pictures", _folder(Environment.SpecialFolder.MyPictures)), ("Home", _folder(Environment.SpecialFolder.UserProfile)) }
            .Concat(Disks())
            .Where(p => _exists(p.Item2))
            .Select(p => new FolderEntry(p.Item1, p.Item2));

    // The disks a person would look for photos on, each with the name it is offered under: on Windows its letter,
    // elsewhere "Computer" for the whole disk and the name of each disk plugged in or shared.
    private IEnumerable<(string, string)> Disks()
        => _drives().Where(d => HoldsFiles(d.Type) && (_windows || d.Mount == "/" || IsPluggedIn(d.Mount)))
            .Select(d => (_windows ? d.Mount : d.Mount == "/" ? "Computer" : Path.GetFileName(d.Mount.TrimEnd('/')), d.Mount));

    // Where a Mac or a Linux machine mounts a disk that is plugged in or shared: "/Volumes/Photos".
    private static bool IsPluggedIn(string mount)
        => new[] { "/Volumes/", "/media/", "/mnt/", "/run/media/" }.Any(at => mount.StartsWith(at, StringComparison.Ordinal));

    // A drive a person keeps files on, rather than a system's own bookkeeping.
    private static bool HoldsFiles(DriveType type) => type is DriveType.Fixed or DriveType.Removable or DriveType.Network;
}
