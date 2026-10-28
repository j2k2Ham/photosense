using LiteDB;
using Moq;
using PhotoSense.Application.Changes;
using PhotoSense.Application.Organizing;
using PhotoSense.Application.Removal;
using PhotoSense.Application.Scanning;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Repositories;
using PhotoSense.Infrastructure.Deletion;
using PhotoSense.Infrastructure.Hashing;
using PhotoSense.Desktop.Imaging;
using PhotoSense.Infrastructure.Metadata;
using PhotoSense.Infrastructure.Persistence;

namespace PhotoSense.Tests.Application;

/// <summary>
/// Clean up and Organize from start to finish on photos that are on no disk at all, as a phone's library will
/// be: every file is listed, read, moved, put back and erased through where the photos live, and only there.
/// </summary>
public sealed class PhotosNotOnDiskTests : IDisposable
{
    private const string LiveId = "LIVE-PHOTO-1";
    // A folder no disk has: a file read or written anywhere but in memory would be missed, or left behind.
    private readonly string _root = Path.Combine(Path.GetTempPath(), "photosense-not-on-disk-" + Guid.NewGuid().ToString("N"));
    private readonly InMemoryFileSystem _files = new();
    private readonly InMemoryPhotoRepository _repo = new();
    private readonly LiteDatabase _db = new(new MemoryStream());
    private readonly LiteDbOrganizeBatchStore _batches;
    private readonly FileChanges _changes;
    private readonly ScanExecutionService _scan;
    private readonly DuplicateRemovalService _cleanUp;
    private readonly OrganizeLibrary _library;
    private readonly OrganizeMover _organize;
    private readonly RemovedFiles _removed;

    public PhotosNotOnDiskTests()
    {
        var audit = Mock.Of<IAuditRepository>();
        var hashing = new Sha256ImageHashingService();
        _batches = new LiteDbOrganizeBatchStore(_db);
        _changes = new FileChanges(_batches, _repo, audit, _files);
        _scan = new ScanExecutionService(_repo, hashing, new MagickImageAnalyzer(), new BasicExifMetadataExtractor(), new InMemoryThumbnailStore(), new InMemoryScanProgressStore(), _files);
        _cleanUp = new DuplicateRemovalService(new DuplicateAnalysisService(_repo), _repo, new CompanionFileFinder(_files), _changes, _files);
        _library = new OrganizeLibrary(_repo, new ExifMediaDetailsReader(_files), hashing, new LiteDbMediaDetailsStore(_db), _files);
        var companions = new CompanionFileFinder(_files, _library.LivePhotoIds(new LivePhotoIdCache(_files).Read));
        _organize = new OrganizeMover(_library, companions, _changes, audit, _files);
        _removed = new RemovedFiles(_repo, _batches, audit, _files);
    }

    public void Dispose() => _db.Dispose();

    private string In(params string[] parts) => Path.Combine([_root, .. parts]);

    private List<string> Everything() => _files.AllFiles().Select(f => Path.GetRelativePath(_root, f)).ToList();

    private async Task<string> IdAsync(string path) => (await _library.DescribeAsync(path))!.Id;

    [Test]
    public async Task Scans_Cleans_Up_Organizes_Puts_Back_And_Erases_Without_Ever_Touching_A_Disk()
    {
        // A Live Photo and its video, and a copy of both in another album.
        var picture = MediaSamples.Picture(LiveId);
        var video = MediaSamples.Movie(LiveId);
        _files.Put(In("IMG_1.JPG"), picture);
        _files.Put(In("IMG_1.MOV"), video);
        _files.Put(In("Copies", "IMG_1.JPG"), picture);
        _files.Put(In("Copies", "IMG_1.MOV"), video);
        var asFound = Everything();

        // Clean up: the scan finds the copies, and one of each pair goes with the video that belongs to it.
        var scanned = await _scan.RunAsync(new ScanRequest(_root, null, Recursive: true), "scan");
        await Assert.That((scanned.Total, scanned.Analyzed, scanned.Unreadable)).IsEqualTo((4, 4, 0));
        var cleaned = await _cleanUp.RemoveDuplicatesAsync();
        await Assert.That((cleaned.Removed, cleaned.Companions, cleaned.Skipped)).IsEqualTo((1, 1, 0));
        var held = Everything().Where(f => f.StartsWith(PhotoStorageOptions.RemovedFolderName, StringComparison.Ordinal)).ToList();
        await Assert.That(held.Select(f => Path.GetExtension(f))).IsEquivalentTo([".JPG", ".MOV"], CollectionOrdering.Any);
        await Assert.That(held.Select(f => Path.GetFileNameWithoutExtension(f)).Distinct()).HasSingleItem();

        // Undone, every file is back where it was, and so is what the scan recorded.
        await Assert.That((await _changes.UndoAsync(cleaned.BatchId!.Value)).Done).IsTrue();
        await Assert.That(Everything()).IsEquivalentTo(asFound, CollectionOrdering.Matching);
        await Assert.That((await _repo.GetAllAsync()).Count).IsEqualTo(4);

        // Organize: the picture goes into a folder for the year it was taken, its video with it.
        var listed = await _library.ListAsync(_root);
        await Assert.That(listed.Count).IsEqualTo(4);
        var trip = new OrganizeDestination(_root, "Trips", direct: false, yearSplit: true, _files);
        var moved = await _organize.ApplyAsync(trip, copy: false, bringCompanions: true, "Trips", [(await IdAsync(In("IMG_1.JPG")), null)]);
        await Assert.That((moved.Done, moved.Companions)).IsEqualTo((1, 1));
        await Assert.That((_files.FileExists(In("Trips", "2024", "IMG_1.JPG")), _files.FileExists(In("Trips", "2024", "IMG_1.MOV")), _files.FileExists(In("IMG_1.JPG")))).IsEqualTo((true, true, false));
        await Assert.That(await _repo.GetByPathAsync(In("Trips", "2024", "IMG_1.JPG"))).IsNotNull();

        var back = await _organize.UndoAsync(moved.BatchId!.Value);
        await Assert.That((back.Restored, back.Skipped)).IsEqualTo((1, 0));
        await Assert.That(Everything()).IsEquivalentTo(asFound, CollectionOrdering.Matching);
        await Assert.That(_files.FolderExists(In("Trips"))).IsFalse();

        // Removed in Organize, the copy waits with its video to be erased for good.
        var removal = await _organize.RemoveAsync(_root, [await IdAsync(In("Copies", "IMG_1.JPG"))]);
        await Assert.That((removal.Done, removal.Companions)).IsEqualTo((1, 1));
        var waiting = (await _removed.FindAsync([_root])).Single();
        await Assert.That((waiting.Path, waiting.Files, waiting.Bytes)).IsEqualTo((In(PhotoStorageOptions.RemovedFolderName), 2, (long)picture.Length + video.Length));

        var erased = await _removed.EraseAsync([waiting.Path]);
        await Assert.That((erased.Erased, erased.Skipped)).IsEqualTo((2, 0));
        await Assert.That(Everything()).IsEquivalentTo(["IMG_1.JPG", "IMG_1.MOV"], CollectionOrdering.Matching);
        await Assert.That(_files.FolderExists(In(PhotoStorageOptions.RemovedFolderName))).IsFalse();

        // And not one of the files was ever on a disk.
        await Assert.That(Directory.Exists(_root)).IsFalse();
    }
}
