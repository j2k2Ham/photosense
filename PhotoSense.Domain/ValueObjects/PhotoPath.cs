using System.Runtime.InteropServices;

namespace PhotoSense.Domain.ValueObjects;

/// <summary>Identity of a file on disk: two spellings of the same path yield the same key.</summary>
public static class PhotoPath
{
    private static readonly bool CaseInsensitive = IgnoresCase(RuntimeInformation.IsOSPlatform);

    /// <summary>Compares paths as this system's disks do: without regard to case on Windows and macOS.</summary>
    public static StringComparer Comparer { get; } = ComparerFor(CaseInsensitive);

    /// <summary>The path's full form, in one case where this system's disks ignore case, so that two spellings of one file match.</summary>
    public static string Key(string path) => Key(path, CaseInsensitive);

    /// <summary>Windows and macOS take two names differing only in case for the same file; Linux does not.</summary>
    public static bool IgnoresCase(Func<OSPlatform, bool> runningOn) => runningOn(OSPlatform.Windows) || runningOn(OSPlatform.OSX);

    /// <summary>A comparer that ignores case, or one that does not.</summary>
    public static StringComparer ComparerFor(bool ignoreCase) => ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>The path's full form, upper-cased when case is to be ignored.</summary>
    /// <param name="path">The path, full or relative to the current folder.</param>
    /// <param name="ignoreCase">Whether the disks it is on ignore case.</param>
    public static string Key(string path, bool ignoreCase)
    {
        var full = Path.GetFullPath(path);
        return ignoreCase ? full.ToUpperInvariant() : full;
    }
}
