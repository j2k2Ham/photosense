using System.Text;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Library;

namespace PhotoSense.Tests.Infrastructure;

/// <summary>A phone's photo library as the rest of PhotoSense sees it: one folder, every picture in it once.</summary>
public sealed class PhotoLibraryFileSystemTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "photosense-library-" + Guid.NewGuid().ToString("N"), "Photo Library");
    private readonly FakePhotoLibrary _library = new();
    private readonly PhotoLibraryFileSystem _photos;

    public PhotoLibraryFileSystemTests() => _photos = new PhotoLibraryFileSystem(_library, Root + Path.DirectorySeparatorChar);

    private static string In(string name) => Path.Combine(Root, name);

    private static (LibraryFileKind, string, byte[]) File(LibraryFileKind kind, string name, string content = "bytes") => (kind, name, Encoding.UTF8.GetBytes(content));

    private List<string> Listed() => [.. _photos.FilesIn(Root).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal)];

    [Test]
    public async Task Every_Picture_And_Video_Is_A_File_In_The_One_Folder_And_A_Live_Photo_Is_Two()
    {
        _library.Add("A1", File(LibraryFileKind.Photo, "IMG_0001.HEIC"), File(LibraryFileKind.PairedVideo, "IMG_0001.MOV"));
        _library.Add("B2", File(LibraryFileKind.Video, "IMG_0002.MOV"));
        _library.Add("C3", File(LibraryFileKind.Photo, "IMG_0003.JPG"));
        // Something kept as neither a picture nor a video, as the framework may list, is not shown.
        _library.Add("D4", File(LibraryFileKind.PairedVideo, "IMG_0004.MOV"));

        await Assert.That(Listed()).IsEquivalentTo(["IMG_0001.HEIC", "IMG_0001.MOV", "IMG_0002.MOV", "IMG_0003.JPG"], CollectionOrdering.Matching);
        await Assert.That((_photos.Root, _photos.FolderExists(Root), _photos.FolderExists(Root + Path.DirectorySeparatorChar), _photos.FolderExists(In("Album")))).IsEqualTo((Root, true, true, false));
        await Assert.That((_photos.FoldersIn(Root).Count, _photos.FilesIn(In("Album")).Count, _photos.IsLink(In("IMG_0001.HEIC")))).IsEqualTo((0, 0, false));
    }

    [Test]
    public async Task Pictures_Taken_Under_One_Name_Are_Told_Apart_By_Their_Own_Ids_And_Keep_Their_Names_When_Others_Come_And_Go()
    {
        _library.Add("1A2B3C4D-0000-4000-8000-000000000001/L0/001", File(LibraryFileKind.Photo, "IMG_0001.HEIC"), File(LibraryFileKind.PairedVideo, "IMG_0001.MOV"));
        _library.Add("9F8E7D6C-0000-4000-8000-000000000002/L0/001", File(LibraryFileKind.Photo, "img_0001.jpg"));
        _library.Add("55555555-0000-4000-8000-000000000003/L0/001", File(LibraryFileKind.Photo, "IMG_0005.HEIC"));
        string[] apart = ["IMG_0001 (1A2B3C4D).HEIC", "IMG_0001 (1A2B3C4D).MOV", "IMG_0005.HEIC", "img_0001 (9F8E7D6C).jpg"];
        await Assert.That(Listed()).IsEquivalentTo(apart, CollectionOrdering.Matching);

        // An older picture under one of the names arrives: no name comes to stand for another picture.
        _library.Add("00000000-0000-4000-8000-000000000004/L0/001", File(LibraryFileKind.Photo, "IMG_0005.HEIC"));
        await Assert.That(Listed()).IsEquivalentTo(["IMG_0001 (1A2B3C4D).HEIC", "IMG_0001 (1A2B3C4D).MOV", "IMG_0005 (00000000).HEIC", "IMG_0005 (55555555).HEIC", "img_0001 (9F8E7D6C).jpg"], CollectionOrdering.Matching);
        await Assert.That(_photos.FileExists(In("IMG_0005.HEIC"))).IsFalse();
    }

    [Test]
    public async Task Ids_That_Begin_Alike_Are_Told_Apart_By_As_Much_Of_Them_As_It_Takes()
    {
        _library.Add("ABCDEFGH-1111", File(LibraryFileKind.Photo, "IMG_1.JPG"));
        _library.Add("ABCDEFGH-2222", File(LibraryFileKind.Photo, "IMG_1.JPG"));
        // Ids that differ only in what is left out of a name stay as alike as they are.
        _library.Add("X-1", File(LibraryFileKind.Photo, "IMG_2.JPG"));
        _library.Add("X1", File(LibraryFileKind.Photo, "IMG_2.JPG"));

        await Assert.That(Listed()).IsEquivalentTo(["IMG_1 (ABCDEFGH1111).JPG", "IMG_1 (ABCDEFGH2222).JPG", "IMG_2 (X1).JPG"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_File_Is_Described_And_Read_As_It_Was_Taken()
    {
        var item = _library.Add("A1", File(LibraryFileKind.Photo, "IMG_0001.HEIC", "the picture"));

        // Asked about before the folder is listed, the library is read for it.
        await Assert.That(_photos.Describe(In("IMG_0001.HEIC"))).IsEqualTo(new StoredFile(11, item.ModifiedUtc));
        await Assert.That((_photos.Describe(In("IMG_0002.HEIC")), _photos.FileExists(In("IMG_0001.HEIC")))).IsEqualTo(((StoredFile?)null, true));
        using (var read = _photos.OpenRead(In("IMG_0001.HEIC")))
        {
            read.Position = 4;
            await Assert.That(new StreamReader(read).ReadToEnd()).IsEqualTo("picture");
        }

        // One the framework hands out to be read from the start only is held whole, to be read from anywhere.
        _library.OnlyFromTheStart = true;
        using var whole = _photos.OpenRead(In("IMG_0001.HEIC"));
        whole.Position = 4;
        await Assert.That(new StreamReader(whole).ReadToEnd()).IsEqualTo("picture");

        await Assert.That(Assert.Throws<FileNotFoundException>(() => _photos.OpenRead(In("IMG_0002.HEIC")))!.Message).IsEqualTo("The picture is no longer in the photo library.");
    }

    [Test]
    public async Task The_Library_Is_Read_Again_Each_Time_Its_Folder_Is_Listed()
    {
        _library.Add("A1", File(LibraryFileKind.Photo, "IMG_0001.HEIC"));
        await Assert.That(Listed()).HasSingleItem();
        await Assert.That(_photos.FileExists(In("IMG_0001.HEIC"))).IsTrue();
        await Assert.That(_library.Reads).IsEqualTo(1);

        _library.Remove("A1");
        _library.Add("B2", File(LibraryFileKind.Photo, "IMG_0002.HEIC"));
        await Assert.That(Listed()).IsEquivalentTo(["IMG_0002.HEIC"], CollectionOrdering.Matching);
        await Assert.That((_photos.FileExists(In("IMG_0001.HEIC")), _library.Reads)).IsEqualTo((false, 2));
    }

    [Test]
    public async Task A_Copy_Is_Known_By_Holding_What_The_Original_Begins_With()
    {
        _library.Add("A1", File(LibraryFileKind.Photo, "IMG_1.JPG", "the whole picture"));
        _library.Add("B2", File(LibraryFileKind.Photo, "IMG_2.JPG", "the whole"));
        _library.Add("C3", File(LibraryFileKind.Photo, "IMG_3.JPG", "another one"));

        await Assert.That((_photos.IsCopyOf(In("IMG_2.JPG"), In("IMG_1.JPG")), _photos.IsCopyOf(In("IMG_3.JPG"), In("IMG_1.JPG")), _photos.IsCopyOf(In("IMG_1.JPG"), In("IMG_2.JPG"))))
            .IsEqualTo((true, false, false));
    }

    [Test]
    public async Task Each_Album_PhotoSense_Made_Is_A_Folder_Inside_It_With_Its_Pictures_And_The_Folders_It_Is_In_Above_It()
    {
        _library.Add("A1", File(LibraryFileKind.Photo, "IMG_1.JPG"));
        _library.Add("B2", File(LibraryFileKind.Photo, "IMG_2.JPG"), File(LibraryFileKind.PairedVideo, "IMG_2.MOV"));
        _library.Add("C3", File(LibraryFileKind.Photo, "IMG_3.JPG"));
        _library.Add("D4", File(LibraryFileKind.Photo, "IMG_4.JPG"));
        _library.Album(["Trips", "Glacier"], "B2");
        _library.Album(["Butte"], "C3", "C3-gone");
        _library.Album([PhotoLibraryFileSystem.RemovedAlbum], "D4");
        // One the person renamed in the Photos app, to a name a path cannot hold, and one put in two albums.
        _library.Album(["Trips/2022\\Summer", ".."], "C3");

        await Assert.That(Listed()).IsEquivalentTo(["IMG_1.JPG"], CollectionOrdering.Matching);
        await Assert.That(_photos.FoldersIn(Root)).IsEquivalentTo([In("Butte"), In("Trips"), In("Trips-2022-Summer"), In("_PhotoSense_Removed")], CollectionOrdering.Matching);
        await Assert.That(_photos.FoldersIn(In("Trips"))).IsEquivalentTo([In(Path.Combine("Trips", "Glacier"))], CollectionOrdering.Matching);
        await Assert.That(_photos.FilesIn(In(Path.Combine("Trips", "Glacier")))).IsEquivalentTo([In(Path.Combine("Trips", "Glacier", "IMG_2.JPG")), In(Path.Combine("Trips", "Glacier", "IMG_2.MOV"))], CollectionOrdering.Matching);
        await Assert.That(_photos.FilesIn(In("Butte"))).IsEquivalentTo([In(Path.Combine("Butte", "IMG_3.JPG"))], CollectionOrdering.Matching);
        await Assert.That(_photos.FileExists(In(Path.Combine("Trips-2022-Summer", "-", "IMG_3.JPG")))).IsTrue();
        await Assert.That(_photos.FilesIn(In("_PhotoSense_Removed"))).IsEquivalentTo([In(Path.Combine("_PhotoSense_Removed", "IMG_4.JPG"))], CollectionOrdering.Matching);
        await Assert.That((_photos.FolderExists(In("Trips")), _photos.FolderExists(In("Nowhere")), _photos.FilesIn(In("Trips")).Count)).IsEqualTo((true, false, 0));
    }

    [Test]
    public async Task A_Picture_Moved_Goes_Into_The_Album_Of_Its_Folder_Made_As_It_Arrives_And_Out_Of_PhotoSenses_Others_In_One_Change()
    {
        _library.Add("A1", File(LibraryFileKind.Photo, "IMG_1.JPG"), File(LibraryFileKind.PairedVideo, "IMG_1.MOV"));
        _library.Add("B2", File(LibraryFileKind.Photo, "IMG_2.JPG"));
        _library.Album(["Trips"], "B2");
        Listed();
        var butte = In(Path.Combine("Butte", "2022"));
        _photos.MakeFolder(butte);
        await Assert.That((_photos.FolderExists(butte), _photos.FolderExists(In("Butte")), _photos.FoldersIn(In("Butte")).Single(), _library.Changes.Count)).IsEqualTo((true, true, butte, 0));

        // A Live Photo goes as one, its picture and its video together.
        _photos.MoveAll([new FileMove(In("IMG_1.JPG"), Path.Combine(butte, "IMG_1.JPG")), new FileMove(In("IMG_1.MOV"), Path.Combine(butte, "IMG_1.MOV")), new FileMove(In(Path.Combine("Trips", "IMG_2.JPG")), Path.Combine(butte, "IMG_2.JPG"))]);
        var change = _library.Changes.Single();
        await Assert.That(change.Add.Select(a => $"{a.Item} {string.Join('/', a.Album)}")).IsEquivalentTo(["A1 Butte/2022", "B2 Butte/2022"], CollectionOrdering.Matching);
        await Assert.That(change.Take.Select(a => $"{a.Item} {string.Join('/', a.Album)}")).IsEquivalentTo(["B2 Trips"], CollectionOrdering.Matching);
        await Assert.That(_photos.FilesIn(butte).Count).IsEqualTo(3);
        await Assert.That(_photos.Describe(Path.Combine(butte, "IMG_1.JPG"))).IsNotNull();

        // Moved one at a time, the video has already gone with its picture; and back at the top, it is in no album of PhotoSense's.
        _photos.Move(Path.Combine(butte, "IMG_1.JPG"), In("IMG_1.JPG"));
        _photos.Move(Path.Combine(butte, "IMG_1.MOV"), In("IMG_1.MOV"));
        await Assert.That(_library.Changes.Count).IsEqualTo(2);
        await Assert.That((_library.Changes[1].Take.Single().Item, _library.Changes[1].Add.Count)).IsEqualTo(("A1", 0));
        await Assert.That(Listed()).IsEquivalentTo(["IMG_1.JPG", "IMG_1.MOV"], CollectionOrdering.Matching);
        // A move to where a picture already is changes nothing.
        _photos.Move(In("IMG_1.JPG"), In("IMG_1.JPG"));
        await Assert.That(_library.Changes.Count).IsEqualTo(2);
    }

    [Test]
    public async Task A_Picture_Put_In_A_Folder_Of_Albums_Joins_The_Album_Of_That_Name_Beside_It_And_A_Removed_One_The_Album_Of_Removed_Pictures()
    {
        _library.Add("A1", File(LibraryFileKind.Photo, "IMG_1.JPG"));
        _library.Add("B2", File(LibraryFileKind.Photo, "IMG_2.JPG"));
        _library.Album(["Road/Trips", "2022"]);
        Listed();
        // The folder is shown under a name a path can hold; the album it stands for keeps the name the person gave it.
        _photos.Move(In("IMG_1.JPG"), In(Path.Combine("Road-Trips", "IMG_1.JPG")));
        _photos.Move(In("IMG_2.JPG"), In(Path.Combine("_PhotoSense_Removed", "Butte", "IMG_2.JPG")));
        await Assert.That(_library.Changes.Select(c => string.Join('/', c.Add.Single().Album))).IsEquivalentTo(["Road/Trips", "PhotoSense Removed/Butte"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task What_Cannot_Be_Done_On_A_Phone_Is_Refused_Before_Anything_Changes()
    {
        _library.Add("A1", File(LibraryFileKind.Photo, "IMG_1.JPG"));
        _library.Add("B2", File(LibraryFileKind.Photo, "IMG_2.JPG"));
        Listed();
        var picture = In("IMG_1.JPG");
        await Assert.That(Assert.Throws<IOException>(() => _photos.Move(picture, In(Path.Combine("Trips", "Other.JPG"))))!.Message)
            .IsEqualTo("IMG_1.JPG keeps its own name on the phone, so it cannot arrive as Other.JPG.");
        await Assert.That(Assert.Throws<UnauthorizedAccessException>(() => _photos.Move(picture, In(Path.Combine("..", "Elsewhere", "IMG_1.JPG"))))!.Message).Contains("is not in the photo library.");
        await Assert.That(Assert.Throws<UnauthorizedAccessException>(() => _photos.MakeFolder(In(Path.Combine("..", "Elsewhere"))))!.Message).Contains("is not in the photo library.");
        await Assert.That(Assert.Throws<UnauthorizedAccessException>(() => _photos.CreateNew(In("New.JPG")))!.Message).IsEqualTo("PhotoSense does not put new files in a phone's photo library.");
        await Assert.That(Assert.Throws<FileNotFoundException>(() => _photos.Move(In("IMG_9.JPG"), In(Path.Combine("Trips", "IMG_9.JPG"))))!.Message).IsEqualTo("IMG_9.JPG is no longer in the photo library.");
        await Assert.That(Assert.Throws<IOException>(() => _photos.MoveAll([new FileMove(picture, In(Path.Combine("A", "IMG_1.JPG"))), new FileMove(picture, In(Path.Combine("B", "IMG_1.JPG")))]))!.Message)
            .IsEqualTo("IMG_1.JPG cannot go to two albums at once.");
        foreach (var copy in new Action[] { () => _photos.Copy(picture, In(Path.Combine("Trips", "IMG_1.JPG"))), () => _photos.DeleteCopy(picture) })
            await Assert.That(Assert.Throws<UnauthorizedAccessException>(copy)!.Message).IsEqualTo(PhotoLibraryFileSystem.NoCopies);
        await Assert.That(_library.Changes).IsEmpty();
        // Nothing to make: the library itself is always there.
        _photos.MakeFolder(Root);
        await Assert.That(_photos.FoldersIn(Root)).IsEmpty();
    }

    [Test]
    public async Task When_The_Person_Says_No_Nothing_Changes_And_The_Library_Is_Read_Afresh()
    {
        _library.Add("A1", File(LibraryFileKind.Photo, "IMG_1.JPG"));
        Listed();
        _library.Refuse = "You chose not to let PhotoSense change your photos.";
        var reads = _library.Reads;
        await Assert.That(Assert.Throws<IOException>(() => _photos.Move(In("IMG_1.JPG"), In(Path.Combine("Trips", "IMG_1.JPG"))))!.Message).IsEqualTo("You chose not to let PhotoSense change your photos.");
        await Assert.That((_photos.FileExists(In("IMG_1.JPG")), _library.Reads)).IsEqualTo((true, reads + 1));
    }

    [Test]
    public async Task Erasing_Sends_Pictures_To_The_Phones_Own_Bin_In_One_Change_A_Live_Photo_Once()
    {
        _library.Add("A1", File(LibraryFileKind.Photo, "IMG_1.JPG"), File(LibraryFileKind.PairedVideo, "IMG_1.MOV"));
        _library.Add("B2", File(LibraryFileKind.Photo, "IMG_2.JPG"));
        _library.Album([PhotoLibraryFileSystem.RemovedAlbum], "A1", "B2");
        var removed = In("_PhotoSense_Removed");
        // One gone since its folder was read has nothing left to erase.
        _photos.EraseAll([Path.Combine(removed, "IMG_1.JPG"), Path.Combine(removed, "IMG_1.MOV"), Path.Combine(removed, "IMG_9.JPG")]);
        await Assert.That((_library.Changes.Single().Delete.Single(), _library.RecentlyDeleted.Single())).IsEqualTo(("A1", "A1"));
        _photos.Erase(Path.Combine(removed, "IMG_2.JPG"));
        await Assert.That(_library.RecentlyDeleted).IsEquivalentTo(["A1", "B2"], CollectionOrdering.Matching);
        // Nothing left of what was asked: no change at all.
        _photos.EraseAll([Path.Combine(removed, "IMG_2.JPG")]);
        await Assert.That(_library.Changes.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Emptied_Albums_Stay_So_The_Phone_Never_Asks_And_Only_Folders_Made_For_Pictures_That_Never_Came_Go()
    {
        _library.Add("A1", File(LibraryFileKind.Photo, "IMG_1.JPG"));
        _library.Album(["Butte", "2022"]);
        _library.Album(["Trips"], "A1");
        var made = In(Path.Combine("Made", "For later"));
        _photos.MakeFolder(made);
        var holding = In(Path.Combine("Holding", "Inside"));
        _photos.MakeFolder(holding);

        _photos.RemoveEmptyFolders([In("Butte"), In(Path.Combine("Butte", "2022")), In("Trips"), made, In("Made"), In("Holding"), Root, In("Nowhere")]);
        await Assert.That(_library.Changes).IsEmpty();
        await Assert.That((_photos.FolderExists(In(Path.Combine("Butte", "2022"))), _photos.FolderExists(In("Trips")), _photos.FolderExists(made), _photos.FolderExists(In("Made"))))
            .IsEqualTo((true, true, false, false));
        // A folder still holding one made for later stays with it.
        await Assert.That((_photos.FolderExists(holding), _photos.FolderExists(In("Holding")))).IsEqualTo((true, true));

        // A folder made for pictures that became an album, and was emptied again, stays as that album.
        var glacier = In("Glacier");
        _photos.MakeFolder(glacier);
        _photos.Move(In(Path.Combine("Trips", "IMG_1.JPG")), Path.Combine(glacier, "IMG_1.JPG"));
        _photos.Move(Path.Combine(glacier, "IMG_1.JPG"), In("IMG_1.JPG"));
        var changes = _library.Changes.Count;
        _photos.RemoveEmptyFolder(glacier);
        await Assert.That((_library.Changes.Count, _photos.FolderExists(glacier), _library.InAlbums()["Glacier"].Length)).IsEqualTo((changes, true, 0));
    }

    [Test]
    public async Task The_Library_Is_Offered_With_Its_Albums_But_Not_The_Removed_Pictures_And_No_Other_Program_Opens_Its_Photos()
    {
        _library.Add("A1", File(LibraryFileKind.Photo, "IMG_1.JPG"));
        _library.Album(["Trips", "Glacier"], "A1");
        _library.Album([PhotoLibraryFileSystem.RemovedAlbum]);
        var browser = new PhotoLibraryBrowser(_photos, _photos.Root);

        var places = browser.Browse(" ");
        await Assert.That((places.Path, places.Parent, places.Folders.Single())).IsEqualTo(((string?)null, (string?)null, new FolderEntry("Photo Library", Root)));
        var inside = browser.Browse(Root + Path.DirectorySeparatorChar);
        await Assert.That((inside.Path, inside.Parent, inside.Folders.Single())).IsEqualTo((Root, (string?)null, new FolderEntry("Trips", In("Trips"))));
        var trips = browser.Browse(In("Trips"));
        await Assert.That((trips.Parent, trips.Folders.Single().Name)).IsEqualTo((Root, "Glacier"));
        await Assert.That(Assert.Throws<DirectoryNotFoundException>(() => browser.Browse(In("Album")))!.Message).IsEqualTo($"The photo library has no folder {In("Album")}.");

        await Assert.That(Assert.Throws<InvalidOperationException>(() => new PhoneViewer().Open(In("IMG_1.JPG")))!.Message)
            .IsEqualTo("Photos cannot be opened in another app from PhotoSense on a phone yet.");
    }
}
