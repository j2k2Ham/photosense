using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Viewing;

/// <summary>Which desktop the service is running on, as far as opening a file is concerned.</summary>
public enum Desktop { Windows, MacOS, Linux }

/// <summary>Hands a file to the operating system to open, as double-clicking it would.</summary>
public sealed class ShellSystemViewer : ISystemViewer
{
    private readonly Desktop _desktop;
    private readonly Func<ProcessStartInfo, IDisposable?> _start;

    public ShellSystemViewer() : this(CurrentDesktop(), Process.Start) { }

    /// <param name="start">Starts the process; replaced in tests so that nothing is really opened.</param>
    public ShellSystemViewer(Desktop desktop, Func<ProcessStartInfo, IDisposable?> start)
    {
        _desktop = desktop;
        _start = start;
    }

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
    public static ProcessStartInfo CommandFor(string path, Desktop desktop)
    {
        if (desktop == Desktop.Windows) return new ProcessStartInfo(path) { UseShellExecute = true };
        // The path is passed as one argument, never through a shell, so nothing in a file name is interpreted.
        var info = new ProcessStartInfo(desktop == Desktop.MacOS ? "open" : "xdg-open") { UseShellExecute = false };
        info.ArgumentList.Add(path);
        return info;
    }

    public static Desktop CurrentDesktop() => DesktopOf(RuntimeInformation.IsOSPlatform);

    public static Desktop DesktopOf(Func<OSPlatform, bool> runningOn)
        => runningOn(OSPlatform.Windows) ? Desktop.Windows : runningOn(OSPlatform.OSX) ? Desktop.MacOS : Desktop.Linux;
}
