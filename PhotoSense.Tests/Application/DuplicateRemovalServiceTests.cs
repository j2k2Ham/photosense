using Moq;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Deletion;
using PhotoSense.Infrastructure.Persistence;

namespace PhotoSense.Tests.Application;

public sealed class DuplicateRemovalServiceTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("photosense-removal-");
    private readonly InMemoryPhotoRepository _repo = new();
    private readonly DuplicateRemovalService _service;

    public DuplicateRemovalServiceTests()
    {
        var analysis = new DuplicateAnalysisService(_repo);
        _service = new DuplicateRemovalService(analysis, new FileSystemPhotoDeletionService(_repo, Mock.Of<IIntegrationEventPublisher>(), new CompanionFileFinder(_ => null)));
    }

    public void Dispose() => _root.Delete(true);

    // A real file with a record that matches it, as a scan would leave it.
    private async Task<Photo> AddAsync(string relativePath, string contentHash, bool kept = false, PhotoSet set = PhotoSet.Primary)
    {
        var path = Path.Combine(_root.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, contentHash);
        var info = new FileInfo(path);
        var photo = new Photo
        {
            SourcePath = path, FileName = info.Name, FileSizeBytes = info.Length, FileModifiedUtc = info.LastWriteTimeUtc,
            ScanRoot = _root.FullName, ContentHash = contentHash, Set = set, IsKept = kept
        };
        await _repo.AddOrUpdateAsync(photo);
        return photo;
    }

    private string Held(string relativePath) => Path.Combine(_root.FullName, PhotoStorageOptions.RemovedFolderName, relativePath);

    [Test]
    public async Task Moves_Every_Duplicate_Aside_And_Leaves_The_Best_Copy()
    {
        var keeper = await AddAsync("IMG_1.jpg", "AAAA");
        var copy = await AddAsync(Path.Combine("backup", "IMG_1 (1).jpg"), "AAAA");
        var unique = await AddAsync("IMG_2.jpg", "BBBB");

        var result = await _service.RemoveDuplicatesAsync();

        await Assert.That(result.Removed).IsEqualTo(1);
        await Assert.That(result.Bytes).IsEqualTo(copy.FileSizeBytes);
        await Assert.That(result.Skipped).IsEqualTo(0);
        await Assert.That(result.Problems).IsEmpty();
        await Assert.That(File.Exists(keeper.SourcePath)).IsTrue();
        await Assert.That(File.Exists(unique.SourcePath)).IsTrue();
        await Assert.That(File.Exists(copy.SourcePath)).IsFalse();
        await Assert.That(File.Exists(Held(Path.Combine("backup", "IMG_1 (1).jpg")))).IsTrue();
        await Assert.That(await _repo.GetAsync(copy.Id)).IsNull();
        await Assert.That(await _repo.GetAsync(keeper.Id)).IsNotNull();
    }

    [Test]
    public async Task Leaves_Copies_Alone_When_The_Photo_Being_Kept_Is_Gone()
    {
        var keeper = await AddAsync("IMG_1.jpg", "AAAA");
        var copy = await AddAsync("IMG_1 (1).jpg", "AAAA");
        File.Delete(keeper.SourcePath);

        var result = await _service.RemoveDuplicatesAsync();

        await Assert.That(result.Removed).IsEqualTo(0);
        await Assert.That(result.Skipped).IsEqualTo(1);
        await Assert.That(result.Problems.Single()).Contains(keeper.SourcePath);
        await Assert.That(File.Exists(copy.SourcePath)).IsTrue();
    }

    [Test]
    public async Task Leaves_Copies_Alone_When_The_Photo_Being_Kept_Has_Changed()
    {
        var keeper = await AddAsync("IMG_1.jpg", "AAAA");
        var copy = await AddAsync("IMG_1 (1).jpg", "AAAA");
        await File.WriteAllTextAsync(keeper.SourcePath, "edited since the scan");

        var result = await _service.RemoveDuplicatesAsync();

        await Assert.That(result.Removed).IsEqualTo(0);
        await Assert.That(File.Exists(copy.SourcePath)).IsTrue();
    }

    [Test]
    public async Task Skips_A_Copy_That_Changed_Since_The_Scan()
    {
        await AddAsync("IMG_1.jpg", "AAAA");
        var copy = await AddAsync("IMG_1 (1).jpg", "AAAA");
        await File.WriteAllTextAsync(copy.SourcePath, "no longer the same file");

        var result = await _service.RemoveDuplicatesAsync();

        await Assert.That(result.Removed).IsEqualTo(0);
        await Assert.That(result.Skipped).IsEqualTo(1);
        await Assert.That(result.Problems.Single()).StartsWith("Changed since the scan");
        await Assert.That(File.Exists(copy.SourcePath)).IsTrue();
        await Assert.That(await _repo.GetAsync(copy.Id)).IsNotNull();
    }

    [Test]
    public async Task Never_Takes_A_Copy_Marked_Keep()
    {
        await AddAsync("IMG_1.jpg", "AAAA");
        var kept = await AddAsync("IMG_1 (1).jpg", "AAAA", kept: true);

        var result = await _service.RemoveDuplicatesAsync();

        await Assert.That(result.Removed).IsEqualTo(0);
        await Assert.That(result.Skipped).IsEqualTo(0);
        await Assert.That(File.Exists(kept.SourcePath)).IsTrue();
    }

    [Test]
    public async Task Can_Be_Limited_To_One_Group()
    {
        var keeperA = await AddAsync("a.jpg", "AAAA");
        var copyA = await AddAsync("a (1).jpg", "AAAA");
        await AddAsync("b.jpg", "BBBB");
        var copyB = await AddAsync("b (1).jpg", "BBBB");

        var result = await _service.RemoveDuplicatesAsync(keeperA.Id.ToString());

        await Assert.That(result.Removed).IsEqualTo(1);
        await Assert.That(File.Exists(copyA.SourcePath)).IsFalse();
        await Assert.That(File.Exists(copyB.SourcePath)).IsTrue();
        await Assert.That((await _service.RemoveDuplicatesAsync("no-such-group")).Removed).IsEqualTo(0);
    }

    [Test]
    public async Task Reports_A_Copy_That_Cannot_Be_Moved_But_Not_One_That_Is_Already_Gone()
    {
        var keeper = await AddAsync("IMG_1.jpg", "AAAA");
        var copy = await AddAsync("IMG_1 (1).jpg", "AAAA");
        var analysis = new DuplicateAnalysisService(_repo);
        var deleter = new Mock<IPhotoDeletionService>();
        deleter.SetupSequence(d => d.DeleteAsync(copy.Id, true, It.Is<IReadOnlyList<Photo>?>(of => of!.Single().Id == keeper.Id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemovalResult(RemovalOutcome.NotFound))
            .ReturnsAsync(new RemovalResult(RemovalOutcome.Failed, Error: "in use"))
            .ReturnsAsync(new RemovalResult(RemovalOutcome.LastCopy));
        var service = new DuplicateRemovalService(analysis, deleter.Object);

        // Already gone, as a Live Photo video is once its picture has taken it along: nothing to report.
        await Assert.That(await service.RemoveDuplicatesAsync()).IsEqualTo(new BulkRemovalResult(0, 0, 0, [], 0), BulkRemovalResults.Same);
        var failed = await service.RemoveDuplicatesAsync();
        await Assert.That(failed.Skipped).IsEqualTo(1);
        await Assert.That(failed.Problems.Single()).EndsWith("in use");
        // The copy turned out to be the kept photo itself, recorded under a second path.
        var itself = await service.RemoveDuplicatesAsync();
        await Assert.That((itself.Removed, itself.Skipped)).IsEqualTo((0, 1));
        await Assert.That(itself.Problems.Single()).IsEqualTo($"The same file as the photo kept, under another path, so it was left alone: {copy.SourcePath}");
        await Assert.That(File.Exists(keeper.SourcePath)).IsTrue();
    }

    // ---- one photo at a time

    [Test]
    public async Task A_Copy_Removed_On_Its_Own_Goes_And_The_Photo_Kept_Stays()
    {
        var keeper = await AddAsync("IMG_0377.JPG", "AAAA");
        var copy = await AddAsync("IMG_0648.JPG", "AAAA");

        var result = await _service.RemoveAsync(copy.Id, deleteFile: true);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.HeldAt).IsEqualTo(Held("IMG_0648.JPG"));
        await Assert.That(File.Exists(keeper.SourcePath)).IsTrue();
        await Assert.That(File.Exists(copy.SourcePath)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task A_Copy_Is_Never_Removed_Once_The_Photo_It_Copies_Is_Gone_Or_Changed(bool changed)
    {
        var keeper = await AddAsync("IMG_0377.JPG", "AAAA");
        var copy = await AddAsync("IMG_0648.JPG", "AAAA");
        var other = await AddAsync("IMG_0648 (1).JPG", "AAAA");
        // Done outside PhotoSense, after the scan.
        if (changed) await File.WriteAllTextAsync(keeper.SourcePath, "edited"); else File.Delete(keeper.SourcePath);

        var result = await _service.RemoveAsync(copy.Id, deleteFile: true);

        // Another copy is still there, but it is the photo kept that each copy was checked against.
        await Assert.That(result.Outcome).IsEqualTo(RemovalOutcome.LastCopy);
        await Assert.That(File.Exists(copy.SourcePath) && File.Exists(other.SourcePath)).IsTrue();
        await Assert.That(await _repo.GetAsync(copy.Id)).IsNotNull();
    }

    [Test]
    public async Task The_Photo_Kept_Can_Be_Removed_Instead_While_One_Of_Its_Copies_Stays()
    {
        var keeper = await AddAsync("IMG_1.jpg", "AAAA");
        var gone = await AddAsync("IMG_1 (1).jpg", "AAAA");
        var copy = await AddAsync("IMG_1 (2).jpg", "AAAA");
        File.Delete(gone.SourcePath);

        await Assert.That((await _service.RemoveAsync(keeper.Id, deleteFile: true)).Succeeded).IsTrue();

        await Assert.That(File.Exists(keeper.SourcePath)).IsFalse();
        await Assert.That(File.Exists(copy.SourcePath)).IsTrue();
    }

    [Test]
    public async Task The_Photo_Kept_Is_Never_Removed_Once_Its_Copies_Are_Gone()
    {
        var keeper = await AddAsync("IMG_1.jpg", "AAAA");
        var copy = await AddAsync("IMG_1 (1).jpg", "AAAA");
        File.Delete(copy.SourcePath);

        await Assert.That((await _service.RemoveAsync(keeper.Id, deleteFile: true)).Outcome).IsEqualTo(RemovalOutcome.LastCopy);
        await Assert.That(File.Exists(keeper.SourcePath)).IsTrue();
    }

    [Test]
    public async Task A_Photo_That_Is_Nobodys_Copy_Is_Removed_When_Asked_And_An_Unknown_One_Is_Reported()
    {
        await AddAsync("IMG_1.jpg", "AAAA");
        await AddAsync("IMG_1 (1).jpg", "AAAA");
        var unique = await AddAsync("IMG_2.jpg", "BBBB");

        await Assert.That((await _service.RemoveAsync(unique.Id, deleteFile: true)).Succeeded).IsTrue();
        await Assert.That(File.Exists(unique.SourcePath)).IsFalse();
        await Assert.That((await _service.RemoveAsync(unique.Id, deleteFile: true)).Outcome).IsEqualTo(RemovalOutcome.NotFound);
    }

    [Test]
    public async Task Forgetting_A_Copy_Leaves_Its_File_Whatever_Became_Of_The_Photo_Kept()
    {
        var keeper = await AddAsync("IMG_1.jpg", "AAAA");
        var copy = await AddAsync("IMG_1 (1).jpg", "AAAA");
        File.Delete(keeper.SourcePath);

        await Assert.That((await _service.RemoveAsync(copy.Id, deleteFile: false)).Succeeded).IsTrue();
        await Assert.That(File.Exists(copy.SourcePath)).IsTrue();
        await Assert.That(await _repo.GetAsync(copy.Id)).IsNull();
    }

    [Test]
    public async Task A_Picture_Takes_Its_Live_Photo_Video_Along_And_The_Kept_Copy_Keeps_Its_Own()
    {
        // Records made before Live Photo identifiers were stored, so the videos appear as a group of their own.
        var service = new DuplicateRemovalService(new DuplicateAnalysisService(_repo),
            new FileSystemPhotoDeletionService(_repo, Mock.Of<IIntegrationEventPublisher>(), new CompanionFileFinder(_ => "LIVE-1")));
        var keptPicture = await AddAsync(Path.Combine("a", "IMG_1.JPG"), "PPPP");
        var keptVideo = await AddAsync(Path.Combine("a", "IMG_1.MOV"), "VVVV");
        var copyPicture = await AddAsync(Path.Combine("b", "IMG_1.JPG"), "PPPP");
        var copyVideo = await AddAsync(Path.Combine("b", "IMG_1.MOV"), "VVVV");

        var result = await service.RemoveDuplicatesAsync();

        await Assert.That((result.Removed, result.Companions, result.Skipped)).IsEqualTo((1, 1, 0));
        await Assert.That(result.Problems).IsEmpty(); // the video had already gone with its picture: nothing to report
        await Assert.That(File.Exists(keptPicture.SourcePath) && File.Exists(keptVideo.SourcePath)).IsTrue();
        await Assert.That(File.Exists(copyPicture.SourcePath) || File.Exists(copyVideo.SourcePath)).IsFalse();
        await Assert.That(File.Exists(Held(Path.Combine("b", "IMG_1.MOV")))).IsTrue();
        await Assert.That((await _repo.GetAllAsync()).Count).IsEqualTo(2);
    }

    [Test]
    public async Task The_Last_Copy_Of_A_Video_Is_Never_Removed_As_Somebody_Elses_Duplicate()
    {
        // The picture kept is in b, but of the two identical videos the one in a ranks first.
        var service = new DuplicateRemovalService(new DuplicateAnalysisService(_repo),
            new FileSystemPhotoDeletionService(_repo, Mock.Of<IIntegrationEventPublisher>(), new CompanionFileFinder(_ => "LIVE-1")));
        var pictureA = await AddAsync(Path.Combine("a", "IMG_1.JPG"), "PPPP", set: PhotoSet.Secondary);
        var videoA = await AddAsync(Path.Combine("a", "IMG_1.MOV"), "VVVV");
        var pictureB = await AddAsync(Path.Combine("b", "IMG_1.JPG"), "PPPP");
        var videoB = await AddAsync(Path.Combine("b", "IMG_1.MOV"), "VVVV", set: PhotoSet.Secondary);

        var result = await service.RemoveDuplicatesAsync();

        // Pictures go first: a's picture leaves with its video. The video group then finds its keeper gone
        // and leaves b's video alone, so the Live Photo in b stays whole.
        await Assert.That((result.Removed, result.Companions, result.Skipped)).IsEqualTo((1, 1, 1));
        await Assert.That(File.Exists(pictureB.SourcePath) && File.Exists(videoB.SourcePath)).IsTrue();
        await Assert.That(File.Exists(pictureA.SourcePath) || File.Exists(videoA.SourcePath)).IsFalse();
    }

    [Test]
    public async Task Stops_When_Cancelled()
    {
        await AddAsync("IMG_1.jpg", "AAAA");
        var copy = await AddAsync("IMG_1 (1).jpg", "AAAA");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => _service.RemoveDuplicatesAsync(ct: cts.Token));
        await Assert.That(File.Exists(copy.SourcePath)).IsTrue();
    }

    // Record equality compares the problem lists by reference, so the results are compared field by field.
    private sealed class BulkRemovalResults : IEqualityComparer<BulkRemovalResult>
    {
        public static readonly BulkRemovalResults Same = new();
        public bool Equals(BulkRemovalResult? a, BulkRemovalResult? b)
            => a is not null && b is not null && (a.Removed, a.Bytes, a.Skipped, a.Companions) == (b.Removed, b.Bytes, b.Skipped, b.Companions) && a.Problems.SequenceEqual(b.Problems);
        public int GetHashCode(BulkRemovalResult r) => r.Removed;
    }

    [Test]
    public async Task A_Kept_Photo_Is_Judged_Unchanged_By_Its_Size_And_By_Its_Time_Where_One_Was_Recorded()
    {
        // No modified time on record for the photo being kept: its size alone says it is still the same file.
        var path = Path.Combine(_root.FullName, "IMG_1.jpg");
        await File.WriteAllTextAsync(path, "AAAA");
        var keeper = new Photo { SourcePath = path, FileName = "IMG_1.jpg", FileSizeBytes = 4, ScanRoot = _root.FullName, ContentHash = "AAAA", Set = PhotoSet.Primary };
        await _repo.AddOrUpdateAsync(keeper);
        var copy = await AddAsync("IMG_1 (1).jpg", "AAAA");

        var result = await _service.RemoveDuplicatesAsync();

        await Assert.That((result.Removed, result.Skipped)).IsEqualTo((1, 0));
        await Assert.That(File.Exists(path)).IsTrue();
        await Assert.That(File.Exists(copy.SourcePath)).IsFalse();
    }

    [Test]
    public async Task A_Kept_Photo_With_No_Time_On_Record_Is_Judged_Changed_By_Its_Size()
    {
        var path = Path.Combine(_root.FullName, "IMG_1.jpg");
        await File.WriteAllTextAsync(path, "AAAA, and more since the scan");
        var keeper = new Photo { SourcePath = path, FileName = "IMG_1.jpg", FileSizeBytes = 4, ScanRoot = _root.FullName, ContentHash = "AAAA", Set = PhotoSet.Primary };
        await _repo.AddOrUpdateAsync(keeper);
        var copy = await AddAsync("IMG_1 (1).jpg", "AAAA");

        var result = await _service.RemoveDuplicatesAsync();

        await Assert.That((result.Removed, result.Skipped)).IsEqualTo((0, 1));
        await Assert.That(File.Exists(copy.SourcePath)).IsTrue();
    }

    [Test]
    public async Task Leaves_Copies_Alone_When_The_Photo_Being_Kept_Was_Saved_Again_At_The_Same_Size()
    {
        var keeper = await AddAsync("IMG_1.jpg", "AAAA");
        var copy = await AddAsync("IMG_1 (1).jpg", "AAAA");
        await File.WriteAllTextAsync(keeper.SourcePath, "BBBB");
        File.SetLastWriteTimeUtc(keeper.SourcePath, keeper.FileModifiedUtc!.Value.AddMinutes(5));

        var result = await _service.RemoveDuplicatesAsync();

        await Assert.That((result.Removed, result.Skipped)).IsEqualTo((0, 1));
        await Assert.That(File.Exists(copy.SourcePath)).IsTrue();
        await Assert.That(result.Problems.Single()).StartsWith("Kept photo is missing or has changed");
    }
}
