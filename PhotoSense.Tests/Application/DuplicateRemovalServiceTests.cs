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

namespace PhotoSense.Tests.Application;

/// <summary>Removing copies and look-alikes as one change, never the last copy of a picture.</summary>
public sealed class DuplicateRemovalServiceTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("photosense-removal-");
    private readonly InMemoryPhotoRepository _repo = new();
    private readonly FailingDisk _disk = new();
    private readonly FileChanges _changes;
    private readonly DuplicateRemovalService _service;

    public DuplicateRemovalServiceTests()
    {
        _changes = new FileChanges(new MemoryBatchStore(), _repo, Mock.Of<IAuditRepository>(), _disk);
        _service = Service(new CompanionFileFinder(_disk, _ => null));
    }

    public void Dispose()
    {
        _root.Delete(true);
    }

    private DuplicateRemovalService Service(ICompanionFileFinder finder) => new(new DuplicateAnalysisService(_repo), _repo, finder, _changes, _disk, new TestClock(TestClock.Day, TimeSpan.FromSeconds(1)));

    // A real file with a record that matches it, as a scan would leave it.
    private async Task<Photo> AddAsync(string relativePath, string contentHash, CancellationToken ct, bool kept = false, PhotoSet set = PhotoSet.Primary)
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
        await _repo.AddOrUpdateAsync(photo, ct);
        return photo;
    }

    private string Held(params string[] parts) => Path.Combine([_root.FullName, PhotoStorageOptions.RemovedFolderName, .. parts]);

    [Test]
    public async Task Moves_Every_Duplicate_Aside_And_Leaves_The_Best_Copy(CancellationToken ct)
    {
        var keeper = await AddAsync("IMG_1.jpg", "AAAA", ct);
        var copy = await AddAsync(Path.Combine("backup", "IMG_1 (1).jpg"), "AAAA", ct);
        var unique = await AddAsync("IMG_2.jpg", "BBBB", ct);

        var result = await _service.RemoveDuplicatesAsync(null, false, ct);

        await Assert.That(result.Removed).IsEqualTo(1);
        await Assert.That(result.Bytes).IsEqualTo(copy.FileSizeBytes);
        await Assert.That(result.Skipped).IsEqualTo(0);
        await Assert.That(result.Problems).IsEmpty();
        await Assert.That(File.Exists(keeper.SourcePath)).IsTrue();
        await Assert.That(File.Exists(unique.SourcePath)).IsTrue();
        await Assert.That(File.Exists(copy.SourcePath)).IsFalse();
        await Assert.That(File.Exists(Held(Path.Combine("backup", "IMG_1 (1).jpg")))).IsTrue();
        await Assert.That(await _repo.GetAsync(copy.Id, ct)).IsNull();
        await Assert.That(await _repo.GetAsync(keeper.Id, ct)).IsNotNull();
    }

    [Test]
    public async Task Leaves_Copies_Alone_When_The_Photo_Being_Kept_Is_Gone(CancellationToken ct)
    {
        var keeper = await AddAsync("IMG_1.jpg", "AAAA", ct);
        var copy = await AddAsync("IMG_1 (1).jpg", "AAAA", ct);
        File.Delete(keeper.SourcePath);

        var result = await _service.RemoveDuplicatesAsync(null, false, ct);

        await Assert.That(result.Removed).IsEqualTo(0);
        await Assert.That(result.Skipped).IsEqualTo(1);
        await Assert.That(result.Problems.Single()).Contains(keeper.SourcePath);
        await Assert.That(File.Exists(copy.SourcePath)).IsTrue();
    }

    [Test]
    public async Task Leaves_Copies_Alone_When_The_Photo_Being_Kept_Has_Changed(CancellationToken ct)
    {
        var keeper = await AddAsync("IMG_1.jpg", "AAAA", ct);
        var copy = await AddAsync("IMG_1 (1).jpg", "AAAA", ct);
        await File.WriteAllTextAsync(keeper.SourcePath, "edited since the scan", ct);

        var result = await _service.RemoveDuplicatesAsync(null, false, ct);

        await Assert.That(result.Removed).IsEqualTo(0);
        await Assert.That(File.Exists(copy.SourcePath)).IsTrue();
    }

    [Test]
    public async Task Skips_A_Copy_That_Changed_Since_The_Scan(CancellationToken ct)
    {
        await AddAsync("IMG_1.jpg", "AAAA", ct);
        var copy = await AddAsync("IMG_1 (1).jpg", "AAAA", ct);
        await File.WriteAllTextAsync(copy.SourcePath, "no longer the same file", ct);

        var result = await _service.RemoveDuplicatesAsync(null, false, ct);

        await Assert.That(result.Removed).IsEqualTo(0);
        await Assert.That(result.Skipped).IsEqualTo(1);
        await Assert.That(result.Problems.Single()).StartsWith("Changed since the scan");
        await Assert.That(File.Exists(copy.SourcePath)).IsTrue();
        await Assert.That(await _repo.GetAsync(copy.Id, ct)).IsNotNull();
    }

    [Test]
    public async Task Never_Takes_A_Copy_Marked_Keep(CancellationToken ct)
    {
        await AddAsync("IMG_1.jpg", "AAAA", ct);
        var kept = await AddAsync("IMG_1 (1).jpg", "AAAA", ct, kept: true);

        var result = await _service.RemoveDuplicatesAsync(null, false, ct);

        await Assert.That(result.Removed).IsEqualTo(0);
        await Assert.That(result.Skipped).IsEqualTo(0);
        await Assert.That(File.Exists(kept.SourcePath)).IsTrue();
    }

    [Test]
    public async Task Can_Be_Limited_To_One_Group(CancellationToken ct)
    {
        var keeperA = await AddAsync("a.jpg", "AAAA", ct);
        var copyA = await AddAsync("a (1).jpg", "AAAA", ct);
        await AddAsync("b.jpg", "BBBB", ct);
        var copyB = await AddAsync("b (1).jpg", "BBBB", ct);

        var result = await _service.RemoveDuplicatesAsync(keeperA.Id.ToString(), false, ct);

        await Assert.That(result.Removed).IsEqualTo(1);
        await Assert.That(File.Exists(copyA.SourcePath)).IsFalse();
        await Assert.That(File.Exists(copyB.SourcePath)).IsTrue();
        await Assert.That((await _service.RemoveDuplicatesAsync("no-such-group", false, ct)).Removed).IsEqualTo(0);
    }

    [Test]
    public async Task Can_Take_The_Groups_Named_Together_And_No_Others(CancellationToken ct)
    {
        var keeperA = await AddAsync("a.jpg", "AAAA", ct);
        var copyA = await AddAsync("a (1).jpg", "AAAA", ct);
        var keeperB = await AddAsync("b.jpg", "BBBB", ct);
        var copyB = await AddAsync("b (1).jpg", "BBBB", ct);
        await AddAsync("c.jpg", "CCCC", ct);
        var copyC = await AddAsync("c (1).jpg", "CCCC", ct);

        // Named as groups of similar shots they are no groups at all, and neither is a name that was never a group's.
        await Assert.That((await _service.RemoveGroupsAsync([keeperA.Id.ToString(), keeperB.Id.ToString()], similar: true, ct)).Removed).IsEqualTo(0);
        await Assert.That((await _service.RemoveGroupsAsync([], false, ct)).Removed).IsEqualTo(0);

        var result = await _service.RemoveGroupsAsync([keeperA.Id.ToString(), "no-such-group", keeperB.Id.ToString()], false, ct);

        await Assert.That((result.Removed, result.Skipped)).IsEqualTo((2, 0));
        await Assert.That((File.Exists(copyA.SourcePath), File.Exists(copyB.SourcePath), File.Exists(copyC.SourcePath))).IsEqualTo((false, false, true));
        await Assert.That((File.Exists(keeperA.SourcePath), File.Exists(keeperB.SourcePath))).IsEqualTo((true, true));
    }

    [Test]
    public async Task The_Similar_Shots_Of_Several_Groups_Go_Together_When_Each_Is_Named(CancellationToken ct)
    {
        // Three bursts of three different views.
        var bursts = new List<Photo[]>();
        foreach (var (name, difference, at) in new[] { ("A", 0d, 0), ("B", 5d, 3600), ("C", 10d, 7200) })
            bursts.Add([await AddShotAsync($"{name}1.JPG", TestPhotos.Signature(difference), at, ct), await AddShotAsync($"{name}2.JPG", TestPhotos.Signature(difference), at + 2, ct)]);
        var groups = (await new DuplicateAnalysisService(_repo).GetAsync(ct)).Similar;
        await Assert.That(groups.Count).IsEqualTo(3);
        string KeyOf(int burst) => groups.Single(g => g.Keeper.FileName.StartsWith(bursts[burst][0].FileName[..1], StringComparison.Ordinal)).Key;

        var result = await _service.RemoveGroupsAsync([KeyOf(0), KeyOf(2)], similar: true, ct);

        await Assert.That((result.Removed, result.Skipped)).IsEqualTo((2, 0));
        // One shot of each of the two bursts named is left, and both of the one that was not.
        await Assert.That(bursts.Select(b => b.Count(p => File.Exists(p.SourcePath)))).IsEquivalentTo(new[] { 1, 2, 1 }, CollectionOrdering.Matching);
    }

    // ---- similar shots, a group at a time

    // A picture as a scan records it, with what similar shots are told by: a hash, a signature and a capture time.
    private async Task<Photo> AddShotAsync(string name, byte[] signature, int secondsLater, CancellationToken ct, bool kept = false)
    {
        var path = Path.Combine(_root.FullName, name);
        await File.WriteAllTextAsync(path, name);
        var info = new FileInfo(path);
        var shot = TestPhotos.Make(name, folder: _root.FullName, signature: signature, taken: TestPhotos.Shot.AddSeconds(secondsLater), format: "JPEG", kept: kept);
        var photo = new Photo
        {
            SourcePath = path, FileName = name, FileSizeBytes = info.Length, FileModifiedUtc = info.LastWriteTimeUtc, ScanRoot = _root.FullName,
            ContentHash = name, PerceptualHash = shot.PerceptualHash, Signature = shot.Signature, Width = shot.Width, Height = shot.Height,
            Format = shot.Format, TakenOn = shot.TakenOn, Set = PhotoSet.Primary, IsKept = kept
        };
        await _repo.AddOrUpdateAsync(photo, ct);
        return photo;
    }

    [Test]
    public async Task The_Similar_Shots_Of_One_Group_Go_Together_When_Asked_For_By_Name_And_The_Best_Stays(CancellationToken ct)
    {
        // A burst: the same view seconds apart, so each is a shot of its own and none a copy.
        var best = await AddShotAsync("IMG_5490.JPG", TestPhotos.Signature(), 0, ct);
        var second = await AddShotAsync("IMG_5491.JPG", TestPhotos.Signature(), 2, ct);
        var third = await AddShotAsync("IMG_5492.JPG", TestPhotos.Signature(), 4, ct);
        var keeping = await AddShotAsync("IMG_5493.JPG", TestPhotos.Signature(), 6, ct, kept: true);
        var analysis = await new DuplicateAnalysisService(_repo).GetAsync(ct);
        await Assert.That(analysis.Duplicates).IsEmpty();
        var group = analysis.Similar.Single();
        await Assert.That(group.Members.Count).IsEqualTo(3);

        // Removing duplicates never takes similar shots, for this group or for all of them.
        await Assert.That((await _service.RemoveDuplicatesAsync(group.Key, false, ct)).Removed).IsEqualTo(0);
        await Assert.That((await _service.RemoveDuplicatesAsync(null, false, ct)).Removed).IsEqualTo(0);
        // And similar shots do not go without a group being named.
        await Assert.That((await _service.RemoveDuplicatesAsync(null, true, ct)).Removed).IsEqualTo(0);

        var result = await _service.RemoveDuplicatesAsync(group.Key, similar: true, ct);

        await Assert.That((result.Removed, result.Skipped)).IsEqualTo((2, 0));
        var left = new[] { best, second, third, keeping }.Where(p => File.Exists(p.SourcePath)).Select(p => p.FileName).ToList();
        await Assert.That(left).Contains(group.Keeper.FileName);
        await Assert.That(left).Contains("IMG_5493.JPG");
        await Assert.That(left.Count).IsEqualTo(2);
    }

    [Test]
    public async Task When_One_Copy_Cannot_Be_Moved_None_Is_And_Every_File_Stays_Where_It_Was(CancellationToken ct)
    {
        await AddAsync("a.jpg", "AAAA", ct);
        var copyA = await AddAsync("a (1).jpg", "AAAA", ct);
        await AddAsync("b.jpg", "BBBB", ct);
        var copyB = await AddAsync("b (1).jpg", "BBBB", ct);
        _disk.Fail("move", copyB.SourcePath);

        var result = await _service.RemoveDuplicatesAsync(null, false, ct);

        await Assert.That((result.Removed, result.Bytes, result.Skipped, result.BatchId)).IsEqualTo((0, 0L, 2, (Guid?)null));
        await Assert.That(result.Problems.Single()).IsEqualTo("b (1).jpg could not be moved: The disk would not move b (1).jpg. Nothing was removed: every file is where it was.");
        await Assert.That(File.Exists(copyA.SourcePath) && File.Exists(copyB.SourcePath)).IsTrue();
        await Assert.That(await _repo.GetAsync(copyA.Id, ct)).IsNotNull();
    }

    [Test]
    public async Task A_Removal_Can_Be_Undone_And_The_Copies_Are_Back_In_Their_Group(CancellationToken ct)
    {
        var keeper = await AddAsync("IMG_1.jpg", "AAAA", ct);
        var copy = await AddAsync(Path.Combine("backup", "IMG_1 (1).jpg"), "AAAA", ct);

        var result = await _service.RemoveDuplicatesAsync(null, false, ct);
        await Assert.That((await new DuplicateAnalysisService(_repo).GetAsync(ct)).Duplicates).IsEmpty();
        var undone = await _changes.UndoAsync(result.BatchId!.Value, ct);

        await Assert.That((undone.Done, undone.Back.Count)).IsEqualTo((true, 1));
        await Assert.That(File.Exists(copy.SourcePath)).IsTrue();
        await Assert.That(Directory.Exists(Held())).IsFalse();
        var group = (await new DuplicateAnalysisService(_repo).GetAsync(ct)).Duplicates.Single();
        await Assert.That((group.Keeper.Id, group.Members.Single().Photo.Id)).IsEqualTo((keeper.Id, copy.Id));
    }

    [Test]
    public async Task A_Copy_Whose_File_Has_Already_Gone_Is_Forgotten_Along_With_The_Rest(CancellationToken ct)
    {
        await AddAsync("a.jpg", "AAAA", ct);
        var gone = await AddAsync("a (1).jpg", "AAAA", ct);
        var copy = await AddAsync("a (2).jpg", "AAAA", ct);
        File.Delete(gone.SourcePath);

        var result = await _service.RemoveDuplicatesAsync(null, false, ct);

        await Assert.That((result.Removed, result.Bytes, result.Skipped)).IsEqualTo((2, gone.FileSizeBytes + copy.FileSizeBytes, 0));
        await Assert.That(result.BatchId).IsNotNull();
        await Assert.That(await _repo.GetAsync(gone.Id, ct)).IsNull();
        await Assert.That(File.Exists(Held("a (2).jpg"))).IsTrue();
    }

    [Test]
    public async Task Forgetting_Copies_Whose_Files_Have_Gone_Leaves_Nothing_To_Undo(CancellationToken ct)
    {
        await AddAsync("a.jpg", "AAAA", ct);
        var gone = await AddAsync("a (1).jpg", "AAAA", ct);
        File.Delete(gone.SourcePath);

        var result = await _service.RemoveDuplicatesAsync(null, false, ct);

        await Assert.That((result.Removed, result.BatchId)).IsEqualTo((1, (Guid?)null));
        await Assert.That(await _repo.GetAsync(gone.Id, ct)).IsNull();
    }

    [Test]
    public async Task A_Copy_That_Is_The_Kept_Photo_Under_Another_Path_Is_Put_Back_And_The_Rest_Still_Go(CancellationToken ct)
    {
        // One file, reached through its folder and through a link to that folder, recorded under both paths.
        var original = await AddAsync(Path.Combine("photos", "IMG_1.jpg"), "AAAA", ct);
        using var link = TestFiles.LinkFolder(Path.Combine(_root.FullName, "alias"), Path.Combine(_root.FullName, "photos"));
        await _repo.AddOrUpdateAsync(new Photo
        {
            SourcePath = Path.Combine(_root.FullName, "alias", "IMG_1.jpg"), FileName = "IMG_1.jpg", FileSizeBytes = original.FileSizeBytes,
            FileModifiedUtc = original.FileModifiedUtc, ScanRoot = _root.FullName, ContentHash = "AAAA", Set = PhotoSet.Primary
        }, ct);
        await AddAsync("b.jpg", "BBBB", ct);
        var copyB = await AddAsync("b (1).jpg", "BBBB", ct);

        var result = await _service.RemoveDuplicatesAsync(null, false, ct);

        await Assert.That((result.Removed, result.Skipped)).IsEqualTo((1, 1));
        await Assert.That(result.Problems.Single()).StartsWith("The same file as the photo kept, under another path, so it was left alone: ");
        await Assert.That(await File.ReadAllTextAsync(original.SourcePath, ct)).IsEqualTo("AAAA");
        await Assert.That(File.Exists(copyB.SourcePath)).IsFalse();
    }

    // ---- one photo at a time

    [Test]
    public async Task A_Copy_Removed_On_Its_Own_Goes_And_The_Photo_Kept_Stays(CancellationToken ct)
    {
        var keeper = await AddAsync("IMG_0377.JPG", "AAAA", ct);
        var copy = await AddAsync("IMG_0648.JPG", "AAAA", ct);

        var result = await _service.RemoveAsync(copy.Id, deleteFile: true, ct);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.HeldAt).IsEqualTo(Held("IMG_0648.JPG"));
        await Assert.That(File.Exists(keeper.SourcePath)).IsTrue();
        await Assert.That(File.Exists(copy.SourcePath)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task A_Copy_Is_Never_Removed_Once_The_Photo_It_Copies_Is_Gone_Or_Changed(bool changed, CancellationToken ct)
    {
        var keeper = await AddAsync("IMG_0377.JPG", "AAAA", ct);
        var copy = await AddAsync("IMG_0648.JPG", "AAAA", ct);
        var other = await AddAsync("IMG_0648 (1).JPG", "AAAA", ct);
        // Done outside PhotoSense, after the scan.
        if (changed) await File.WriteAllTextAsync(keeper.SourcePath, "edited", ct); else File.Delete(keeper.SourcePath);

        var result = await _service.RemoveAsync(copy.Id, deleteFile: true, ct);

        // Another copy is still there, but it is the photo kept that each copy was checked against.
        await Assert.That(result.Outcome).IsEqualTo(RemovalOutcome.LastCopy);
        await Assert.That(File.Exists(copy.SourcePath) && File.Exists(other.SourcePath)).IsTrue();
        await Assert.That(await _repo.GetAsync(copy.Id, ct)).IsNotNull();
    }

    [Test]
    public async Task The_Photo_Kept_Can_Be_Removed_Instead_While_One_Of_Its_Copies_Stays(CancellationToken ct)
    {
        var keeper = await AddAsync("IMG_1.jpg", "AAAA", ct);
        var gone = await AddAsync("IMG_1 (1).jpg", "AAAA", ct);
        var copy = await AddAsync("IMG_1 (2).jpg", "AAAA", ct);
        File.Delete(gone.SourcePath);

        await Assert.That((await _service.RemoveAsync(keeper.Id, deleteFile: true, ct)).Succeeded).IsTrue();

        await Assert.That(File.Exists(keeper.SourcePath)).IsFalse();
        await Assert.That(File.Exists(copy.SourcePath)).IsTrue();
    }

    [Test]
    public async Task The_Photo_Kept_Is_Never_Removed_Once_Its_Copies_Are_Gone(CancellationToken ct)
    {
        var keeper = await AddAsync("IMG_1.jpg", "AAAA", ct);
        var copy = await AddAsync("IMG_1 (1).jpg", "AAAA", ct);
        File.Delete(copy.SourcePath);

        await Assert.That((await _service.RemoveAsync(keeper.Id, deleteFile: true, ct)).Outcome).IsEqualTo(RemovalOutcome.LastCopy);
        await Assert.That(File.Exists(keeper.SourcePath)).IsTrue();
    }

    [Test]
    public async Task A_Photo_That_Is_Nobodys_Copy_Is_Removed_When_Asked_And_An_Unknown_One_Is_Reported(CancellationToken ct)
    {
        await AddAsync("IMG_1.jpg", "AAAA", ct);
        await AddAsync("IMG_1 (1).jpg", "AAAA", ct);
        var unique = await AddAsync("IMG_2.jpg", "BBBB", ct);

        await Assert.That((await _service.RemoveAsync(unique.Id, deleteFile: true, ct)).Succeeded).IsTrue();
        await Assert.That(File.Exists(unique.SourcePath)).IsFalse();
        await Assert.That((await _service.RemoveAsync(unique.Id, deleteFile: true, ct)).Outcome).IsEqualTo(RemovalOutcome.NotFound);
    }

    [Test]
    public async Task Forgetting_A_Copy_Leaves_Its_File_Whatever_Became_Of_The_Photo_Kept(CancellationToken ct)
    {
        var keeper = await AddAsync("IMG_1.jpg", "AAAA", ct);
        var copy = await AddAsync("IMG_1 (1).jpg", "AAAA", ct);
        File.Delete(keeper.SourcePath);

        await Assert.That((await _service.RemoveAsync(copy.Id, deleteFile: false, ct)).Succeeded).IsTrue();
        await Assert.That(File.Exists(copy.SourcePath)).IsTrue();
        await Assert.That(await _repo.GetAsync(copy.Id, ct)).IsNull();
    }

    [Test]
    public async Task A_Picture_Takes_Its_Live_Photo_Video_Along_And_The_Kept_Copy_Keeps_Its_Own(CancellationToken ct)
    {
        // Records made before Live Photo identifiers were stored, so the videos appear as a group of their own.
        var service = Service(new CompanionFileFinder(_disk, _ => "LIVE-1"));
        var keptPicture = await AddAsync(Path.Combine("a", "IMG_1.JPG"), "PPPP", ct);
        var keptVideo = await AddAsync(Path.Combine("a", "IMG_1.MOV"), "VVVV", ct);
        var copyPicture = await AddAsync(Path.Combine("b", "IMG_1.JPG"), "PPPP", ct);
        var copyVideo = await AddAsync(Path.Combine("b", "IMG_1.MOV"), "VVVV", ct);

        var result = await service.RemoveDuplicatesAsync(null, false, ct);

        await Assert.That((result.Removed, result.Companions, result.Skipped)).IsEqualTo((1, 1, 0));
        await Assert.That(result.Problems).IsEmpty(); // the video had already gone with its picture: nothing to report
        await Assert.That(File.Exists(keptPicture.SourcePath) && File.Exists(keptVideo.SourcePath)).IsTrue();
        await Assert.That(File.Exists(copyPicture.SourcePath) || File.Exists(copyVideo.SourcePath)).IsFalse();
        await Assert.That(File.Exists(Held(Path.Combine("b", "IMG_1.MOV")))).IsTrue();
        await Assert.That((await _repo.GetAllAsync(ct)).Count).IsEqualTo(2);
    }

    [Test]
    public async Task The_Last_Copy_Of_A_Video_Is_Never_Removed_As_Somebody_Elses_Duplicate(CancellationToken ct)
    {
        // The picture kept is in b, but of the two identical videos the one in a ranks first.
        var service = Service(new CompanionFileFinder(_disk, _ => "LIVE-1"));
        var pictureA = await AddAsync(Path.Combine("a", "IMG_1.JPG"), "PPPP", ct, set: PhotoSet.Secondary);
        var videoA = await AddAsync(Path.Combine("a", "IMG_1.MOV"), "VVVV", ct);
        var pictureB = await AddAsync(Path.Combine("b", "IMG_1.JPG"), "PPPP", ct);
        var videoB = await AddAsync(Path.Combine("b", "IMG_1.MOV"), "VVVV", ct, set: PhotoSet.Secondary);

        var result = await service.RemoveDuplicatesAsync(null, false, ct);

        // Pictures go first: a's picture leaves with its video. The video group then finds its keeper gone
        // and leaves b's video alone, so the Live Photo in b stays whole.
        await Assert.That((result.Removed, result.Companions, result.Skipped)).IsEqualTo((1, 1, 1));
        await Assert.That(File.Exists(pictureB.SourcePath) && File.Exists(videoB.SourcePath)).IsTrue();
        await Assert.That(File.Exists(pictureA.SourcePath) || File.Exists(videoA.SourcePath)).IsFalse();
    }

    [Test]
    public async Task A_Photo_Removed_On_Its_Own_Is_Held_Keeping_Its_Place_Under_The_Scanned_Folder(CancellationToken ct)
    {
        var photo = await AddAsync(Path.Combine("2024", "trip", "IMG_1.jpg"), "picture", ct);

        var result = await _service.RemoveAsync(photo.Id, deleteFile: true, ct);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.HeldAt).IsEqualTo(Held("2024", "trip", "IMG_1.jpg"));
        await Assert.That(result.BatchId).IsNotNull();
        await Assert.That(File.Exists(photo.SourcePath)).IsFalse();
        await Assert.That(await File.ReadAllTextAsync(result.HeldAt!, ct)).IsEqualTo("picture");
        await Assert.That(await _repo.GetAsync(photo.Id, ct)).IsNull();
    }

    [Test]
    public async Task A_File_Held_Under_A_Name_Already_Taken_Gets_A_Number(CancellationToken ct)
    {
        var first = await AddAsync("IMG_1.jpg", "first", ct);
        await Assert.That((await _service.RemoveAsync(first.Id, true, ct)).HeldAt).IsEqualTo(Held("IMG_1.jpg"));
        var second = await AddAsync("IMG_1.jpg", "second", ct);

        var held = (await _service.RemoveAsync(second.Id, true, ct)).HeldAt;

        await Assert.That(held).IsEqualTo(Held("IMG_1 (1).jpg"));
        await Assert.That(await File.ReadAllTextAsync(Held("IMG_1.jpg"), ct)).IsEqualTo("first");
        await Assert.That(await File.ReadAllTextAsync(held!, ct)).IsEqualTo("second");
    }

    [Test]
    [Arguments(null)]
    [Arguments("elsewhere")]
    public async Task A_File_Is_Held_Beside_Itself_When_Its_Scanned_Folder_Is_Unknown_Or_Not_Above_It(string? scanRoot, CancellationToken ct)
    {
        var photo = await AddAsync(Path.Combine("sub", "IMG_1.jpg"), "picture", ct);
        photo.ScanRoot = scanRoot is null ? null : Path.Combine(_root.FullName, scanRoot);
        await _repo.AddOrUpdateAsync(photo, ct);

        var result = await _service.RemoveAsync(photo.Id, true, ct);

        await Assert.That(result.HeldAt).IsEqualTo(Path.Combine(_root.FullName, "sub", PhotoStorageOptions.RemovedFolderName, "IMG_1.jpg"));
    }

    [Test]
    public async Task A_Photo_That_Changed_Since_It_Was_Scanned_Is_Left_With_Its_Record(CancellationToken ct)
    {
        var resized = await AddAsync("resized.jpg", "picture", ct);
        await File.WriteAllTextAsync(resized.SourcePath, "a different length now", ct);
        var touched = await AddAsync("touched.jpg", "picture", ct);
        File.SetLastWriteTimeUtc(touched.SourcePath, touched.FileModifiedUtc!.Value.AddMinutes(5));

        await Assert.That((await _service.RemoveAsync(resized.Id, true, ct)).Outcome).IsEqualTo(RemovalOutcome.Changed);
        await Assert.That((await _service.RemoveAsync(touched.Id, true, ct)).Outcome).IsEqualTo(RemovalOutcome.Changed);
        await Assert.That(File.Exists(resized.SourcePath) && File.Exists(touched.SourcePath)).IsTrue();
        await Assert.That(await _repo.GetAsync(resized.Id, ct)).IsNotNull();
    }

    [Test]
    public async Task A_Photo_That_Cannot_Be_Moved_Keeps_Its_Record_And_Says_Why(CancellationToken ct)
    {
        var photo = await AddAsync("IMG_1.jpg", "picture", ct);
        _disk.Fail("move", photo.SourcePath);

        var result = await _service.RemoveAsync(photo.Id, true, ct);

        await Assert.That((result.Outcome, result.Error)).IsEqualTo((RemovalOutcome.Failed, (string?)"IMG_1.jpg could not be moved: The disk would not move IMG_1.jpg. Nothing was removed: every file is where it was."));
        await Assert.That(File.Exists(photo.SourcePath)).IsTrue();
        await Assert.That(await _repo.GetAsync(photo.Id, ct)).IsNotNull();
    }

    [Test]
    public async Task A_File_Recorded_Under_Two_Paths_Is_Put_Back_Rather_Than_Removed_As_A_Copy_Of_Itself(CancellationToken ct)
    {
        // One file, reached through the folder itself and through a link to that folder.
        var original = await AddAsync(Path.Combine("photos", "IMG_1.jpg"), "picture", ct);
        var alias = Path.Combine(_root.FullName, "alias");
        using var link = TestFiles.LinkFolder(alias, Path.Combine(_root.FullName, "photos"));
        var same = new Photo
        {
            SourcePath = Path.Combine(alias, "IMG_1.jpg"), FileName = "IMG_1.jpg", FileSizeBytes = original.FileSizeBytes,
            FileModifiedUtc = original.FileModifiedUtc, ScanRoot = _root.FullName, ContentHash = original.ContentHash, Set = PhotoSet.Primary
        };
        await _repo.AddOrUpdateAsync(same, ct);

        var result = await _service.RemoveAsync(same.Id, true, ct);

        await Assert.That(result.Outcome).IsEqualTo(RemovalOutcome.LastCopy);
        await Assert.That(await File.ReadAllTextAsync(original.SourcePath, ct)).IsEqualTo("picture");
        await Assert.That(await _repo.GetAsync(same.Id, ct)).IsNotNull();
    }

    [Test]
    public async Task Forgetting_Without_Deleting_Leaves_The_File_Where_It_Is(CancellationToken ct)
    {
        var photo = await AddAsync("IMG_1.jpg", "picture", ct);

        var result = await _service.RemoveAsync(photo.Id, deleteFile: false, ct);

        await Assert.That((result.Succeeded, result.HeldAt, result.BatchId)).IsEqualTo((true, (string?)null, (Guid?)null));
        await Assert.That(File.Exists(photo.SourcePath)).IsTrue();
        await Assert.That(await _repo.GetAsync(photo.Id, ct)).IsNull();
    }

    [Test]
    public async Task A_Record_Whose_File_Is_Already_Gone_Is_Simply_Forgotten(CancellationToken ct)
    {
        var photo = await AddAsync("IMG_1.jpg", "picture", ct);
        File.Delete(photo.SourcePath);

        var result = await _service.RemoveAsync(photo.Id, true, ct);

        await Assert.That((result.Succeeded, result.HeldAt)).IsEqualTo((true, (string?)null));
        await Assert.That(await _repo.GetAsync(photo.Id, ct)).IsNull();
    }

    [Test]
    public async Task A_Record_Without_A_Modified_Time_Is_Checked_By_Size_Alone(CancellationToken ct)
    {
        var path = Path.Combine(_root.FullName, "IMG_7.jpg");
        await File.WriteAllTextAsync(path, "picture", ct);
        var photo = new Photo { SourcePath = path, FileName = "IMG_7.jpg", FileSizeBytes = new FileInfo(path).Length, ScanRoot = _root.FullName, ContentHash = "picture" };
        await _repo.AddOrUpdateAsync(photo, ct);

        await Assert.That((await _service.RemoveAsync(photo.Id, deleteFile: true, ct)).Succeeded).IsTrue();
        await Assert.That(File.Exists(Held("IMG_7.jpg"))).IsTrue();
    }

    [Test]
    public async Task Stops_When_Cancelled(CancellationToken ct)
    {
        await AddAsync("IMG_1.jpg", "AAAA", ct);
        var copy = await AddAsync("IMG_1 (1).jpg", "AAAA", ct);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => _service.RemoveDuplicatesAsync(null, false, cts.Token));
        await Assert.That(File.Exists(copy.SourcePath)).IsTrue();
    }

    // Record equality compares the problem lists by reference, so the results are compared field by field.
    private sealed class BulkRemovalResults : IEqualityComparer<BulkRemovalResult>
    {
        /// <summary>The one comparer.</summary>
        public static readonly BulkRemovalResults Same = new();
        /// <summary>Whether two results say the same.</summary>
        public bool Equals(BulkRemovalResult? a, BulkRemovalResult? b)
            => a is not null && b is not null && (a.Removed, a.Bytes, a.Skipped, a.Companions) == (b.Removed, b.Bytes, b.Skipped, b.Companions) && a.Problems.SequenceEqual(b.Problems);
        /// <summary>A hash that agrees with Equals.</summary>
        public int GetHashCode(BulkRemovalResult r) => r.Removed;
    }

    [Test]
    public async Task A_Kept_Photo_Is_Judged_Unchanged_By_Its_Size_And_By_Its_Time_Where_One_Was_Recorded(CancellationToken ct)
    {
        // No modified time on record for the photo being kept: its size alone says it is still the same file.
        var path = Path.Combine(_root.FullName, "IMG_1.jpg");
        await File.WriteAllTextAsync(path, "AAAA", ct);
        var keeper = new Photo { SourcePath = path, FileName = "IMG_1.jpg", FileSizeBytes = 4, ScanRoot = _root.FullName, ContentHash = "AAAA", Set = PhotoSet.Primary };
        await _repo.AddOrUpdateAsync(keeper, ct);
        var copy = await AddAsync("IMG_1 (1).jpg", "AAAA", ct);

        var result = await _service.RemoveDuplicatesAsync(null, false, ct);

        await Assert.That((result.Removed, result.Skipped)).IsEqualTo((1, 0));
        await Assert.That(File.Exists(path)).IsTrue();
        await Assert.That(File.Exists(copy.SourcePath)).IsFalse();
    }

    [Test]
    public async Task A_Kept_Photo_With_No_Time_On_Record_Is_Judged_Changed_By_Its_Size(CancellationToken ct)
    {
        var path = Path.Combine(_root.FullName, "IMG_1.jpg");
        await File.WriteAllTextAsync(path, "AAAA, and more since the scan", ct);
        var keeper = new Photo { SourcePath = path, FileName = "IMG_1.jpg", FileSizeBytes = 4, ScanRoot = _root.FullName, ContentHash = "AAAA", Set = PhotoSet.Primary };
        await _repo.AddOrUpdateAsync(keeper, ct);
        var copy = await AddAsync("IMG_1 (1).jpg", "AAAA", ct);

        var result = await _service.RemoveDuplicatesAsync(null, false, ct);

        await Assert.That((result.Removed, result.Skipped)).IsEqualTo((0, 1));
        await Assert.That(File.Exists(copy.SourcePath)).IsTrue();
    }

    [Test]
    public async Task Leaves_Copies_Alone_When_The_Photo_Being_Kept_Was_Saved_Again_At_The_Same_Size(CancellationToken ct)
    {
        var keeper = await AddAsync("IMG_1.jpg", "AAAA", ct);
        var copy = await AddAsync("IMG_1 (1).jpg", "AAAA", ct);
        await File.WriteAllTextAsync(keeper.SourcePath, "BBBB", ct);
        File.SetLastWriteTimeUtc(keeper.SourcePath, keeper.FileModifiedUtc!.Value.AddMinutes(5));

        var result = await _service.RemoveDuplicatesAsync(null, false, ct);

        await Assert.That((result.Removed, result.Skipped)).IsEqualTo((0, 1));
        await Assert.That(File.Exists(copy.SourcePath)).IsTrue();
        await Assert.That(result.Problems.Single()).StartsWith("Kept photo is missing or has changed");
    }
}
