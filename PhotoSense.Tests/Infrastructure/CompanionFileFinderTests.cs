using Moq;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Deletion;
using PhotoSense.Infrastructure.Persistence;
using Xunit;

namespace PhotoSense.Tests.Infrastructure;

/// <summary>
/// What goes with a photo when it is removed. Each file's content stands in for the Live Photo
/// identifier it carries ("id:..."), so the rules can be exercised without real media files.
/// </summary>
public sealed class CompanionFileFinderTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("photosense-companions-");
    private readonly CompanionFileFinder _finder = new(ContentAsId);

    public void Dispose() => _root.Delete(true);

    private static string? ContentAsId(string path)
    {
        var text = File.ReadAllText(path);
        return text.StartsWith("id:", StringComparison.Ordinal) ? text[3..] : null;
    }

    private string Put(string name, string content = "data")
    {
        var path = Path.Combine(_root.FullName, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private List<string> Names(string path) => _finder.FindFor(path).Select(f => Path.GetFileName(f)!).OrderBy(n => n, StringComparer.Ordinal).ToList();

    [Fact]
    public void A_Picture_Takes_Its_Live_Photo_Video_And_Both_Sidecars()
    {
        var picture = Put("IMG_1234.HEIC", "id:AAAA");
        Put("IMG_1234.MOV", "id:aaaa"); // identifiers are compared without regard to case
        Put("IMG_1234.AAE");
        Put("IMG_O1234.AAE");
        Put("IMG_1235.HEIC", "id:BBBB"); // a different item entirely
        Put("IMG_1235.MOV", "id:BBBB");
        Put("IMG_1235.AAE");

        Assert.Equal(["IMG_1234.AAE", "IMG_1234.MOV", "IMG_O1234.AAE"], Names(picture));
    }

    [Fact]
    public void Nothing_Goes_While_Another_Picture_Of_The_Same_Shot_Stays()
    {
        // The usual case after an iPhone import: the HEIC, its JPEG conversion, one video, one sidecar.
        var heic = Put("IMG_1234.HEIC", "id:AAAA");
        var jpeg = Put("IMG_1234.JPG", "id:AAAA");
        Put("IMG_1234.MOV", "id:AAAA");
        Put("IMG_1234.AAE");

        Assert.Empty(Names(heic));
        Assert.Empty(Names(jpeg));

        File.Delete(heic); // once the other format is gone, the last picture takes them
        Assert.Equal(["IMG_1234.AAE", "IMG_1234.MOV"], Names(jpeg));
    }

    [Fact]
    public void An_Edited_Version_And_Its_Original_Share_Their_Sidecars()
    {
        var original = Put("IMG_1234.HEIC", "id:AAAA");
        var edited = Put("IMG_E1234.HEIC", "id:EEEE");
        Put("IMG_E1234.MOV", "id:EEEE");
        Put("IMG_1234.MOV", "id:AAAA");
        Put("IMG_1234.AAE");
        Put("IMG_O1234.AAE");

        Assert.Empty(Names(edited));   // the original stays
        Assert.Empty(Names(original)); // the edited version stays
    }

    [Fact]
    public void A_Video_That_Merely_Shares_The_Name_Is_Never_Taken()
    {
        // A real 1.3 GB video sat beside a same-numbered picture in the sample library.
        var picture = Put("IMG_0723.JPG");                    // carries no identifier
        Put("IMG_0723.MOV");                                  // nor does the video
        Assert.Empty(Names(picture));

        var live = Put("IMG_3405.JPG", "id:52E3");
        Put("IMG_3405.MOV", "id:FF8F");                       // somebody else's Live Photo video
        Put("IMG_3405.AAE");
        Assert.Empty(Names(live));                            // and the sidecar stays, since a video of that name remains

        var half = Put("IMG_0001.HEIC");                      // picture without an identifier, video with one
        Put("IMG_0001.MOV", "id:AAAA");
        Assert.Empty(Names(half));
    }

    [Fact]
    public void Sidecars_Go_With_The_Last_File_Of_Their_Item_Whatever_It_Is()
    {
        var picture = Put("IMG_2000.PNG");
        Put("IMG_2000.AAE");
        Assert.Equal(["IMG_2000.AAE"], Names(picture));

        var video = Put("IMG_3000.MOV");
        Put("IMG_3000.AAE");
        Put("IMG_O3000.AAE");
        Assert.Equal(["IMG_3000.AAE", "IMG_O3000.AAE"], Names(video));

        var liveVideo = Put("IMG_4000.MOV", "id:AAAA");       // removing just the video half leaves the picture its sidecar
        Put("IMG_4000.HEIC", "id:AAAA");
        Put("IMG_4000.AAE");
        Assert.Empty(Names(liveVideo));
    }

    [Fact]
    public void Looks_Only_In_The_Photos_Own_Folder()
    {
        var picture = Put(Path.Combine("a", "IMG_1234.HEIC"), "id:AAAA");
        Put(Path.Combine("b", "IMG_1234.MOV"), "id:AAAA");
        Put(Path.Combine("b", "IMG_1234.AAE"));
        Assert.Empty(Names(picture));
        Assert.Empty(_finder.FindFor(Path.Combine(_root.FullName, "missing-folder", "IMG_1.HEIC")));
    }

    [Fact]
    public async Task Removing_A_Photo_Moves_Its_Companions_With_It_And_Forgets_Their_Records()
    {
        var repo = new InMemoryPhotoRepository();
        var service = new FileSystemPhotoDeletionService(repo, Mock.Of<IIntegrationEventPublisher>(), _finder);
        var picturePath = Put(Path.Combine("2024", "IMG_1234.HEIC"), "id:AAAA");
        var videoPath = Put(Path.Combine("2024", "IMG_1234.MOV"), "id:AAAA");
        var sidecarPath = Put(Path.Combine("2024", "IMG_1234.AAE"));
        var otherPath = Put(Path.Combine("2024", "IMG_9999.MOV"), "id:ZZZZ");
        var picture = Record(picturePath);
        var video = Record(videoPath); // scanned in its own right, as videos are
        await repo.AddOrUpdateAsync(picture);
        await repo.AddOrUpdateAsync(video);

        var result = await service.DeleteAsync(picture.Id, deleteFile: true);

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { sidecarPath, videoPath }.OrderBy(p => p), result.Companions.OrderBy(p => p));
        var held = Path.Combine(_root.FullName, PhotoStorageOptions.RemovedFolderName, "2024");
        Assert.Equal(["IMG_1234.AAE", "IMG_1234.HEIC", "IMG_1234.MOV"], Directory.GetFiles(held).Select(Path.GetFileName).OrderBy(n => n));
        Assert.False(File.Exists(videoPath) || File.Exists(sidecarPath));
        Assert.True(File.Exists(otherPath));
        Assert.Empty(await repo.GetAllAsync());

        // Forgetting a record without deleting takes nothing with it.
        var kept = Record(Put("IMG_5000.HEIC", "id:CCCC"));
        Put("IMG_5000.AAE");
        await repo.AddOrUpdateAsync(kept);
        Assert.Empty((await service.DeleteAsync(kept.Id, deleteFile: false)).Companions);
        Assert.True(File.Exists(Path.Combine(_root.FullName, "IMG_5000.AAE")));
    }

    [Fact]
    public async Task A_Companion_That_Cannot_Be_Moved_Stays_And_The_Photo_Still_Goes()
    {
        var repo = new InMemoryPhotoRepository();
        var service = new FileSystemPhotoDeletionService(repo, Mock.Of<IIntegrationEventPublisher>(), _finder);
        var picture = Record(Put("IMG_1234.HEIC", "id:AAAA"));
        var sidecarPath = Put("IMG_1234.AAE");
        await repo.AddOrUpdateAsync(picture);

        RemovalResult result;
        using (new FileStream(sidecarPath, FileMode.Open, FileAccess.Read, FileShare.None))
            result = await service.DeleteAsync(picture.Id, deleteFile: true);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Companions);
        Assert.True(File.Exists(sidecarPath));
        Assert.False(File.Exists(picture.SourcePath));
    }

    private Photo Record(string path)
    {
        var info = new FileInfo(path);
        return new Photo { SourcePath = path, FileName = info.Name, FileSizeBytes = info.Length, FileModifiedUtc = info.LastWriteTimeUtc, ScanRoot = _root.FullName, ContentHash = info.Name };
    }
}
