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
    public async Task Nothing_In_The_Library_Can_Be_Changed_Yet()
    {
        _library.Add("A1", File(LibraryFileKind.Photo, "IMG_1.JPG"));
        var picture = In("IMG_1.JPG");
        Action[] changes =
        [
            () => _photos.Move(picture, In("IMG_2.JPG")), () => _photos.Copy(picture, In("IMG_2.JPG")), () => _photos.MakeFolder(In("Trips")),
            () => _photos.RemoveEmptyFolder(In("Trips")), () => _photos.DeleteCopy(picture), () => _photos.Erase(picture),
        ];
        foreach (var change in changes)
            await Assert.That(Assert.Throws<UnauthorizedAccessException>(change)!.Message).IsEqualTo(PhotoLibraryFileSystem.ReadOnly);
        await Assert.That(Listed()).IsEquivalentTo(["IMG_1.JPG"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task The_Library_Is_The_One_Folder_Offered_And_No_Other_Program_Opens_Its_Photos()
    {
        var browser = new PhotoLibraryBrowser(_photos);

        var places = browser.Browse(" ");
        await Assert.That((places.Path, places.Parent, places.Folders.Single())).IsEqualTo(((string?)null, (string?)null, new FolderEntry("Photo Library", Root)));
        var inside = browser.Browse(Root);
        await Assert.That((inside.Path, inside.Parent, inside.Folders.Count)).IsEqualTo((Root, (string?)null, 0));
        await Assert.That(Assert.Throws<DirectoryNotFoundException>(() => browser.Browse(In("Album")))!.Message).IsEqualTo($"The photo library has no folder {In("Album")}.");

        await Assert.That(Assert.Throws<InvalidOperationException>(() => new PhoneViewer().Open(In("IMG_1.JPG")))!.Message)
            .IsEqualTo("Photos cannot be opened in another app from PhotoSense on a phone yet.");
    }
}
