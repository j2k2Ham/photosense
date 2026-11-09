using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using PhotoSense.Domain.Services;

namespace PhotoSense.Desktop.Viewing;

/// <summary>Which desktop the service is running on, as far as opening a file is concerned.</summary>
internal enum DesktopSystem
{
    /// <summary>Windows, whose shell opens a file itself.</summary>
    Windows,
    /// <summary>macOS, which opens a file with <c>open</c>.</summary>
    MacOS,
    /// <summary>Linux and the rest, which open a file with <c>xdg-open</c>.</summary>
    Linux
}

/// <summary>Hands a file to the operating system to open, as double-clicking it would.</summary>
public sealed class ShellSystemViewer : ISystemViewer
{
    private readonly DesktopSystem _desktop;
    private readonly Func<ProcessStartInfo, IDisposable?> _start;

    /// <summary>Opens files on the desktop this computer has.</summary>
    public ShellSystemViewer() : this(CurrentDesktop(), Process.Start) { }

    /// <summary>Opens files on the desktop given, starting the opener as given.</summary>
    /// <param name="desktop">The desktop whose way of opening a file is used.</param>
    /// <param name="start">Starts the process; replaced in tests so that nothing is really opened.</param>
    internal ShellSystemViewer(DesktopSystem desktop, Func<ProcessStartInfo, IDisposable?> start)
    {
        _desktop = desktop;
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

    /// <summary>What to run to open a file: the file itself through the Windows shell, or the desktop's opener elsewhere.</summary>
    internal static ProcessStartInfo CommandFor(string path, DesktopSystem desktop)
    {
        if (desktop == DesktopSystem.Windows) return new ProcessStartInfo(path) { UseShellExecute = true };
        // The path is passed as one argument, never through a shell, so nothing in a file name is interpreted.
        var info = new ProcessStartInfo(desktop == DesktopSystem.MacOS ? "open" : "xdg-open") { UseShellExecute = false };
        info.ArgumentList.Add(path);
        return info;
    }

    /// <summary>The desktop this computer has.</summary>
    internal static DesktopSystem CurrentDesktop() => DesktopOf(RuntimeInformation.IsOSPlatform);

    /// <summary>The desktop of a system, asked about through <paramref name="runningOn"/>, so that each answer can be tried on one computer.</summary>
    internal static DesktopSystem DesktopOf(Func<OSPlatform, bool> runningOn)
        => runningOn(OSPlatform.Windows) ? DesktopSystem.Windows : runningOn(OSPlatform.OSX) ? DesktopSystem.MacOS : DesktopSystem.Linux;
}
