namespace PhotoSense.Domain.Services;

/// <summary>A folder on the machine the service runs on.</summary>
/// <param name="Name">What the folder is called, as shown.</param>
/// <param name="Path">Its full path.</param>
public sealed record FolderEntry(string Name, string Path);

/// <summary>The folders inside one folder. With no <see cref="Path"/>, the places browsing starts from.</summary>
/// <param name="Path">The folder listed; null for the places browsing starts from.</param>
/// <param name="Parent">The folder above it; null at the top.</param>
/// <param name="Folders">The folders inside it.</param>
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
