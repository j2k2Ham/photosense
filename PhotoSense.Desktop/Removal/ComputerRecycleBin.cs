using System.Runtime.InteropServices;
using PhotoSense.Domain.Services;

namespace PhotoSense.Desktop.Removal;

/// <summary>
/// The Recycle Bin on Windows and the Trash on a Mac. A folder on a drive of the computer's own has one; on a Mac an
/// external disk has one too; a network share has none, nor, on Windows, a USB stick or memory card. Linux, which only
/// the checks run on, is taken to have none.
/// </summary>
public sealed class ComputerRecycleBin : IRecycleBin
{
    private readonly bool _windows;
    private readonly bool _mac;
    private readonly Func<IEnumerable<(string Root, DriveType Type)>> _drives;
    private readonly Action<IReadOnlyList<string>> _recycle;

    /// <summary>The bin of the computer this runs on.</summary>
    public ComputerRecycleBin() : this(RuntimeInformation.IsOSPlatform, Drives, NativeRecycleBin.Recycle) { }

    /// <summary>A bin as the system that <paramref name="runningOn"/> describes would have it.</summary>
    /// <param name="runningOn">Whether the computer runs a system; a test answers for another computer than its own.</param>
    /// <param name="drives">The drives there are, each by where it starts and what kind it is, as they are now.</param>
    /// <param name="recycle">Hands files to the system's bin; a test gives one of its own.</param>
    public ComputerRecycleBin(Func<OSPlatform, bool> runningOn, Func<IEnumerable<(string Root, DriveType Type)>> drives, Action<IReadOnlyList<string>> recycle)
    {
        _windows = runningOn(OSPlatform.Windows);
        _mac = !_windows && runningOn(OSPlatform.OSX);
        _drives = drives;
        _recycle = recycle;
    }

    /// <inheritdoc />
    public string Name => _mac ? "Trash" : "Recycle Bin";

    /// <inheritdoc />
    public bool Holds(string folder)
    {
        if (!_windows && !_mac) return false;
        // The drive a folder is on is the one whose start is the longest part of its path: on a Mac /Volumes/Card, not /.
        var drive = _drives().Where(d => Inside(folder, d.Root)).OrderByDescending(d => d.Root.Length).FirstOrDefault();
        return drive.Type == DriveType.Fixed || (_mac && drive.Type == DriveType.Removable);
    }

    /// <inheritdoc />
    public void Recycle(IReadOnlyList<string> paths) => _recycle(paths);

    // Whether a full path is the root's or inside it; both systems take no notice of case in a path, as people usually
    // set them up, and either slash divides it.
    private static bool Inside(string path, string root)
    {
        var start = root.TrimEnd('\\', '/');
        return path.StartsWith(start, StringComparison.OrdinalIgnoreCase) && (path.Length == start.Length || path[start.Length] is '\\' or '/');
    }

    private static IEnumerable<(string Root, DriveType Type)> Drives() => DriveInfo.GetDrives().Select(d => (d.RootDirectory.FullName, d.DriveType));
}
