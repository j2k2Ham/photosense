using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using PhotoSense.Domain.Services;

namespace PhotoSense.Desktop.Viewing;

/// <summary>Hands a file to the operating system to open, as double-clicking it would.</summary>
public sealed class ShellSystemViewer : ISystemViewer
{
    // Which desktop the service is running on, as far as opening a file is concerned.
    private enum DesktopSystem
    {
        // Windows, whose shell opens a file itself.
        Windows,
        // macOS, which opens a file with "open".
        MacOS,
        // Linux and the rest, which open a file with "xdg-open".
        Linux
    }

    private readonly DesktopSystem _desktop;
    private readonly Func<ProcessStartInfo, IDisposable?> _start;

    /// <summary>Opens files on the desktop this computer has.</summary>
    public ShellSystemViewer() : this(RuntimeInformation.IsOSPlatform, Process.Start) { }

    /// <summary>Opens files as the system that <paramref name="runningOn"/> describes would, starting each opener with <paramref name="start"/>.</summary>
    /// <param name="runningOn">Whether the computer runs a system; a test answers for another computer than its own.</param>
    /// <param name="start">Starts a process; a test gives one that opens nothing.</param>
    public ShellSystemViewer(Func<OSPlatform, bool> runningOn, Func<ProcessStartInfo, IDisposable?> start)
    {
        _desktop = runningOn(OSPlatform.Windows) ? DesktopSystem.Windows : runningOn(OSPlatform.OSX) ? DesktopSystem.MacOS : DesktopSystem.Linux;
        _start = start;
    }

    /// <inheritdoc />
    /// <exception cref="FileNotFoundException">The file is not there.</exception>
    /// <exception cref="InvalidOperationException">No application could be started for it.</exception>
    public void Open(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new FileNotFoundException("The file is no longer there.", full);
        try
        {
            // The viewer runs on its own; only the handle used to start it is released here.
            using var started = _start(CommandFor(full, _desktop));
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            throw new InvalidOperationException($"No application could be started for {Path.GetFileName(full)}: {ex.Message}", ex);
        }
    }

    // What to run to open a file: the file itself through the Windows shell, or the desktop's opener elsewhere.
    private static ProcessStartInfo CommandFor(string path, DesktopSystem desktop)
    {
        if (desktop == DesktopSystem.Windows) return new ProcessStartInfo(path) { UseShellExecute = true };
        // The path is passed as one argument, never through a shell, so nothing in a file name is interpreted.
        var info = new ProcessStartInfo(desktop == DesktopSystem.MacOS ? "open" : "xdg-open") { UseShellExecute = false };
        info.ArgumentList.Add(path);
        return info;
    }

}
