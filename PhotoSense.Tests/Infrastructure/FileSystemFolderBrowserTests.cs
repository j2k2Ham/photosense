using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Browsing;
using Xunit;

namespace PhotoSense.Tests.Infrastructure;

public sealed class FileSystemFolderBrowserTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("photosense-browse-");
    private readonly FileSystemFolderBrowser _browser = new();

    public void Dispose() => _root.Delete(true);

    private string Folder(params string[] parts)
    {
        var path = Path.Combine([_root.FullName, .. parts]);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void Lists_The_Folders_Inside_A_Folder_By_Name_With_Their_Full_Paths()
    {
        var phone = Folder("Phone Pictures");
        var backup = Folder("backup");
        Folder("Albums", "2024");
        File.WriteAllText(Path.Combine(_root.FullName, "notes.txt"), "a file is not a folder");

        var listing = _browser.Browse(_root.FullName);

        Assert.Equal(_root.FullName, listing.Path);
        Assert.Equal(_root.Parent!.FullName, listing.Parent);
        Assert.Equal(new[] { "Albums", "backup", "Phone Pictures" }, listing.Folders.Select(f => f.Name));
        Assert.Equal(new[] { Path.Combine(_root.FullName, "Albums"), backup, phone }, listing.Folders.Select(f => f.Path));
    }

    [Fact]
    public void A_Path_Is_Listed_Under_Its_Full_Spelling_However_It_Was_Asked_For()
    {
        Folder("Albums", "2024");
        var roundabout = Path.Combine(_root.FullName, "Albums", "..", "Albums") + Path.DirectorySeparatorChar;

        var listing = _browser.Browse(roundabout);

        Assert.Equal(Path.Combine(_root.FullName, "Albums"), listing.Path);
        Assert.Equal("2024", Assert.Single(listing.Folders).Name);
    }

    [Fact]
    public void Leaves_Out_Hidden_Folders_And_The_Folder_Removed_Files_Are_Held_In()
    {
        Folder("Albums");
        Folder(PhotoStorageOptions.RemovedFolderName);
        Folder(PhotoStorageOptions.RemovedFolderName.ToUpperInvariant() + "-not-that-one");
        // A leading dot hides a folder on Linux and macOS; the attribute does on Windows.
        var hidden = new DirectoryInfo(Folder(".thumbnails"));
        hidden.Attributes |= FileAttributes.Hidden;

        var names = _browser.Browse(_root.FullName).Folders.Select(f => f.Name);

        Assert.Equal(new[] { PhotoStorageOptions.RemovedFolderName.ToUpperInvariant() + "-not-that-one", "Albums" }.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), names);
    }

    [Fact]
    public void The_Top_Of_A_Disk_Has_No_Folder_Above_It()
    {
        var top = Path.GetPathRoot(_root.FullName)!;
        var listing = _browser.Browse(top);
        Assert.Equal(top, listing.Path);
        Assert.Null(listing.Parent);
        Assert.NotEmpty(listing.Folders);
    }

    [Fact]
    public void A_Folder_That_May_Not_Be_Looked_Into_Is_Shown_As_Empty()
    {
        var locked = Folder("private");
        Folder("private", "inside");
        using var denied = TestFiles.DenyAccess(locked);
        if (denied is null) return; // nothing is out of reach of the account running this

        var listing = _browser.Browse(locked);

        Assert.Equal(locked, listing.Path);
        Assert.Empty(listing.Folders);
    }

    [Fact]
    public void A_Folder_That_Is_Not_There_Is_Reported()
    {
        var missing = Path.Combine(_root.FullName, "no-such-folder");
        var error = Assert.Throws<DirectoryNotFoundException>(() => _browser.Browse(missing));
        Assert.Contains(missing, error.Message);

        // A file is not a folder either.
        var file = Path.Combine(_root.FullName, "photo.jpg");
        File.WriteAllText(file, "x");
        Assert.Throws<DirectoryNotFoundException>(() => _browser.Browse(file));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void With_No_Folder_Named_It_Offers_The_Places_To_Start_From(string? path)
    {
        var places = new[] { new FolderEntry("Pictures", Folder("Pictures")), new FolderEntry("C:\\", _root.FullName) };
        var listing = new FileSystemFolderBrowser(() => places).Browse(path);

        Assert.Null(listing.Path);
        Assert.Null(listing.Parent);
        Assert.Equal(places, listing.Folders);
    }

    [Fact]
    public void On_This_Machine_The_Starting_Places_All_Exist_And_Include_The_Home_Folder()
    {
        var places = _browser.Browse(null).Folders;

        Assert.NotEmpty(places);
        Assert.All(places, p => Assert.True(Directory.Exists(p.Path), p.Path));
        Assert.Contains(places, p => p.Name == "Home" && p.Path == Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        Assert.Equal(places, FileSystemFolderBrowser.StartingPlaces());
    }

    [Fact]
    public void Only_Places_That_Are_There_Are_Offered()
    {
        var there = Folder("Pictures");
        var offered = FileSystemFolderBrowser.Existing([("Pictures", there), ("Gone", Path.Combine(_root.FullName, "gone")), ("Unset", string.Empty)]);
        Assert.Equal(new[] { new FolderEntry("Pictures", there) }, offered);
    }

    [Theory]
    [InlineData(DriveType.Fixed, true)]
    [InlineData(DriveType.Removable, true)]      // a camera card or a USB stick
    [InlineData(DriveType.Network, true)]
    [InlineData(DriveType.CDRom, false)]
    [InlineData(DriveType.Ram, false)]           // on Linux, the system's own bookkeeping
    [InlineData(DriveType.NoRootDirectory, false)]
    [InlineData(DriveType.Unknown, false)]
    public void Only_Drives_That_People_Keep_Files_On_Are_Offered(DriveType type, bool offered)
        => Assert.Equal(offered, FileSystemFolderBrowser.HoldsFiles(type));
}
