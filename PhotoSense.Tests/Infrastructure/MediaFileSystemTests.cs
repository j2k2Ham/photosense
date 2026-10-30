using System.Text;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Library;

namespace PhotoSense.Tests.Infrastructure;

/// <summary>An Android phone's photos as the rest of PhotoSense sees them: the phone's storage as a folder, with its own folders in it.</summary>
public sealed class MediaFileSystemTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "photosense-android-" + Guid.NewGuid().ToString("N"), "Phone");
    private readonly FakeMediaStore _store = new();
    private readonly MediaFileSystem _phone;

    public MediaFileSystemTests() => _phone = new MediaFileSystem(_store, Root + Path.DirectorySeparatorChar);

    private static string In(params string[] parts) => Path.Combine([Root, .. parts]);

    private static byte[] Bytes(string content = "bytes") => Encoding.UTF8.GetBytes(content);

    private static List<string> Names(IEnumerable<string> paths) => [.. paths.Select(p => Path.GetFileName(p)!).Order(StringComparer.Ordinal)];

    [Test]
    public async Task Each_Photo_Is_A_File_In_The_Folder_It_Is_In_On_The_Phone()
    {
        _store.Add("1", "DCIM/Camera/", "IMG_0001.jpg", Bytes());
        _store.Add("2", "DCIM/Camera/", "VID_0002.mp4", Bytes(), video: true);
        _store.Add("3", "Pictures/Screenshots/", "Screenshot_1.png", Bytes());
        _store.Add("4", "", "loose.jpg", Bytes());

        await Assert.That(Names(_phone.FilesIn(In("DCIM", "Camera")))).IsEquivalentTo(["IMG_0001.jpg", "VID_0002.mp4"], CollectionOrdering.Matching);
        await Assert.That(Names(_phone.FilesIn(Root))).IsEquivalentTo(["loose.jpg"], CollectionOrdering.Matching);
        await Assert.That(Names(_phone.FoldersIn(Root))).IsEquivalentTo(["DCIM", "Pictures"], CollectionOrdering.Matching);
        await Assert.That(Names(_phone.FoldersIn(In("Pictures")))).IsEquivalentTo(["Screenshots"], CollectionOrdering.Matching);
        await Assert.That((_phone.Root, _phone.FolderExists(Root), _phone.FolderExists(In("DCIM") + Path.DirectorySeparatorChar), _phone.FolderExists(In("Movies")))).IsEqualTo((Root, true, true, false));
        await Assert.That((_phone.FileExists(In("DCIM", "Camera", "IMG_0001.jpg")), _phone.Describe(In("DCIM", "Camera", "IMG_0001.jpg"))!.Length, _phone.Describe(In("DCIM", "gone.jpg")), _phone.IsLink(In("loose.jpg"))))
            .IsEqualTo((true, 5L, (StoredFile?)null, false));
    }

    [Test]
    public async Task A_Photo_Is_Read_From_Any_Point_Even_When_The_Phone_Hands_It_Out_Only_From_The_Start()
    {
        _store.Add("1", "DCIM/Camera/", "IMG_0001.jpg", Bytes("the whole picture"));
        _store.Add("2", "DCIM/Camera/", "IMG_0002.jpg", Bytes("the whole"));
        foreach (var onlyForward in new[] { false, true })
        {
            _store.OnlyFromTheStart = onlyForward;
            using var read = _phone.OpenRead(In("DCIM", "Camera", "IMG_0001.jpg"));
            read.Position = 4;
            await Assert.That(new StreamReader(read).ReadToEnd()).IsEqualTo("whole picture");
        }
        await Assert.That(_phone.IsCopyOf(In("DCIM", "Camera", "IMG_0002.jpg"), In("DCIM", "Camera", "IMG_0001.jpg"))).IsTrue();
        await Assert.That(Assert.Throws<FileNotFoundException>(() => _phone.OpenRead(In("DCIM", "gone.jpg")))!.Message).IsEqualTo("The photo is no longer on the phone.");
    }

    [Test]
    public async Task Photos_Move_Between_Folders_And_Names_In_One_Change_And_Never_Replace_One()
    {
        _store.Add("1", "DCIM/Camera/", "IMG_0001.jpg", Bytes());
        _store.Add("2", "DCIM/Camera/", "IMG_0002.jpg", Bytes());
        _store.Add("3", "Pictures/Butte/", "IMG_0002.jpg", Bytes());
        _phone.MakeFolder(In("Pictures", "Butte", "2022"));
        await Assert.That(_phone.FolderExists(In("Pictures", "Butte", "2022"))).IsTrue();

        _phone.MoveAll([new FileMove(In("DCIM", "Camera", "IMG_0001.jpg"), In("Pictures", "Butte", "2022", "IMG_0001.jpg")),
            new FileMove(In("DCIM", "Camera", "IMG_0002.jpg"), In("Pictures", "Butte", "IMG_0002 (1).jpg"))]);
        await Assert.That(_store.Changes.Single().Move).IsEquivalentTo([("1", "Pictures/Butte/2022/", "IMG_0001.jpg"), ("2", "Pictures/Butte/", "IMG_0002 (1).jpg")], CollectionOrdering.Matching);
        await Assert.That(Names(_phone.FilesIn(In("Pictures", "Butte")))).IsEquivalentTo(["IMG_0002 (1).jpg", "IMG_0002.jpg"], CollectionOrdering.Matching);

        // Already where it was going, as after a change cut short: nothing more to do.
        _phone.Move(In("DCIM", "Camera", "IMG_0001.jpg"), In("Pictures", "Butte", "2022", "IMG_0001.jpg"));
        await Assert.That(_store.Changes.Count).IsEqualTo(1);

        await Assert.That(Assert.Throws<IOException>(() => _phone.Move(In("Pictures", "Butte", "IMG_0002.jpg"), In("Pictures", "Butte", "IMG_0002 (1).jpg")))!.Message)
            .IsEqualTo("IMG_0002 (1).jpg is there already, and is not replaced.");
        await Assert.That(Assert.Throws<FileNotFoundException>(() => _phone.Move(In("DCIM", "Camera", "IMG_0009.jpg"), In("Pictures", "IMG_0009.jpg")))!.Message)
            .IsEqualTo("IMG_0009.jpg is no longer on the phone.");
        await Assert.That(Assert.Throws<IOException>(() => _phone.MoveAll([new FileMove(In("Pictures", "Butte", "IMG_0002.jpg"), In("Pictures", "A.jpg")), new FileMove(In("Pictures", "Butte", "IMG_0002.jpg"), In("DCIM", "B.jpg"))]))!.Message)
            .IsEqualTo("IMG_0002.jpg cannot go to two places at once.");
        await Assert.That(_store.Changes.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Photos_Go_Only_Inside_DCIM_Pictures_Or_Movies_And_Never_Outside_The_Phone()
    {
        _store.Add("1", "DCIM/Camera/", "IMG_0001.jpg", Bytes());
        var photo = In("DCIM", "Camera", "IMG_0001.jpg");
        await Assert.That(Assert.Throws<UnauthorizedAccessException>(() => _phone.MakeFolder(In("Butte")))!.Message)
            .IsEqualTo("On an Android phone, photos are kept inside DCIM, Pictures or Movies, so Butte has to be made inside one of those.");
        await Assert.That(Assert.Throws<UnauthorizedAccessException>(() => _phone.Move(photo, In("IMG_0001.jpg")))!.Message)
            .IsEqualTo("On an Android phone, photos are kept inside DCIM, Pictures or Movies, not in the phone's storage itself.");
        await Assert.That(Assert.Throws<UnauthorizedAccessException>(() => _phone.Move(photo, In("Pictures", "PhotoSense Removed", "IMG_0001.jpg")))!.Message)
            .IsEqualTo("PhotoSense keeps Pictures/PhotoSense Removed for what it removes from the phone, so photos are not put there.");
        await Assert.That(Assert.Throws<UnauthorizedAccessException>(() => _phone.Move(photo, Path.Combine(Root, "..", "Elsewhere", "IMG_0001.jpg")))!.Message).Contains("is not on the phone.");
        await Assert.That(Assert.Throws<UnauthorizedAccessException>(() => _phone.Copy(photo, In("Pictures", "IMG_0001.jpg")))!.Message).IsEqualTo(MediaFileSystem.NoCopies);
        await Assert.That(Assert.Throws<UnauthorizedAccessException>(() => _phone.DeleteCopy(photo))!.Message).IsEqualTo(MediaFileSystem.NoCopies);
        await Assert.That(Assert.Throws<UnauthorizedAccessException>(() => _phone.CreateNew(In("Pictures", "New.jpg")))!.Message).IsEqualTo("PhotoSense does not put new files on a phone.");
        // The phone's storage itself is there already; any of its media folders can be made, as lowercase too.
        _phone.MakeFolder(Root);
        _phone.MakeFolder(In("movies", "Trips"));
        await Assert.That((_store.Changes.Count, _phone.FolderExists(In("movies", "Trips")))).IsEqualTo((0, true));
    }

    [Test]
    public async Task What_Is_Removed_From_The_Whole_Phone_Waits_In_Pictures_And_Is_Shown_In_The_Removed_Folder_Of_The_Phone()
    {
        _store.Add("1", "DCIM/Camera/", "IMG_0001.jpg", Bytes());
        _store.Add("2", "DCIM/Camera/", "IMG_0002.jpg", Bytes());
        var removed = In("_PhotoSense_Removed", "DCIM", "Camera");
        _phone.MakeFolder(removed);
        _phone.Move(In("DCIM", "Camera", "IMG_0001.jpg"), Path.Combine(removed, "IMG_0001.jpg"));
        // Removed from a folder, it waits in that folder's own, as on a computer.
        _phone.Move(In("DCIM", "Camera", "IMG_0002.jpg"), In("DCIM", "Camera", "_PhotoSense_Removed", "IMG_0002.jpg"));

        await Assert.That(_store.Places().Values.Order(StringComparer.Ordinal)).IsEquivalentTo(["DCIM/Camera/_PhotoSense_Removed/IMG_0002.jpg", "Pictures/PhotoSense Removed/DCIM/Camera/IMG_0001.jpg"], CollectionOrdering.Matching);
        await Assert.That(Names(_phone.FilesIn(removed))).IsEquivalentTo(["IMG_0001.jpg"], CollectionOrdering.Matching);
        await Assert.That(Names(_phone.FoldersIn(Root))).IsEquivalentTo(["DCIM", "_PhotoSense_Removed"], CollectionOrdering.Matching);
        // A folder of that name the phone already had elsewhere is only a folder.
        _store.Add("3", "Movies/PhotoSense Removed/", "VID.mp4", Bytes(), video: true);
        _store.Add("4", "pictures/Other/", "IMG_4.jpg", Bytes());
        _phone.FilesIn(Root);
        await Assert.That(Names(_phone.FoldersIn(In("Movies")))).IsEquivalentTo(["PhotoSense Removed"], CollectionOrdering.Matching);
        await Assert.That(_phone.FileExists(In("pictures", "Other", "IMG_4.jpg"))).IsTrue();

        _phone.EraseAll([Path.Combine(removed, "IMG_0001.jpg"), Path.Combine(removed, "IMG_0001.jpg"), Path.Combine(removed, "gone.jpg")]);
        await Assert.That((_store.Changes[^1].Trash.Single(), _store.Trashed.Single())).IsEqualTo(("1", "1"));
        // Nothing left to erase: the phone is not asked.
        var asked = _store.Changes.Count;
        _phone.Erase(Path.Combine(removed, "IMG_0001.jpg"));
        await Assert.That(_store.Changes.Count).IsEqualTo(asked);
    }

    [Test]
    public async Task Folders_Made_For_Photos_That_Never_Came_Are_Forgotten_And_Those_That_Hold_Any_Stay()
    {
        _store.Add("1", "Pictures/Kept/", "IMG_1.jpg", Bytes());
        _phone.MakeFolder(In("Pictures", "Made", "Later"));
        _phone.MakeFolder(In("Pictures", "Holding", "Inside"));
        _phone.RemoveEmptyFolders([In("Pictures", "Made"), In("Pictures", "Made", "Later"), In("Pictures", "Holding"), In("Pictures", "Kept")]);
        await Assert.That((_phone.FolderExists(In("Pictures", "Made")), _phone.FolderExists(In("Pictures", "Holding", "Inside")), _phone.FolderExists(In("Pictures", "Kept")))).IsEqualTo((false, true, true));
        _phone.RemoveEmptyFolder(In("Pictures", "Holding", "Inside"));
        await Assert.That((_phone.FolderExists(In("Pictures", "Holding", "Inside")), _store.Changes.Count)).IsEqualTo((false, 0));
    }

    [Test]
    public async Task A_Change_The_Person_Declines_Changes_Nothing_And_The_Phone_Is_Read_Afresh()
    {
        _store.Add("1", "DCIM/Camera/", "IMG_0001.jpg", Bytes());
        _store.Refuse = "You chose not to let PhotoSense change these photos.";
        await Assert.That(Assert.Throws<IOException>(() => _phone.Move(In("DCIM", "Camera", "IMG_0001.jpg"), In("Pictures", "IMG_0001.jpg")))!.Message).IsEqualTo("You chose not to let PhotoSense change these photos.");
        _store.Add("2", "DCIM/Camera/", "IMG_0002.jpg", Bytes());
        await Assert.That(Names(_phone.FilesIn(In("DCIM", "Camera")))).IsEquivalentTo(["IMG_0001.jpg", "IMG_0002.jpg"], CollectionOrdering.Matching);
        // Listing the whole phone reads it again, so a scan sees what is on it now.
        _store.Add("3", "", "IMG_0003.jpg", Bytes());
        await Assert.That(Names(_phone.FilesIn(Root))).IsEquivalentTo(["IMG_0003.jpg"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task The_Folder_Browser_Offers_The_Phone_And_Its_Folders_But_Not_What_Was_Removed()
    {
        _store.Add("1", "DCIM/Camera/", "IMG_0001.jpg", Bytes());
        _store.Add("2", "Pictures/PhotoSense Removed/DCIM/", "IMG_0002.jpg", Bytes());
        var browser = new PhotoLibraryBrowser(_phone, Root);
        await Assert.That(browser.Browse(null).Folders.Single().Path).IsEqualTo(Root);
        var top = browser.Browse(Root);
        await Assert.That((top.Parent, top.Folders.Select(f => f.Name).Single())).IsEqualTo(((string?)null, "DCIM"));
        await Assert.That(browser.Browse(In("DCIM")).Parent).IsEqualTo(Root);
    }
}
