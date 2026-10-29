using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Browsing;

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

    [Test]
    public async Task Lists_The_Folders_Inside_A_Folder_By_Name_With_Their_Full_Paths()
    {
        var phone = Folder("Phone Pictures");
        var backup = Folder("backup");
        Folder("Albums", "2024");
        File.WriteAllText(Path.Combine(_root.FullName, "notes.txt"), "a file is not a folder");

        var listing = _browser.Browse(_root.FullName);

        await Assert.That(listing.Path).IsEqualTo(_root.FullName);
        await Assert.That(listing.Parent).IsEqualTo(_root.Parent!.FullName);
        await Assert.That(listing.Folders.Select(f => f.Name)).IsEquivalentTo(new[] { "Albums", "backup", "Phone Pictures" }, CollectionOrdering.Matching);
        await Assert.That(listing.Folders.Select(f => f.Path)).IsEquivalentTo(new[] { Path.Combine(_root.FullName, "Albums"), backup, phone }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_Path_Is_Listed_Under_Its_Full_Spelling_However_It_Was_Asked_For()
    {
        Folder("Albums", "2024");
        var roundabout = Path.Combine(_root.FullName, "Albums", "..", "Albums") + Path.DirectorySeparatorChar;

        var listing = _browser.Browse(roundabout);

        await Assert.That(listing.Path).IsEqualTo(Path.Combine(_root.FullName, "Albums"));
        await Assert.That(listing.Folders.Single().Name).IsEqualTo("2024");
    }

    [Test]
    public async Task Leaves_Out_Hidden_Folders_And_The_Folder_Removed_Files_Are_Held_In()
    {
        Folder("Albums");
        Folder(PhotoStorageOptions.RemovedFolderName);
        Folder(PhotoStorageOptions.RemovedFolderName.ToUpperInvariant() + "-not-that-one");
        // A leading dot hides a folder on Linux and macOS; the attribute does on Windows.
        var hidden = new DirectoryInfo(Folder(".thumbnails"));
        hidden.Attributes |= FileAttributes.Hidden;

        var names = _browser.Browse(_root.FullName).Folders.Select(f => f.Name);

        await Assert.That(names).IsEquivalentTo(new[] { PhotoStorageOptions.RemovedFolderName.ToUpperInvariant() + "-not-that-one", "Albums" }.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), CollectionOrdering.Matching);
    }

    [Test]
    public async Task The_Top_Of_A_Disk_Has_No_Folder_Above_It()
    {
        var top = Path.GetPathRoot(_root.FullName)!;
        var listing = _browser.Browse(top);
        await Assert.That(listing.Path).IsEqualTo(top);
        await Assert.That(listing.Parent).IsNull();
        await Assert.That(listing.Folders).IsNotEmpty();
    }

    [Test]
    public async Task A_Folder_That_May_Not_Be_Looked_Into_Is_Shown_As_Empty()
    {
        var locked = Folder("private");
        Folder("private", "inside");
        using var denied = TestFiles.DenyAccess(locked);
        if (denied is null) return; // nothing is out of reach of the account running this

        var listing = _browser.Browse(locked);

        await Assert.That(listing.Path).IsEqualTo(locked);
        await Assert.That(listing.Folders).IsEmpty();
    }

    [Test]
    public async Task A_Folder_That_Is_Not_There_Is_Reported()
    {
        var missing = Path.Combine(_root.FullName, "no-such-folder");
        var error = Assert.ThrowsExactly<DirectoryNotFoundException>(() => _browser.Browse(missing));
        await Assert.That(error.Message).Contains(missing);

        // A file is not a folder either.
        var file = Path.Combine(_root.FullName, "photo.jpg");
        File.WriteAllText(file, "x");
        Assert.ThrowsExactly<DirectoryNotFoundException>(() => _browser.Browse(file));
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    public async Task With_No_Folder_Named_It_Offers_The_Places_To_Start_From(string? path)
    {
        var places = new[] { new FolderEntry("Pictures", Folder("Pictures")), new FolderEntry("C:\\", _root.FullName) };
        var listing = new FileSystemFolderBrowser(() => places).Browse(path);

        await Assert.That(listing.Path).IsNull();
        await Assert.That(listing.Parent).IsNull();
        await Assert.That(listing.Folders).IsEquivalentTo(places, CollectionOrdering.Matching);
    }

    [Test]
    public async Task On_This_Machine_The_Starting_Places_All_Exist_And_Include_The_Home_Folder()
    {
        var places = _browser.Browse(null).Folders;

        await Assert.That(places).IsNotEmpty();
        foreach (var p in places) await Assert.That(Directory.Exists(p.Path)).IsTrue().Because(p.Path);
        await Assert.That(places).Contains(p => p.Name == "Home" && p.Path == Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        await Assert.That(FileSystemFolderBrowser.StartingPlaces()).IsEquivalentTo(places, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Only_Places_That_Are_There_Are_Offered()
    {
        var there = Folder("Pictures");
        var offered = FileSystemFolderBrowser.Existing([("Pictures", there), ("Gone", Path.Combine(_root.FullName, "gone")), ("Unset", string.Empty)]);
        await Assert.That(offered).IsEquivalentTo(new[] { new FolderEntry("Pictures", there) }, CollectionOrdering.Matching);
    }

    [Test]
    [Arguments(DriveType.Fixed, true)]
    [Arguments(DriveType.Removable, true)]      // a camera card or a USB stick
    [Arguments(DriveType.Network, true)]
    [Arguments(DriveType.CDRom, false)]
    [Arguments(DriveType.Ram, false)]           // on Linux, the system's own bookkeeping
    [Arguments(DriveType.NoRootDirectory, false)]
    [Arguments(DriveType.Unknown, false)]
    public async Task Only_Drives_That_People_Keep_Files_On_Are_Offered(DriveType type, bool offered)
        => await Assert.That(FileSystemFolderBrowser.HoldsFiles(type)).IsEqualTo(offered);
}
