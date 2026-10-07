namespace PhotoSense.Domain.Services;

/// <summary>A folder on the machine the service runs on.</summary>
public sealed record FolderEntry(string Name, string Path);

/// <summary>The folders inside one folder. With no <see cref="Path"/>, the places browsing starts from.</summary>
public sealed record FolderListing(string? Path, string? Parent, IReadOnlyList<FolderEntry> Folders);

/// <summary>
/// Lets the UI offer the folders of the machine that holds the photos. A browser's own folder dialog
/// gives a web page the folder's name but never its full path, which is what a scan needs.
/// </summary>
public interface IFolderBrowser
{
    /// <summary>Lists the folders inside <paramref name="path"/>, or the places to start from when it is blank.</summary>
    /// <exception cref="DirectoryNotFoundException">There is no such folder.</exception>
    FolderListing Browse(string? path);
}
