using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Library;

/// <summary>Offers the one folder a phone's photo library is shown as, to be scanned and organized.</summary>
public sealed class PhotoLibraryBrowser(PhotoLibraryFileSystem library) : IFolderBrowser
{
    public FolderListing Browse(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return new FolderListing(null, null, [new FolderEntry(Path.GetFileName(library.Root), library.Root)]);
        if (library.FolderExists(path)) return new FolderListing(library.Root, null, []);
        throw new DirectoryNotFoundException($"The photo library has no folder {path}.");
    }
}
