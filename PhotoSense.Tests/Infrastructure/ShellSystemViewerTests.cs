using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Diagnostics;
using PhotoSense.Infrastructure.Viewing;
using Xunit;

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

    [Fact]
    public void On_Windows_The_File_Itself_Is_Handed_To_The_Shell()
    {
        var path = File_("IMG_1.HEIC");
        Viewer(Desktop.Windows).Open(path);
        var command = Assert.Single(_started);
        Assert.Equal((path, true), (command.FileName, command.UseShellExecute));
        Assert.Empty(command.ArgumentList);
    }

    [Theory]
    [InlineData(Desktop.MacOS, "open")]
    [InlineData(Desktop.Linux, "xdg-open")]
    public void Elsewhere_The_Desktops_Opener_Is_Run_With_The_Path_As_One_Argument(Desktop desktop, string opener)
    {
        // A name a shell would mangle: it must arrive whole.
        var path = File_("clip; rm -rf $HOME 'x'.mov");
        Viewer(desktop).Open(path);
        var command = Assert.Single(_started);
        Assert.Equal((opener, false), (command.FileName, command.UseShellExecute));
        Assert.Equal(path, Assert.Single(command.ArgumentList));
    }

    [Fact]
    public void A_Relative_Path_Is_Opened_By_Its_Full_Path()
    {
        var path = File_("IMG_2.JPG");
        var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), path);
        Viewer(Desktop.Windows).Open(relative);
        Assert.Equal(path, Assert.Single(_started).FileName);
    }

    [Fact]
    public void A_File_That_Is_Gone_Is_Not_Handed_To_Anything()
    {
        var missing = Path.Combine(_root.FullName, "gone.jpg");
        var error = Assert.Throws<FileNotFoundException>(() => Viewer(Desktop.Windows).Open(missing));
        Assert.Equal(missing, error.FileName);
        Assert.Empty(_started);
    }

    [Fact]
    public void The_Handle_Used_To_Start_The_Viewer_Is_Released()
    {
        var handle = new Handle();
        Viewer(Desktop.Windows, _ => handle).Open(File_("IMG_3.JPG"));
        Assert.True(handle.Disposed);
    }

    [Theory]
    [InlineData(typeof(Win32Exception))]            // no application is registered for the type
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(PlatformNotSupportedException))]
    public void A_Viewer_That_Cannot_Be_Started_Is_Reported_By_File_Name(Type failure)
    {
        var path = File_("IMG_4.HEIC");
        var viewer = Viewer(Desktop.Windows, _ => throw (Exception)Activator.CreateInstance(failure)!);
        var error = Assert.Throws<InvalidOperationException>(() => viewer.Open(path));
        Assert.StartsWith("No application could be started for IMG_4.HEIC", error.Message);
        Assert.IsType(failure, error.InnerException);
    }

    [Fact]
    public void Other_Failures_Are_Not_Dressed_Up_As_A_Missing_Viewer()
    {
        var viewer = Viewer(Desktop.Windows, _ => throw new OutOfMemoryException());
        Assert.Throws<OutOfMemoryException>(() => viewer.Open(File_("IMG_5.JPG")));
    }

    [Fact]
    public void Knows_The_Desktop_It_Is_Running_On()
    {
        var expected = OperatingSystem.IsWindows() ? Desktop.Windows : OperatingSystem.IsMacOS() ? Desktop.MacOS : Desktop.Linux;
        Assert.Equal(expected, ShellSystemViewer.CurrentDesktop());
        // The real viewer still refuses a missing file before starting anything.
        Assert.Throws<FileNotFoundException>(() => new ShellSystemViewer().Open(Path.Combine(_root.FullName, "gone.jpg")));
    }


    [Fact]
    public void Tells_The_Desktops_Apart()
    {
        Assert.Equal(Desktop.Windows, ShellSystemViewer.DesktopOf(os => os == OSPlatform.Windows));
        Assert.Equal(Desktop.MacOS, ShellSystemViewer.DesktopOf(os => os == OSPlatform.OSX));
        Assert.Equal(Desktop.Linux, ShellSystemViewer.DesktopOf(os => os == OSPlatform.Linux));
        Assert.Equal(Desktop.Linux, ShellSystemViewer.DesktopOf(os => os == OSPlatform.FreeBSD)); // any other desktop follows the same convention
    }

    private sealed class Handle : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
