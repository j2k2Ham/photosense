using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Application.Scanning;

/// <summary>The pictures and videos under a folder, as a scan and Organize take them in.</summary>
internal static class PhotoFileEnumerator
{
    /// <summary>Whether a scan takes the file in: a picture or a video.</summary>
    public static bool IsSupported(string path) => MediaFiles.IsScanned(path);

    /// <summary>
    /// The pictures and videos in a folder, and with <paramref name="recursive"/> in the folders inside it, leaving out
    /// the folders of removed files and the libraries of photo programs. None when the folder is not there.
    /// </summary>
    public static IEnumerable<string> Enumerate(IPhotoFileSystem files, string root, bool recursive)
    {
        // Inside a photo program's own library every file belongs to that program.
        if (string.IsNullOrWhiteSpace(root) || !files.FolderExists(root) || PhotoLibraries.IsInLibrary(Path.GetFullPath(root))) yield break;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var file in files.FilesIn(directory))
                if (IsSupported(file)) yield return file;
            if (!recursive) continue;
            // A link to another folder is not among them: it would list the same files under a second path,
            // making each look like a duplicate of itself.
            foreach (var sub in files.FoldersIn(directory))
            {
                // Removed files wait here; scanning them would report them as duplicates again.
                if (Path.GetFileName(sub).Equals(PhotoStorageOptions.RemovedFolderName, StringComparison.OrdinalIgnoreCase)) continue;
                if (PhotoLibraries.IsLibrary(Path.GetFileName(sub))) continue;
                pending.Push(sub);
            }
        }
    }
}
