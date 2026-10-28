using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Files;

namespace PhotoSense.Tests.Infrastructure;

/// <summary>The computer's own disks, as everything that lists, reads or changes photos goes through them.</summary>
public sealed class DiskFileSystemTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("photosense-disk-");
    private readonly DiskFileSystem _disk = new();

    public void Dispose() => TestFiles.Remove(_root);

    private string In(params string[] parts) => Path.Combine([_root.FullName, .. parts]);

    private string Put(string relative, string content = "data")
    {
        var path = In(relative.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static List<string> Names(IEnumerable<string> paths) => paths.Select(p => Path.GetFileName(p)!).Order(StringComparer.Ordinal).ToList();

    // ---- looking

    [Test]
    public async Task Tells_What_Is_There_And_Describes_A_File_By_Its_Size_And_When_It_Last_Changed()
    {
        var file = Put("IMG_1.JPG", "four");
        var when = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, when);

        await Assert.That((_disk.FileExists(file), _disk.FolderExists(_root.FullName), _disk.FileExists(_root.FullName), _disk.FolderExists(file))).IsEqualTo((true, true, false, false));
        await Assert.That(_disk.Describe(file)).IsEqualTo(new StoredFile(4, when));
        await Assert.That(_disk.Describe(In("gone.JPG"))).IsNull();
    }

    [Test]
    public async Task Lists_The_Files_And_Folders_Directly_Inside_A_Folder_Hidden_Ones_Too()
    {
        Put("IMG_1.JPG");
        var hidden = Put(".IMG_1.AAE");
        File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        Put("2024/IMG_2.JPG");
        var hiddenFolder = Directory.CreateDirectory(In(".thumbs"));
        hiddenFolder.Attributes |= FileAttributes.Hidden;

        await Assert.That(Names(_disk.FilesIn(_root.FullName))).IsEquivalentTo([".IMG_1.AAE", "IMG_1.JPG"], CollectionOrdering.Matching);
        await Assert.That(Names(_disk.FoldersIn(_root.FullName))).IsEquivalentTo([".thumbs", "2024"], CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("not there")]
    [Arguments("")]
    public async Task A_Folder_That_Is_Not_There_Holds_Nothing(string name)
    {
        // An empty name is the folder of a bare file name, which is no folder at all.
        var folder = name.Length > 0 ? In(name) : name;
        await Assert.That((_disk.FilesIn(folder).Count, _disk.FoldersIn(folder).Count)).IsEqualTo((0, 0));
    }

    [Test]
    public async Task A_Link_To_A_Folder_Elsewhere_Is_Known_For_One_And_Never_Listed()
    {
        var file = Put("Real/IMG_1.JPG");
        Directory.CreateDirectory(In("Phone"));
        using var link = TestFiles.LinkFolder(In("Phone", "Link"), In("Real"));

        await Assert.That((_disk.IsLink(In("Phone", "Link")), _disk.IsLink(In("Real")), _disk.IsLink(file), _disk.IsLink(In("Not there")))).IsEqualTo((true, false, false, false));
        await Assert.That(_disk.FoldersIn(In("Phone"))).IsEmpty();
    }

    [Test]
    public async Task A_Link_To_A_File_Is_Known_For_One_And_Listed_Like_Any_Other_File()
    {
        var file = Put("Real/IMG_1.JPG");
        var link = In("Real", "IMG_1 link.JPG");
        try { File.CreateSymbolicLink(link, file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; } // Windows lets only some accounts make one

        await Assert.That(_disk.IsLink(link)).IsTrue();
        await Assert.That(_disk.FilesIn(In("Real"))).Contains(link);
    }

    [Test]
    public async Task A_Folder_That_May_Not_Be_Looked_Into_Holds_Nothing()
    {
        Put("Private/IMG_1.JPG");
        using var denied = TestFiles.DenyAccess(In("Private"));
        if (denied is null) return; // nothing is out of reach of the account running this
        await Assert.That(_disk.FilesIn(In("Private"))).IsEmpty();
    }

    // ---- reading

    [Test]
    public async Task A_File_Being_Read_Can_Still_Be_Moved_And_Is_Read_From_Any_Point()
    {
        var file = Put("clip.MOV", "0123456789");
        await using var reading = _disk.OpenRead(file);

        _disk.Move(file, In("moved.MOV"));
        reading.Position = 6;
        await Assert.That(new StreamReader(reading).ReadToEnd()).IsEqualTo("6789");
        await Assert.That(File.Exists(In("moved.MOV"))).IsTrue();
    }

    [Test]
    public async Task A_File_Is_A_Copy_Of_Another_When_It_Holds_What_The_Other_Begins_With()
    {
        var bytes = new byte[200_000];
        new Random(7).NextBytes(bytes);
        var changed = bytes.ToArray();
        changed[150_000] ^= 0xFF;
        string Write(string name, byte[] content)
        {
            var path = In(name);
            File.WriteAllBytes(path, content);
            return path;
        }
        var original = Write("original.bin", bytes);

        await Assert.That(_disk.IsCopyOf(Write("whole.bin", bytes), original)).IsTrue();
        await Assert.That(_disk.IsCopyOf(Write("part.bin", bytes[..100_000]), original)).IsTrue();
        await Assert.That(_disk.IsCopyOf(Write("empty.bin", []), original)).IsTrue();
        await Assert.That(_disk.IsCopyOf(Write("longer.bin", [.. bytes, 1]), original)).IsFalse();
        await Assert.That(_disk.IsCopyOf(Write("differs.bin", changed), original)).IsFalse();
    }

    // ---- changing

    [Test]
    public async Task Moves_And_Copies_Never_Replace_A_File()
    {
        var picture = Put("IMG_1.JPG", "mine");
        var there = Put("There/IMG_1.JPG", "someone else's");

        Assert.Throws<IOException>(() => _disk.Move(picture, there));
        Assert.Throws<IOException>(() => _disk.Copy(picture, there));
        await Assert.That((File.ReadAllText(picture), File.ReadAllText(there))).IsEqualTo(("mine", "someone else's"));

        _disk.Copy(picture, In("There", "copy.JPG"));
        _disk.Move(picture, In("There", "moved.JPG"));
        await Assert.That((File.Exists(picture), File.ReadAllText(In("There", "copy.JPG")), File.ReadAllText(In("There", "moved.JPG")))).IsEqualTo((false, "mine", "mine"));
    }

    [Test]
    public async Task Makes_Folders_And_Takes_One_Away_Only_When_It_Is_Empty()
    {
        _disk.MakeFolder(In("2024", "May"));
        Directory.CreateDirectory(In("empty"));
        Put("full/a.txt");

        _disk.RemoveEmptyFolder(In("empty"));
        _disk.RemoveEmptyFolder(In("full"));
        _disk.RemoveEmptyFolder(In("never there"));

        await Assert.That((Directory.Exists(In("2024", "May")), Directory.Exists(In("empty")), File.Exists(In("full", "a.txt")))).IsEqualTo((true, false, true));
    }

    [Test]
    public async Task Deletes_A_Copy_And_Erases_A_File_Even_When_It_Is_Marked_Read_Only()
    {
        var copy = Put("copy.JPG");
        var plain = Put("IMG_1.JPG");
        var marked = Put("IMG_2.JPG");
        File.SetAttributes(marked, File.GetAttributes(marked) | FileAttributes.ReadOnly);

        _disk.DeleteCopy(copy);
        _disk.Erase(plain);
        _disk.Erase(marked);

        await Assert.That(Directory.EnumerateFileSystemEntries(_root.FullName)).IsEmpty();
    }
}
