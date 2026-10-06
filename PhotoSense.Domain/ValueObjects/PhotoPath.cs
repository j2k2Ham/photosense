namespace PhotoSense.Domain.ValueObjects;

/// <summary>Identity of a file on disk: two spellings of the same path yield the same key.</summary>
public static class PhotoPath
{
    private static readonly bool CaseInsensitive = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public static StringComparer Comparer { get; } = CaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static string Key(string path)
    {
        var full = Path.GetFullPath(path);
        return CaseInsensitive ? full.ToUpperInvariant() : full;
    }
}
