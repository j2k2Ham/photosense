using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Application.Scanning;

public static class PhotoFileEnumerator
{
    public static bool IsSupported(string path) => MediaFiles.IsScanned(path);

    public static IEnumerable<string> Enumerate(string root, bool recursive)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) yield break;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var file in ReadDirectory(directory, Directory.EnumerateFiles))
                if (IsSupported(file)) yield return file;
            if (!recursive) continue;
            foreach (var sub in ReadDirectory(directory, Directory.EnumerateDirectories))
            {
                // Removed files wait here; scanning them would report them as duplicates again.
                if (Path.GetFileName(sub).Equals(PhotoStorageOptions.RemovedFolderName, StringComparison.OrdinalIgnoreCase)) continue;
                // A link to another folder would list the same files under a second path,
                // making each look like a duplicate of itself.
                if (new DirectoryInfo(sub).LinkTarget is not null) continue;
                pending.Push(sub);
            }
        }
    }

    private static List<string> ReadDirectory(string directory, Func<string, IEnumerable<string>> read)
    {
        try { return read(directory).ToList(); }
        catch (UnauthorizedAccessException) { return []; }
        catch (IOException) { return []; }
    }
}
