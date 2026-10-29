using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Infrastructure.Library;

/// <summary>Offers the folder a phone's photo library is shown as, and the folders of PhotoSense's albums inside it.</summary>
public sealed class PhotoLibraryBrowser(PhotoLibraryFileSystem library) : IFolderBrowser
{
    public FolderListing Browse(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return new FolderListing(null, null, [new FolderEntry(Path.GetFileName(library.Root), library.Root)]);
        if (!library.FolderExists(path)) throw new DirectoryNotFoundException($"The photo library has no folder {path}.");
        var folder = Path.TrimEndingDirectorySeparator(path);
        // What was removed is not somewhere to put anything.
        var inside = library.FoldersIn(folder).Where(f => !Path.GetFileName(f).Equals(PhotoStorageOptions.RemovedFolderName, StringComparison.OrdinalIgnoreCase));
        var parent = PhotoPath.Comparer.Equals(folder, library.Root) ? null : Path.GetDirectoryName(folder);
        return new FolderListing(folder, parent, [.. inside.Select(f => new FolderEntry(Path.GetFileName(f), f))]);
    }
}
