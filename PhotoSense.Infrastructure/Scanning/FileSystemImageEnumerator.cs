namespace PhotoSense.Infrastructure.Scanning;

public static class FileSystemImageEnumerator
{
    private static readonly string[] _extensions = [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp"];

    public static IEnumerable<string> Enumerate(string root, bool recursive)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) yield break;
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var file in Directory.EnumerateFiles(root, "*.*", option))
        {
            if (_extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                yield return file;
        }
    }
}
