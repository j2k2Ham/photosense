using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Diagnostics;
using PhotoSense.Infrastructure.Viewing;

namespace PhotoSense.Tests.Infrastructure;

public sealed class ShellSystemViewerTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("photosense-viewer-");
    private readonly List<ProcessStartInfo> _started = [];

    public void Dispose() => _root.Delete(true);

    private string File_(string name)
    {
        var path = Path.Combine(_root.FullName, name);
        File.WriteAllText(path, "x");
        return path;
    }

    // Nothing is really opened: the command that would be run is recorded instead.
    private ShellSystemViewer Viewer(Desktop desktop, Func<ProcessStartInfo, IDisposable?>? start = null)
        => new(desktop, start ?? (info => { _started.Add(info); return null; }));

    [Test]
    public async Task On_Windows_The_File_Itself_Is_Handed_To_The_Shell()
    {
        var path = File_("IMG_1.HEIC");
        Viewer(Desktop.Windows).Open(path);
        var command = _started.Single();
        await Assert.That((command.FileName, command.UseShellExecute)).IsEqualTo((path, true));
        await Assert.That(command.ArgumentList).IsEmpty();
    }

    [Test]
    [Arguments(Desktop.MacOS, "open")]
    [Arguments(Desktop.Linux, "xdg-open")]
    public async Task Elsewhere_The_Desktops_Opener_Is_Run_With_The_Path_As_One_Argument(Desktop desktop, string opener)
    {
        // A name a shell would mangle: it must arrive whole.
        var path = File_("clip; rm -rf $HOME 'x'.mov");
        Viewer(desktop).Open(path);
        var command = _started.Single();
        await Assert.That((command.FileName, command.UseShellExecute)).IsEqualTo((opener, false));
        await Assert.That(command.ArgumentList.Single()).IsEqualTo(path);
    }

    [Test]
    public async Task A_Relative_Path_Is_Opened_By_Its_Full_Path()
    {
        var path = File_("IMG_2.JPG");
        var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), path);
        Viewer(Desktop.Windows).Open(relative);
        // The full path is built on the folder the tests were started from, and a terminal may spell its drive "c:".
        await Assert.That(_started.Single().FileName).IsEqualTo(path).IgnoringCase();
    }

    [Test]
    public async Task A_File_That_Is_Gone_Is_Not_Handed_To_Anything()
    {
        var missing = Path.Combine(_root.FullName, "gone.jpg");
        var error = Assert.ThrowsExactly<FileNotFoundException>(() => Viewer(Desktop.Windows).Open(missing));
        await Assert.That(error.FileName).IsEqualTo(missing);
        await Assert.That(_started).IsEmpty();
    }

    [Test]
    public async Task The_Handle_Used_To_Start_The_Viewer_Is_Released()
    {
        var handle = new Handle();
        Viewer(Desktop.Windows, _ => handle).Open(File_("IMG_3.JPG"));
        await Assert.That(handle.Disposed).IsTrue();
    }

    [Test]
    [Arguments(typeof(Win32Exception))]            // no application is registered for the type
    [Arguments(typeof(InvalidOperationException))]
    [Arguments(typeof(PlatformNotSupportedException))]
    public async Task A_Viewer_That_Cannot_Be_Started_Is_Reported_By_File_Name(Type failure)
    {
        var path = File_("IMG_4.HEIC");
        var viewer = Viewer(Desktop.Windows, _ => throw (Exception)Activator.CreateInstance(failure)!);
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => viewer.Open(path));
        await Assert.That(error.Message).StartsWith("No application could be started for IMG_4.HEIC");
        await Assert.That(error.InnerException!.GetType()).IsEqualTo(failure);
    }

    [Test]
    public void Other_Failures_Are_Not_Dressed_Up_As_A_Missing_Viewer()
    {
        var viewer = Viewer(Desktop.Windows, _ => throw new OutOfMemoryException());
        Assert.ThrowsExactly<OutOfMemoryException>(() => viewer.Open(File_("IMG_5.JPG")));
    }

    [Test]
    public async Task Knows_The_Desktop_It_Is_Running_On()
    {
        var expected = OperatingSystem.IsWindows() ? Desktop.Windows : OperatingSystem.IsMacOS() ? Desktop.MacOS : Desktop.Linux;
        await Assert.That(ShellSystemViewer.CurrentDesktop()).IsEqualTo(expected);
        // The real viewer still refuses a missing file before starting anything.
        Assert.ThrowsExactly<FileNotFoundException>(() => new ShellSystemViewer().Open(Path.Combine(_root.FullName, "gone.jpg")));
    }


    [Test]
    public async Task Tells_The_Desktops_Apart()
    {
        await Assert.That(ShellSystemViewer.DesktopOf(os => os == OSPlatform.Windows)).IsEqualTo(Desktop.Windows);
        await Assert.That(ShellSystemViewer.DesktopOf(os => os == OSPlatform.OSX)).IsEqualTo(Desktop.MacOS);
        await Assert.That(ShellSystemViewer.DesktopOf(os => os == OSPlatform.Linux)).IsEqualTo(Desktop.Linux);
        await Assert.That(ShellSystemViewer.DesktopOf(os => os == OSPlatform.FreeBSD)).IsEqualTo(Desktop.Linux); // any other desktop follows the same convention
    }

    private sealed class Handle : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
