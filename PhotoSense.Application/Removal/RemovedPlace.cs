using PhotoSense.Domain.Configuration;

namespace PhotoSense.Application.Removal;

/// <summary>
/// Where a removed file waits: in the folder that holds removed files inside the folder it was removed from,
/// keeping its place below that folder, so that putting it back is a matter of moving it up.
/// </summary>
public static class RemovedPlace
{
    /// <param name="root">The folder scanned or organized. A file outside it, or one with no such folder known, is held beside itself.</param>
    public static string For(string path, string? root)
    {
        var source = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(source)!;
        var top = string.IsNullOrWhiteSpace(root) ? folder : Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var relative = Path.GetRelativePath(top, source);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? Path.Combine(folder, PhotoStorageOptions.RemovedFolderName, Path.GetFileName(source))
            : Path.Combine(top, PhotoStorageOptions.RemovedFolderName, relative);
    }
}
