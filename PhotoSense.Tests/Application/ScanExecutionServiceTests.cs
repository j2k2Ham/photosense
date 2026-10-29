using Moq;
using PhotoSense.Application.Scanning;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Hashing;
using PhotoSense.Infrastructure.Persistence;

namespace PhotoSense.Tests.Application;

public sealed class ScanExecutionServiceTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("photosense-scan-");
    private readonly InMemoryPhotoRepository _repo = new();
    private readonly FakeAnalyzer _analyzer = new();
    private readonly FakeThumbnails _thumbnails = new();
    private readonly InMemoryScanProgressStore _progress = new();
    private readonly Mock<IScanLogSink> _log = new();
    private readonly ScanExecutionService _service;

    public ScanExecutionServiceTests()
        => _service = new ScanExecutionService(_repo, new Sha256ImageHashingService(), _analyzer, Mock.Of<IPhotoMetadataExtractor>(), _thumbnails, _progress, _log.Object, parallelism: 2);

    public void Dispose() => _root.Delete(true);

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_root.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    // ---- how long scans take

    /// <summary>Scans kept in a list, newest first, as the real history hands them back.</summary>
    private sealed class History : IScanHistory
    {
        public List<ScanRecord> Scans { get; } = [];
        public Task AddAsync(ScanRecord scan, CancellationToken ct = default) { Scans.Insert(0, scan); return Task.CompletedTask; }
        public Task<IReadOnlyList<ScanRecord>> RecentAsync(int take = 10, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ScanRecord>>(Scans.Take(take).ToList());
    }

    /// <summary>A clock that moves on seven seconds each time it is read.</summary>
    private sealed class SteppingClock : TimeProvider
    {
        private long _seconds;
        public static readonly DateTimeOffset Day = new(2026, 10, 7, 20, 0, 0, TimeSpan.Zero);
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => _seconds += 7;
        public override DateTimeOffset GetUtcNow() => Day;
    }

    [Test]
    public async Task Keeps_What_Each_Scan_Took_And_Tells_The_Next_How_Long_To_Expect()
    {
        var history = new History();
        var service = new ScanExecutionService(_repo, new Sha256ImageHashingService(), _analyzer, Mock.Of<IPhotoMetadataExtractor>(), _thumbnails, _progress,
            parallelism: 2, history: history, time: new SteppingClock());
        Write("a.jpg", "picture a");

        // The first scan has nothing to go by, and leaves a record of itself.
        await service.RunAsync(new ScanRequest(_root.FullName, null, true), "first");
        await Assert.That(_progress.Get("first").ExpectedSeconds).IsNull();
        var first = history.Scans.Single();
        await Assert.That((first.StartedUtc, first.Seconds, first.Total, first.Read, first.Unchanged)).IsEqualTo((SteppingClock.Day.UtcDateTime, 7d, 1, 1, 0));

        // A scan long enough to tell: two seconds a file. The next has two files it has not seen before.
        history.Scans.Insert(0, new ScanRecord { Seconds = 200, Read = 100, Total = 100 });
        Write("b.jpg", "picture b");
        File.WriteAllBytes(Path.Combine(_root.FullName, "c.jpg"), []);   // an empty file: recorded, and nothing in it to analyse
        await service.RunAsync(new ScanRequest(_root.FullName, null, true), "second");

        await Assert.That(_progress.Get("second").ExpectedSeconds).IsEqualTo(4);
        var second = history.Scans[0];
        await Assert.That((second.Total, second.Read + second.Unchanged, second.Unchanged)).IsEqualTo((3, 3, 1));
    }

    [Test]
    public async Task A_Scan_That_Was_Not_Started_Leaves_No_Record_Of_Itself()
    {
        var history = new History();
        var service = new ScanExecutionService(_repo, new Sha256ImageHashingService(), _analyzer, Mock.Of<IPhotoMetadataExtractor>(), _thumbnails, _progress, history: history);
        await service.RunAsync(new ScanRequest(Path.Combine(_root.FullName, "not-there"), null, true), "missing");
        await Assert.That(history.Scans).IsEmpty();
    }

    private Task<ScanSummary> ScanAsync(string? secondary = null, bool recursive = true, string instance = "scan")
        => _service.RunAsync(new ScanRequest(_root.FullName, secondary, recursive), instance);

    [Test]
    public async Task Records_Each_Image_With_Its_Hashes_And_Measurements()
    {
        var path = Write("a.jpg", "picture a");
        Write("notes.txt", "not a picture");

        var summary = await ScanAsync();

        await Assert.That(summary).IsEqualTo(new ScanSummary(Total: 1, Analyzed: 1, Unchanged: 0, Unreadable: 0, Pruned: 0));
        var photo = (await _repo.GetAllAsync()).Single();
        await Assert.That(photo.SourcePath).IsEqualTo(path);
        await Assert.That(photo.FileName).IsEqualTo("a.jpg");
        await Assert.That(photo.FileSizeBytes).IsEqualTo(new FileInfo(path).Length);
        await Assert.That(photo.FileModifiedUtc).IsEqualTo(new FileInfo(path).LastWriteTimeUtc);
        await Assert.That(photo.ScanRoot).IsEqualTo(_root.FullName);
        await Assert.That(photo.Set).IsEqualTo(PhotoSet.Primary);
        await Assert.That(photo.ContentHash!.Length).IsEqualTo(64);
        await Assert.That(photo.PerceptualHash).IsEqualTo(FakeAnalyzer.HashOf("picture a").ToString("X16"));
        await Assert.That((photo.Width, photo.Height, photo.Format, photo.EncodedQuality)).IsEqualTo((640, 480, "JPEG", 90));
        await Assert.That(photo.Signature).IsNotNull();
        await Assert.That(_thumbnails.Saved.ContainsKey(photo.ContentHash)).IsTrue();
        var progress = _progress.Get("scan");
        await Assert.That((progress.PrimaryTotal, progress.PrimaryProcessed)).IsEqualTo((1, 1));
        await Assert.That(progress.CompletedUtc).IsNotNull();
    }

    [Test]
    public async Task Scanning_Again_Does_Not_Make_A_File_A_Duplicate_Of_Itself()
    {
        Write("a.jpg", "picture a");
        Write("b.jpg", "picture b");
        await ScanAsync();
        var firstIds = (await _repo.GetAllAsync()).Select(p => p.Id).OrderBy(i => i.Value).ToList();

        var second = await ScanAsync(instance: "again");

        await Assert.That(second).IsEqualTo(new ScanSummary(Total: 2, Analyzed: 0, Unchanged: 2, Unreadable: 0, Pruned: 0));
        await Assert.That((await _repo.GetAllAsync()).Select(p => p.Id).OrderBy(i => i.Value)).IsEquivalentTo(firstIds, CollectionOrdering.Matching);
        await Assert.That(_analyzer.Calls).IsEqualTo(2); // unchanged files are not decoded again
        await Assert.That(DuplicateAnalysisService.Analyze(await _repo.GetAllAsync()).Duplicates).IsEmpty();
    }

    [Test]
    public async Task A_Changed_File_Is_Read_Again_And_Keeps_Its_Record_And_Keep_Mark()
    {
        var path = Write("a.jpg", "picture a");
        await ScanAsync();
        var before = (await _repo.GetAllAsync()).Single();
        before.IsKept = true;
        await _repo.AddOrUpdateAsync(before);

        File.WriteAllText(path, "picture a, edited and longer");
        var summary = await ScanAsync(instance: "again");

        await Assert.That(summary.Analyzed).IsEqualTo(1);
        var after = (await _repo.GetAllAsync()).Single();
        await Assert.That(after.Id).IsEqualTo(before.Id);
        await Assert.That(after.ContentHash).IsNotEqualTo(before.ContentHash);
        await Assert.That(after.IsKept).IsTrue();
    }

    [Test]
    public async Task Forgets_Files_That_Are_No_Longer_There()
    {
        var gone = Write("gone.jpg", "picture");
        Write("stays.jpg", "another");
        await ScanAsync();

        File.Delete(gone);
        var summary = await ScanAsync(instance: "again");

        await Assert.That(summary.Pruned).IsEqualTo(1);
        await Assert.That((await _repo.GetAllAsync()).Single().FileName).IsEqualTo("stays.jpg");
    }

    [Test]
    public async Task A_Missing_Folder_Scans_Nothing_And_Forgets_Nothing()
    {
        Write("a.jpg", "picture a");
        await ScanAsync();

        var missing = Path.Combine(_root.FullName, "no-such-folder");
        var summary = await _service.RunAsync(new ScanRequest(missing, null, true), "typo");
        var blank = await _service.RunAsync(new ScanRequest(" ", null, true), "blank");
        var missingSecondary = await ScanAsync(secondary: missing, instance: "typo2");

        await Assert.That(summary).IsEqualTo(new ScanSummary(0, 0, 0, 0, 0));
        await Assert.That(blank).IsEqualTo(new ScanSummary(0, 0, 0, 0, 0));
        await Assert.That(missingSecondary).IsEqualTo(new ScanSummary(0, 0, 0, 0, 0));
        await Assert.That(await _repo.GetAllAsync()).HasSingleItem();
        await Assert.That(_progress.Get("typo").CompletedUtc).IsNotNull();
        _log.Verify(l => l.Log("typo", "Error", It.Is<string>(m => m.Contains(missing))), Times.Once);
    }

    [Test]
    public async Task Starting_Over_Forgets_Earlier_Results_And_Reads_Every_File_Again()
    {
        var path = Write("a.jpg", "picture a");
        Write("b.jpg", "picture b");
        await ScanAsync();
        var before = (await _repo.GetAllAsync()).Single(p => p.SourcePath == path);
        before.IsKept = true;
        await _repo.AddOrUpdateAsync(before);
        // Left over from a scan of some other folder: an ordinary scan would only drop it at its end.
        await _repo.AddOrUpdateAsync(new Photo { SourcePath = Path.Combine(Path.GetTempPath(), "elsewhere", "old.jpg"), FileName = "old.jpg", ContentHash = "OLD" });
        _thumbnails.Saved["STALE"] = [9];

        var summary = await _service.RunAsync(new ScanRequest(_root.FullName, null, true, StartOver: true), "fresh");

        // Nothing is taken as unchanged, and nothing is counted as having gone missing.
        await Assert.That(summary).IsEqualTo(new ScanSummary(Total: 2, Analyzed: 2, Unchanged: 0, Unreadable: 0, Pruned: 0));
        await Assert.That(_analyzer.Calls).IsEqualTo(4);
        var after = (await _repo.GetAllAsync()).Single(p => p.SourcePath == path);
        await Assert.That(after.Id).IsNotEqualTo(before.Id);
        await Assert.That(after.IsKept).IsFalse();                          // marks made on the earlier results go with them
        await Assert.That((await _repo.GetAllAsync()).Count).IsEqualTo(2);
        await Assert.That(_thumbnails.Saved.Keys).DoesNotContain("STALE");
        await Assert.That(_thumbnails.Saved.Count).IsEqualTo(2);
        _log.Verify(l => l.Log("fresh", "Info", "Starting over: forgot 3 files recorded by earlier scans"), Times.Once);
    }

    [Test]
    public async Task Starting_Over_With_A_Mistyped_Folder_Forgets_Nothing()
    {
        Write("a.jpg", "picture a");
        await ScanAsync();
        var quiet = new ScanExecutionService(_repo, new Sha256ImageHashingService(), _analyzer, Mock.Of<IPhotoMetadataExtractor>(), _thumbnails, _progress, parallelism: 1);

        var missing = Path.Combine(_root.FullName, "no-such-folder");
        await Assert.That(await quiet.RunAsync(new ScanRequest(missing, null, true, StartOver: true), "typo")).IsEqualTo(new ScanSummary(0, 0, 0, 0, 0));
        await Assert.That(await _repo.GetAllAsync()).HasSingleItem();
        await Assert.That(_thumbnails.Saved).HasSingleItem();

        // The same service, with nowhere to log, starts over all the same once the folder is right.
        var summary = await quiet.RunAsync(new ScanRequest(_root.FullName, null, true, StartOver: true), "fresh");
        await Assert.That((summary.Analyzed, summary.Unchanged)).IsEqualTo((1, 0));
    }

    [Test]
    public async Task A_File_That_Cannot_Be_Decoded_Is_Still_Recorded_By_Its_Content()
    {
        Write("broken.heic", FakeAnalyzer.Undecodable);

        var summary = await ScanAsync();

        await Assert.That((summary.Analyzed, summary.Unreadable)).IsEqualTo((0, 1));
        var photo = (await _repo.GetAllAsync()).Single();
        await Assert.That(photo.ContentHash).IsNotNull();
        await Assert.That(photo.PerceptualHash).IsNull();
        await Assert.That(photo.Width).IsEqualTo(0);
        _log.Verify(l => l.Log("scan", "Warn", It.Is<string>(m => m.Contains("broken.heic"))), Times.Once);

        // It is not attempted again while it stays unchanged.
        await Assert.That((await ScanAsync(instance: "again")).Unchanged).IsEqualTo(1);
        await Assert.That(_analyzer.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task A_File_That_Cannot_Be_Opened_Keeps_Its_Earlier_Record()
    {
        var path = Write("locked.jpg", "picture");
        await ScanAsync();
        File.WriteAllText(path, "changed, so it has to be read again");

        ScanSummary summary;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            summary = await ScanAsync(instance: "again");

        await Assert.That((summary.Unreadable, summary.Pruned)).IsEqualTo((1, 0));
        await Assert.That(await _repo.GetAllAsync()).HasSingleItem();
    }

    [Test]
    public async Task Secondary_Folder_Is_Scanned_As_The_Secondary_Set()
    {
        Write("a.jpg", "picture a");
        var backup = Directory.CreateTempSubdirectory("photosense-backup-");
        try
        {
            File.WriteAllText(Path.Combine(backup.FullName, "a copy.jpg"), "picture a");
            var summary = await ScanAsync(secondary: backup.FullName);

            await Assert.That(summary.Total).IsEqualTo(2);
            var photos = await _repo.GetAllAsync();
            await Assert.That(photos.Single(p => p.FileName == "a copy.jpg").Set).IsEqualTo(PhotoSet.Secondary);
            await Assert.That(photos.Single(p => p.FileName == "a copy.jpg").ScanRoot).IsEqualTo(backup.FullName);
            var progress = _progress.Get("scan");
            await Assert.That((progress.PrimaryTotal, progress.PrimaryProcessed, progress.SecondaryTotal, progress.SecondaryProcessed)).IsEqualTo((1, 1, 1, 1));
            var group = DuplicateAnalysisService.Analyze(photos).Duplicates.Single();
            await Assert.That(group.Keeper.FileName).IsEqualTo("a.jpg");
        }
        finally { backup.Delete(true); }
    }

    [Test]
    public async Task A_File_Reachable_From_Both_Folders_Is_Scanned_Once_As_Primary()
    {
        Write(Path.Combine("sub", "a.jpg"), "picture a");
        var summary = await ScanAsync(secondary: Path.Combine(_root.FullName, "sub"));

        await Assert.That(summary.Total).IsEqualTo(1);
        await Assert.That((await _repo.GetAllAsync()).Single().Set).IsEqualTo(PhotoSet.Primary);
    }

    [Test]
    public async Task Moving_A_Folder_Between_Sets_Updates_The_Record_Without_Reading_The_File_Again()
    {
        Write(Path.Combine("sub", "a.jpg"), "picture a");
        await ScanAsync();
        var sub = Path.Combine(_root.FullName, "sub");
        var other = Directory.CreateTempSubdirectory("photosense-other-");
        try
        {
            var summary = await _service.RunAsync(new ScanRequest(other.FullName, sub, true), "swap");
            await Assert.That(summary.Unchanged).IsEqualTo(1);
            var photo = (await _repo.GetAllAsync()).Single();
            await Assert.That((photo.Set, photo.ScanRoot)).IsEqualTo((PhotoSet.Secondary, sub));
            await Assert.That(_analyzer.Calls).IsEqualTo(1);
        }
        finally { other.Delete(true); }
    }

    [Test]
    public async Task Top_Level_Scan_Leaves_Subfolders_Out_And_Removed_Files_Are_Never_Scanned()
    {
        Write("a.jpg", "picture a");
        Write(Path.Combine("sub", "b.jpg"), "picture b");
        Write(Path.Combine(PhotoStorageOptions.RemovedFolderName, "a (1).jpg"), "picture a");

        await Assert.That((await ScanAsync()).Total).IsEqualTo(2);
        var topOnly = await ScanAsync(recursive: false, instance: "top");
        await Assert.That((topOnly.Total, topOnly.Pruned)).IsEqualTo((1, 1));
    }

    [Test]
    public async Task Stops_Without_Forgetting_Anything_When_Cancelled()
    {
        Write("a.jpg", "picture a");
        await ScanAsync();
        Write("b.jpg", "picture b");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _service.RunAsync(new ScanRequest(_root.FullName, null, true), "cancelled", cts.Token));

        await Assert.That(await _repo.GetAllAsync()).HasSingleItem();
        await Assert.That(_progress.Get("cancelled").CompletedUtc).IsNotNull();
    }

    [Test]
    public async Task A_Video_Is_Read_In_Full_Only_When_Another_Video_Has_Its_Size()
    {
        Write("clip.mov", "1234567890");
        Write("a.mp4", "same size AAAA");
        Write(Path.Combine("backup", "a.mp4"), "same size AAAA");
        Write("b.mp4", "same size BBBB");
        Write("picture.jpg", "1234567890"); // a picture's size says nothing about a video

        var summary = await ScanAsync();

        await Assert.That(summary).IsEqualTo(new ScanSummary(Total: 5, Analyzed: 5, Unchanged: 0, Unreadable: 0, Pruned: 0));
        var photos = (await _repo.GetAllAsync()).ToDictionary(p => Path.GetRelativePath(_root.FullName, p.SourcePath));
        var clip = photos["clip.mov"];
        await Assert.That(clip.IsVideo).IsTrue();
        await Assert.That(clip.ContentHash).IsNull(); // nothing else is its size, so nothing can be identical to it
        await Assert.That((clip.Format, clip.PerceptualHash, clip.Width, clip.AnalysisVersion)).IsEqualTo(("MOV", (string?)null, 0, ScanExecutionService.AnalysisVersion));
        await Assert.That(photos[Path.Combine("backup", "a.mp4")].ContentHash).IsEqualTo(photos["a.mp4"].ContentHash);
        await Assert.That(photos["a.mp4"].ContentHash).IsNotNull();
        await Assert.That(photos["b.mp4"].ContentHash).IsNotEqualTo(photos["a.mp4"].ContentHash);
        await Assert.That(_analyzer.Calls).IsEqualTo(1);        // only the picture is decoded
        _thumbnails.Saved.Single();

        var group = DuplicateAnalysisService.Analyze(photos.Values.ToList()).Duplicates.Single();
        await Assert.That((group.Keeper.FileName, group.Members.Single().Photo.FileName)).IsEqualTo(("a.mp4", "a.mp4"));

        await Assert.That((await ScanAsync(instance: "again")).Unchanged).IsEqualTo(5);

        // A second copy of the lone clip turns up: now both must be read to see whether they match.
        Write("clip copy.mov", "1234567890");
        var third = await ScanAsync(instance: "third");
        await Assert.That((third.Analyzed, third.Unchanged)).IsEqualTo((2, 4));
        await Assert.That(DuplicateAnalysisService.Analyze(await _repo.GetAllAsync()).Duplicates.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Empty_Files_Are_Recorded_But_Never_Matched_With_One_Another()
    {
        // Three zero-byte videos sat side by side in the sample library: transfers that never completed.
        Write("IMG_1501.MOV", "");
        Write("IMG_1502.MOV", "");
        Write("IMG_1503.JPG", "");
        Write("IMG_1504.JPG", "");

        var summary = await ScanAsync();

        await Assert.That(summary.Total).IsEqualTo(4);
        var photos = await _repo.GetAllAsync();
        foreach (var p in photos) await Assert.That(p.ContentHash).IsNull();
        await Assert.That(DuplicateAnalysisService.Analyze(photos).Duplicates).IsEmpty();
        await Assert.That((await ScanAsync(instance: "again")).Unchanged).IsEqualTo(4);
    }

    [Test]
    public async Task A_Record_Written_By_An_Earlier_Version_Of_The_Scan_Is_Read_Again()
    {
        var path = Write("a.jpg", "picture a");
        var info = new FileInfo(path);
        await _repo.AddOrUpdateAsync(new Photo
        {
            SourcePath = path, FileName = "a.jpg", FileSizeBytes = info.Length, FileModifiedUtc = info.LastWriteTimeUtc,
            ContentHash = "RECORDED-BEFORE", AnalysisVersion = ScanExecutionService.AnalysisVersion - 1, IsKept = true
        });

        var summary = await ScanAsync();

        await Assert.That((summary.Analyzed, summary.Unchanged)).IsEqualTo((1, 0));
        var photo = (await _repo.GetAllAsync()).Single();
        await Assert.That(photo.AnalysisVersion).IsEqualTo(ScanExecutionService.AnalysisVersion);
        await Assert.That(photo.ContentHash).IsNotEqualTo("RECORDED-BEFORE");
        await Assert.That(photo.IsKept).IsTrue();
    }

    [Test]
    public async Task Reports_Progress_As_It_Goes()
    {
        for (int i = 0; i < 100; i++) Write($"{i}.jpg", $"picture {i}");
        var service = new ScanExecutionService(_repo, new Sha256ImageHashingService(), _analyzer, Mock.Of<IPhotoMetadataExtractor>(), _thumbnails, _progress, _log.Object);
        await service.RunAsync(new ScanRequest(_root.FullName, null, true), "many");
        _log.Verify(l => l.Log("many", "Info", "Processed 100/100 files"), Times.Once);
        await Assert.That(_progress.Get("many").OverallPercent).IsEqualTo(100);
    }


    [Test]
    public async Task A_File_Touched_Without_Changing_Size_Is_Read_Again()
    {
        var path = Write("a.jpg", "picture a");
        await ScanAsync();
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(5));

        var summary = await ScanAsync(instance: "again");

        await Assert.That((summary.Analyzed, summary.Unchanged)).IsEqualTo((1, 0));
        await Assert.That((await _repo.GetAllAsync()).Single().FileModifiedUtc).IsEqualTo(File.GetLastWriteTimeUtc(path));
    }

    [Test]
    public async Task A_Record_Without_A_Modified_Time_Is_Read_Again()
    {
        var path = Write("a.jpg", "picture a");
        await _repo.AddOrUpdateAsync(new Photo
        {
            SourcePath = path, FileName = "a.jpg", FileSizeBytes = new FileInfo(path).Length,
            ContentHash = "RECORDED-BEFORE", AnalysisVersion = ScanExecutionService.AnalysisVersion
        });

        var summary = await ScanAsync();

        await Assert.That((summary.Analyzed, summary.Unchanged)).IsEqualTo((1, 0));
        var photo = (await _repo.GetAllAsync()).Single();
        await Assert.That(photo.FileModifiedUtc).IsEqualTo(new FileInfo(path).LastWriteTimeUtc);
        await Assert.That(photo.ContentHash).IsNotEqualTo("RECORDED-BEFORE");
    }

    [Test]
    public async Task A_Video_That_Vanishes_As_The_Scan_Starts_Is_Counted_Unreadable()
    {
        var gone = Write("a.mov", "same size AAAA");
        Write("b.mov", "same size BBBB");
        // The folder has been listed by the time the totals are reported; the file goes just after.
        var progress = new Mock<IScanProgressStore>();
        progress.Setup(p => p.SetTotals(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>())).Callback(() => File.Delete(gone));
        var service = new ScanExecutionService(_repo, new Sha256ImageHashingService(), _analyzer, Mock.Of<IPhotoMetadataExtractor>(), _thumbnails, progress.Object, _log.Object, parallelism: 1);

        var summary = await service.RunAsync(new ScanRequest(_root.FullName, null, true), "scan");

        await Assert.That(summary).IsEqualTo(new ScanSummary(Total: 2, Analyzed: 1, Unchanged: 0, Unreadable: 1, Pruned: 0));
        var remaining = (await _repo.GetAllAsync()).Single();
        await Assert.That(remaining.FileName).IsEqualTo("b.mov");
        await Assert.That(remaining.ContentHash).IsNull(); // with the other gone, nothing shares its size
    }

    [Test]
    public async Task Scans_The_Same_With_Nowhere_To_Log()
    {
        var quiet = new ScanExecutionService(_repo, new Sha256ImageHashingService(), _analyzer, Mock.Of<IPhotoMetadataExtractor>(), _thumbnails, _progress, parallelism: 1);
        var missing = Path.Combine(_root.FullName, "no-such-folder");
        await Assert.That(await quiet.RunAsync(new ScanRequest(missing, null, true), "typo")).IsEqualTo(new ScanSummary(0, 0, 0, 0, 0));

        for (int i = 0; i < 98; i++) Write($"{i}.jpg", $"picture {i}");
        Write("broken.heic", FakeAnalyzer.Undecodable);
        var locked = Write("locked.jpg", "another picture");
        ScanSummary summary;
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            summary = await quiet.RunAsync(new ScanRequest(_root.FullName, null, true), "quiet");

        await Assert.That(summary).IsEqualTo(new ScanSummary(Total: 100, Analyzed: 98, Unchanged: 0, Unreadable: 2, Pruned: 0));
        await Assert.That(_progress.Get("quiet").OverallPercent).IsEqualTo(100);
        _log.VerifyNoOtherCalls();
    }

    private sealed class FakeAnalyzer : IImageAnalyzer
    {
        public const string Undecodable = "not an image";
        private int _calls;
        public int Calls => _calls;

        public async Task<ImageAnalysis> AnalyzeAsync(Stream imageStream, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            using var reader = new StreamReader(imageStream, leaveOpen: true);
            var content = await reader.ReadToEndAsync(ct);
            if (content == Undecodable) throw new InvalidDataException("no decode delegate");
            return new ImageAnalysis(640, 480, "JPEG", 90, HashOf(content), new byte[TestPhotos.SignatureLength], [1, 2, 3]);
        }

        // Different content gives an unrelated hash, as different pictures do.
        public static ulong HashOf(string content)
            => BitConverter.ToUInt64(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content)));

        public Task<byte[]> RenderJpegAsync(Stream imageStream, int maxEdge, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeThumbnails : IThumbnailStore
    {
        public System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> Saved { get; } = new();
        public Task SaveAsync(string contentHash, byte[] jpeg, CancellationToken ct = default) { Saved[contentHash] = jpeg; return Task.CompletedTask; }
        public Task<byte[]?> GetAsync(string contentHash, CancellationToken ct = default) => Task.FromResult(Saved.GetValueOrDefault(contentHash));
        public Task ClearAsync(CancellationToken ct = default) { Saved.Clear(); return Task.CompletedTask; }
    }
}
