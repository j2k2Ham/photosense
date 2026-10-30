using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Infrastructure.Library;

/// <summary>Offers the folder a phone's photos are shown as, and the folders inside it: its albums on an iPhone, its own folders on Android.</summary>
/// <param name="root">Where the phone's photos are shown, as a folder.</param>
public sealed class PhotoLibraryBrowser(IPhotoFileSystem library, string root) : IFolderBrowser
{
    public FolderListing Browse(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return new FolderListing(null, null, [new FolderEntry(Path.GetFileName(root), root)]);
        if (!library.FolderExists(path)) throw new DirectoryNotFoundException($"The photo library has no folder {path}.");
        var folder = Path.TrimEndingDirectorySeparator(path);
        // What was removed is not somewhere to put anything.
        var inside = library.FoldersIn(folder).Where(f => !Path.GetFileName(f).Equals(PhotoStorageOptions.RemovedFolderName, StringComparison.OrdinalIgnoreCase));
        var parent = PhotoPath.Comparer.Equals(folder, root) ? null : Path.GetDirectoryName(folder);
        return new FolderListing(folder, parent, [.. inside.Select(f => new FolderEntry(Path.GetFileName(f), f))]);
    }
}
