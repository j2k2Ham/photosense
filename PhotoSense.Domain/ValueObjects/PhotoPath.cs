using System.Runtime.InteropServices;

namespace PhotoSense.Domain.ValueObjects;

/// <summary>Identity of a file on disk: two spellings of the same path yield the same key.</summary>
public static class PhotoPath
{
    private static readonly bool CaseInsensitive = IgnoresCase(RuntimeInformation.IsOSPlatform);

    public static StringComparer Comparer { get; } = ComparerFor(CaseInsensitive);

    public static string Key(string path) => Key(path, CaseInsensitive);

    /// <summary>Windows and macOS take two names differing only in case for the same file; Linux does not.</summary>
    public static bool IgnoresCase(Func<OSPlatform, bool> runningOn) => runningOn(OSPlatform.Windows) || runningOn(OSPlatform.OSX);

    public static StringComparer ComparerFor(bool ignoreCase) => ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static string Key(string path, bool ignoreCase)
    {
        var full = Path.GetFullPath(path);
        return ignoreCase ? full.ToUpperInvariant() : full;
    }
}
