using PhotoSense.Application.Scanning;
using PhotoSense.Domain.Configuration;
using Xunit;

namespace PhotoSense.Tests.Application;

public sealed class PhotoFileEnumeratorTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("photosense-enum-");

    public void Dispose() => _root.Delete(true);

    private void Touch(params string[] parts)
    {
        var path = Path.Combine([_root.FullName, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1]);
    }

    private List<string> Names(bool recursive) => PhotoFileEnumerator.Enumerate(_root.FullName, recursive).Select(f => Path.GetFileName(f)!).OrderBy(x => x, StringComparer.Ordinal).ToList();

    [Fact]
    public void Finds_Pictures_And_Videos_And_Nothing_Else()
    {
        var media = new[] { "a.jpg", "b.JPEG", "c.png", "d.gif", "e.bmp", "f.tiff", "g.tif", "h.webp", "i.HEIC", "j.heif", "k.MOV", "l.mp4", "m.m4v", "n.3gp" };
        foreach (var f in media.Concat(["o.txt", "p.aae", "q.raw", "noextension"])) Touch(f);
        Assert.Equal(media.OrderBy(x => x, StringComparer.Ordinal), Names(recursive: false));
    }

    [Fact]
    public void Looks_In_Subfolders_Only_When_Asked()
    {
        Touch("a.jpg");
        Touch("nested", "deeper", "b.png");
        Assert.Equal(["a.jpg", "b.png"], Names(recursive: true));
        Assert.Equal(["a.jpg"], Names(recursive: false));
    }

    [Fact]
    public void Never_Looks_In_The_Folder_Removed_Files_Are_Held_In()
    {
        Touch("a.jpg");
        Touch(PhotoStorageOptions.RemovedFolderName, "a (1).jpg");
        Touch("album", PhotoStorageOptions.RemovedFolderName.ToLowerInvariant(), "b (1).jpg");
        Assert.Equal(["a.jpg"], Names(recursive: true));
    }

    [Fact]
    public void Does_Not_Follow_A_Link_To_Another_Folder()
    {
        Touch("real", "a.jpg");
        try { Directory.CreateSymbolicLink(Path.Combine(_root.FullName, "link"), Path.Combine(_root.FullName, "real")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; } // creating links needs a privilege this account may lack
        Assert.Equal(["a.jpg"], Names(recursive: true));
    }

    [Theory]
    [InlineData("nonexistent_root_123")]
    [InlineData("")]
    [InlineData(" ")]
    public void A_Folder_That_Is_Not_There_Has_No_Files(string root)
        => Assert.Empty(PhotoFileEnumerator.Enumerate(root, true));

    [Theory]
    [InlineData("IMG_1.HEIC", true)]
    [InlineData("clip.MOV", true)]
    [InlineData("IMG_1.AAE", false)]
    public void Knows_Which_Files_It_Scans(string name, bool supported)
        => Assert.Equal(supported, PhotoFileEnumerator.IsSupported(name));

    [Fact]
    public void A_Folder_That_Goes_Away_While_Being_Listed_Is_Passed_Over()
    {
        Touch("first", "a.jpg");
        Touch("second", "b.jpg");
        using var files = PhotoFileEnumerator.Enumerate(_root.FullName, recursive: true).GetEnumerator();
        Assert.True(files.MoveNext());
        // Both folders are known by now; the one not yet looked in is taken away.
        var other = Path.GetFileName(files.Current) == "a.jpg" ? "second" : "first";
        Directory.Delete(Path.Combine(_root.FullName, other), recursive: true);
        Assert.False(files.MoveNext());
    }

    [Fact]
    public void A_Folder_That_May_Not_Be_Read_Is_Passed_Over()
    {
        Touch("a.jpg");
        Touch("private", "b.jpg");
        Touch("public", "c.jpg");
        using var denied = TestFiles.DenyAccess(Path.Combine(_root.FullName, "private"));
        if (denied is null) return; // nothing is out of reach of the account running this
        Assert.Equal(["a.jpg", "c.jpg"], Names(recursive: true));
    }
}
