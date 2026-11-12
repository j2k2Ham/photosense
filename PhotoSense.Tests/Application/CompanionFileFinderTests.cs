using Moq;
using PhotoSense.Application.Changes;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Application.Removal;
using PhotoSense.Infrastructure.Persistence;
using PhotoSense.Tests.Application;

namespace PhotoSense.Tests.Application;

/// <summary>
/// What goes with a photo when it is removed. Each file's content stands in for the Live Photo
/// identifier it carries ("id:..."), so the rules can be exercised without real media files.
/// </summary>
public sealed class CompanionFileFinderTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("photosense-companions-");
    private readonly CompanionFileFinder _finder = new(TestFiles.Disk, (path, _) => Task.FromResult(ContentAsId(path)));

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

    private async Task<List<string>> NamesAsync(string path, CancellationToken ct) => (await _finder.FindForAsync(path, ct)).Select(f => Path.GetFileName(f)!).OrderBy(n => n, StringComparer.Ordinal).ToList();

    [Test]
    public async Task A_Picture_Takes_Its_Live_Photo_Video_And_Both_Sidecars(CancellationToken ct)
    {
        var picture = Put("IMG_1234.HEIC", "id:AAAA");
        Put("IMG_1234.MOV", "id:aaaa"); // identifiers are compared without regard to case
        Put("IMG_1234.AAE");
        Put("IMG_O1234.AAE");
        Put("IMG_1235.HEIC", "id:BBBB"); // a different item entirely
        Put("IMG_1235.MOV", "id:BBBB");
        Put("IMG_1235.AAE");

        await Assert.That(await NamesAsync(picture, ct)).IsEquivalentTo(["IMG_1234.AAE", "IMG_1234.MOV", "IMG_O1234.AAE"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Nothing_Goes_While_Another_Picture_Of_The_Same_Shot_Stays(CancellationToken ct)
    {
        // The usual case after an iPhone import: the HEIC, its JPEG conversion, one video, one sidecar.
        var heic = Put("IMG_1234.HEIC", "id:AAAA");
        var jpeg = Put("IMG_1234.JPG", "id:AAAA");
        Put("IMG_1234.MOV", "id:AAAA");
        Put("IMG_1234.AAE");

        await Assert.That(await NamesAsync(heic, ct)).IsEmpty();
        await Assert.That(await NamesAsync(jpeg, ct)).IsEmpty();

        File.Delete(heic); // once the other format is gone, the last picture takes them
        await Assert.That(await NamesAsync(jpeg, ct)).IsEquivalentTo(["IMG_1234.AAE", "IMG_1234.MOV"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task An_Edited_Version_And_Its_Original_Share_Their_Sidecars(CancellationToken ct)
    {
        var original = Put("IMG_1234.HEIC", "id:AAAA");
        var edited = Put("IMG_E1234.HEIC", "id:EEEE");
        Put("IMG_E1234.MOV", "id:EEEE");
        Put("IMG_1234.MOV", "id:AAAA");
        Put("IMG_1234.AAE");
        Put("IMG_O1234.AAE");

        await Assert.That(await NamesAsync(edited, ct)).IsEmpty();   // the original stays
        await Assert.That(await NamesAsync(original, ct)).IsEmpty(); // the edited version stays
    }

    [Test]
    public async Task A_Video_That_Merely_Shares_The_Name_Is_Never_Taken(CancellationToken ct)
    {
        // A real 1.3 GB video sat beside a same-numbered picture in the sample library.
        var picture = Put("IMG_0723.JPG");                    // carries no identifier
        Put("IMG_0723.MOV");                                  // nor does the video
        await Assert.That(await NamesAsync(picture, ct)).IsEmpty();

        var live = Put("IMG_3405.JPG", "id:52E3");
        Put("IMG_3405.MOV", "id:FF8F");                       // somebody else's Live Photo video
        Put("IMG_3405.AAE");
        await Assert.That(await NamesAsync(live, ct)).IsEmpty();                            // and the sidecar stays, since a video of that name remains

        var half = Put("IMG_0001.HEIC");                      // picture without an identifier, video with one
        Put("IMG_0001.MOV", "id:AAAA");
        await Assert.That(await NamesAsync(half, ct)).IsEmpty();
    }

    [Test]
    public async Task Sidecars_Go_With_The_Last_File_Of_Their_Item_Whatever_It_Is(CancellationToken ct)
    {
        var picture = Put("IMG_2000.PNG");
        Put("IMG_2000.AAE");
        await Assert.That(await NamesAsync(picture, ct)).IsEquivalentTo(["IMG_2000.AAE"], CollectionOrdering.Matching);

        var video = Put("IMG_3000.MOV");
        Put("IMG_3000.AAE");
        Put("IMG_O3000.AAE");
        await Assert.That(await NamesAsync(video, ct)).IsEquivalentTo(["IMG_3000.AAE", "IMG_O3000.AAE"], CollectionOrdering.Matching);

        var liveVideo = Put("IMG_4000.MOV", "id:AAAA");       // removing just the video half leaves the picture its sidecar
        Put("IMG_4000.HEIC", "id:AAAA");
        Put("IMG_4000.AAE");
        await Assert.That(await NamesAsync(liveVideo, ct)).IsEmpty();
    }

    [Test]
    public async Task Looks_Only_In_The_Photos_Own_Folder(CancellationToken ct)
    {
        var picture = Put(Path.Combine("a", "IMG_1234.HEIC"), "id:AAAA");
        Put(Path.Combine("b", "IMG_1234.MOV"), "id:AAAA");
        Put(Path.Combine("b", "IMG_1234.AAE"));
        await Assert.That(await NamesAsync(picture, ct)).IsEmpty();
        await Assert.That(await _finder.FindForAsync(Path.Combine(_root.FullName, "missing-folder", "IMG_1.HEIC"), ct)).IsEmpty();
    }

    // Removes photos the way Clean up does, through a journal of its own.
    private DuplicateRemovalService Remover(InMemoryPhotoRepository repo, IPhotoFileSystem? disk = null)
        => new(new DuplicateAnalysisService(repo), repo, _finder,
            new FileChanges(new MemoryBatchStore(), repo, Mock.Of<IAuditRepository>(), disk ?? TestFiles.Disk), disk ?? TestFiles.Disk, new TestClock(TestClock.Day, TimeSpan.FromSeconds(1)));

    [Test]
    public async Task Removing_A_Photo_Moves_Its_Companions_With_It_And_Forgets_Their_Records(CancellationToken ct)
    {
        var repo = new InMemoryPhotoRepository();
        var service = Remover(repo);
        var picturePath = Put(Path.Combine("2024", "IMG_1234.HEIC"), "id:AAAA");
        var videoPath = Put(Path.Combine("2024", "IMG_1234.MOV"), "id:AAAA");
        var sidecarPath = Put(Path.Combine("2024", "IMG_1234.AAE"));
        var otherPath = Put(Path.Combine("2024", "IMG_9999.MOV"), "id:ZZZZ");
        var picture = Record(picturePath);
        var video = Record(videoPath); // scanned in its own right, as videos are
        await repo.AddOrUpdateAsync(picture, ct);
        await repo.AddOrUpdateAsync(video, ct);

        var result = await service.RemoveAsync(picture.Id, deleteFile: true, ct);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Companions.OrderBy(p => p)).IsEquivalentTo(new[] { sidecarPath, videoPath }.OrderBy(p => p), CollectionOrdering.Matching);
        var held = Path.Combine(_root.FullName, PhotoStorageOptions.RemovedFolderName, "2024");
        await Assert.That(Directory.GetFiles(held).Select(f => Path.GetFileName(f)!).OrderBy(n => n)).IsEquivalentTo(["IMG_1234.AAE", "IMG_1234.HEIC", "IMG_1234.MOV"], CollectionOrdering.Matching);
        await Assert.That(File.Exists(videoPath) || File.Exists(sidecarPath)).IsFalse();
        await Assert.That(File.Exists(otherPath)).IsTrue();
        await Assert.That(await repo.GetAllAsync(ct)).IsEmpty();

        // Forgetting a record without deleting takes nothing with it.
        var kept = Record(Put("IMG_5000.HEIC", "id:CCCC"));
        Put("IMG_5000.AAE");
        await repo.AddOrUpdateAsync(kept, ct);
        await Assert.That((await service.RemoveAsync(kept.Id, deleteFile: false, ct)).Companions).IsEmpty();
        await Assert.That(File.Exists(Path.Combine(_root.FullName, "IMG_5000.AAE"))).IsTrue();
    }

    [Test]
    public async Task A_Companion_That_Cannot_Be_Moved_Keeps_Its_Photo_Where_It_Is_Too(CancellationToken ct)
    {
        var repo = new InMemoryPhotoRepository();
        var sidecarPath = Put("IMG_1234.AAE");
        var service = Remover(repo, new FailingDisk().Fail("move", sidecarPath));
        var picture = Record(Put("IMG_1234.HEIC", "id:AAAA"));
        await repo.AddOrUpdateAsync(picture, ct);

        var result = await service.RemoveAsync(picture.Id, deleteFile: true, ct);

        // Taking the photo without its sidecar would leave the sidecar with nothing to belong to.
        await Assert.That((result.Outcome, result.Error)).IsEqualTo((RemovalOutcome.Failed, (string?)"IMG_1234.AAE could not be moved: The disk would not move IMG_1234.AAE. Nothing was removed: every file is where it was."));
        await Assert.That(File.Exists(sidecarPath) && File.Exists(picture.SourcePath)).IsTrue();
        await Assert.That(await repo.GetAsync(picture.Id, ct)).IsNotNull();
    }

    private Photo Record(string path)
    {
        var info = new FileInfo(path);
        return new Photo { SourcePath = path, FileName = info.Name, FileSizeBytes = info.Length, FileModifiedUtc = info.LastWriteTimeUtc, ScanRoot = _root.FullName, ContentHash = info.Name };
    }

    [Test]
    public async Task What_Is_Not_Inside_A_Folder_Has_No_Companions(CancellationToken ct)
        => await Assert.That(await _finder.FindForAsync(Path.GetPathRoot(_root.FullName)!, ct)).IsEmpty();
}
