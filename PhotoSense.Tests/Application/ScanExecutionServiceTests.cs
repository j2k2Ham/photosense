using Moq;
using PhotoSense.Application.Scanning;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Hashing;
using PhotoSense.Infrastructure.Persistence;
using Xunit;

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

    private Task<ScanSummary> ScanAsync(string? secondary = null, bool recursive = true, string instance = "scan")
        => _service.RunAsync(new ScanRequest(_root.FullName, secondary, recursive), instance);

    [Fact]
    public async Task Records_Each_Image_With_Its_Hashes_And_Measurements()
    {
        var path = Write("a.jpg", "picture a");
        Write("notes.txt", "not a picture");

        var summary = await ScanAsync();

        Assert.Equal(new ScanSummary(Total: 1, Analyzed: 1, Unchanged: 0, Unreadable: 0, Pruned: 0), summary);
        var photo = Assert.Single(await _repo.GetAllAsync());
        Assert.Equal(path, photo.SourcePath);
        Assert.Equal("a.jpg", photo.FileName);
        Assert.Equal(new FileInfo(path).Length, photo.FileSizeBytes);
        Assert.Equal(new FileInfo(path).LastWriteTimeUtc, photo.FileModifiedUtc);
        Assert.Equal(_root.FullName, photo.ScanRoot);
        Assert.Equal(PhotoSet.Primary, photo.Set);
        Assert.Equal(64, photo.ContentHash!.Length);
        Assert.Equal(FakeAnalyzer.HashOf("picture a").ToString("X16"), photo.PerceptualHash);
        Assert.Equal((640, 480, "JPEG", 90), (photo.Width, photo.Height, photo.Format, photo.EncodedQuality));
        Assert.NotNull(photo.Signature);
        Assert.True(_thumbnails.Saved.ContainsKey(photo.ContentHash));
        var progress = _progress.Get("scan");
        Assert.Equal((1, 1), (progress.PrimaryTotal, progress.PrimaryProcessed));
        Assert.NotNull(progress.CompletedUtc);
    }

    [Fact]
    public async Task Scanning_Again_Does_Not_Make_A_File_A_Duplicate_Of_Itself()
    {
        Write("a.jpg", "picture a");
        Write("b.jpg", "picture b");
        await ScanAsync();
        var firstIds = (await _repo.GetAllAsync()).Select(p => p.Id).OrderBy(i => i.Value).ToList();

        var second = await ScanAsync(instance: "again");

        Assert.Equal(new ScanSummary(Total: 2, Analyzed: 0, Unchanged: 2, Unreadable: 0, Pruned: 0), second);
        Assert.Equal(firstIds, (await _repo.GetAllAsync()).Select(p => p.Id).OrderBy(i => i.Value));
        Assert.Equal(2, _analyzer.Calls); // unchanged files are not decoded again
        Assert.Empty(DuplicateAnalysisService.Analyze(await _repo.GetAllAsync()).Duplicates);
    }

    [Fact]
    public async Task A_Changed_File_Is_Read_Again_And_Keeps_Its_Record_And_Keep_Mark()
    {
        var path = Write("a.jpg", "picture a");
        await ScanAsync();
        var before = Assert.Single(await _repo.GetAllAsync());
        before.IsKept = true;
        await _repo.AddOrUpdateAsync(before);

        File.WriteAllText(path, "picture a, edited and longer");
        var summary = await ScanAsync(instance: "again");

        Assert.Equal(1, summary.Analyzed);
        var after = Assert.Single(await _repo.GetAllAsync());
        Assert.Equal(before.Id, after.Id);
        Assert.NotEqual(before.ContentHash, after.ContentHash);
        Assert.True(after.IsKept);
    }

    [Fact]
    public async Task Forgets_Files_That_Are_No_Longer_There()
    {
        var gone = Write("gone.jpg", "picture");
        Write("stays.jpg", "another");
        await ScanAsync();

        File.Delete(gone);
        var summary = await ScanAsync(instance: "again");

        Assert.Equal(1, summary.Pruned);
        Assert.Equal("stays.jpg", Assert.Single(await _repo.GetAllAsync()).FileName);
    }

    [Fact]
    public async Task A_Missing_Folder_Scans_Nothing_And_Forgets_Nothing()
    {
        Write("a.jpg", "picture a");
        await ScanAsync();

        var missing = Path.Combine(_root.FullName, "no-such-folder");
        var summary = await _service.RunAsync(new ScanRequest(missing, null, true), "typo");
        var blank = await _service.RunAsync(new ScanRequest(" ", null, true), "blank");
        var missingSecondary = await ScanAsync(secondary: missing, instance: "typo2");

        Assert.Equal(new ScanSummary(0, 0, 0, 0, 0), summary);
        Assert.Equal(new ScanSummary(0, 0, 0, 0, 0), blank);
        Assert.Equal(new ScanSummary(0, 0, 0, 0, 0), missingSecondary);
        Assert.Single(await _repo.GetAllAsync());
        Assert.NotNull(_progress.Get("typo").CompletedUtc);
        _log.Verify(l => l.Log("typo", "Error", It.Is<string>(m => m.Contains(missing))), Times.Once);
    }

    [Fact]
    public async Task A_File_That_Cannot_Be_Decoded_Is_Still_Recorded_By_Its_Content()
    {
        Write("broken.heic", FakeAnalyzer.Undecodable);

        var summary = await ScanAsync();

        Assert.Equal((0, 1), (summary.Analyzed, summary.Unreadable));
        var photo = Assert.Single(await _repo.GetAllAsync());
        Assert.NotNull(photo.ContentHash);
        Assert.Null(photo.PerceptualHash);
        Assert.Equal(0, photo.Width);
        _log.Verify(l => l.Log("scan", "Warn", It.Is<string>(m => m.Contains("broken.heic"))), Times.Once);

        // It is not attempted again while it stays unchanged.
        Assert.Equal(1, (await ScanAsync(instance: "again")).Unchanged);
        Assert.Equal(1, _analyzer.Calls);
    }

    [Fact]
    public async Task A_File_That_Cannot_Be_Opened_Keeps_Its_Earlier_Record()
    {
        var path = Write("locked.jpg", "picture");
        await ScanAsync();
        File.WriteAllText(path, "changed, so it has to be read again");

        ScanSummary summary;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            summary = await ScanAsync(instance: "again");

        Assert.Equal((1, 0), (summary.Unreadable, summary.Pruned));
        Assert.Single(await _repo.GetAllAsync());
    }

    [Fact]
    public async Task Secondary_Folder_Is_Scanned_As_The_Secondary_Set()
    {
        Write("a.jpg", "picture a");
        var backup = Directory.CreateTempSubdirectory("photosense-backup-");
        try
        {
            File.WriteAllText(Path.Combine(backup.FullName, "a copy.jpg"), "picture a");
            var summary = await ScanAsync(secondary: backup.FullName);

            Assert.Equal(2, summary.Total);
            var photos = await _repo.GetAllAsync();
            Assert.Equal(PhotoSet.Secondary, photos.Single(p => p.FileName == "a copy.jpg").Set);
            Assert.Equal(backup.FullName, photos.Single(p => p.FileName == "a copy.jpg").ScanRoot);
            var progress = _progress.Get("scan");
            Assert.Equal((1, 1, 1, 1), (progress.PrimaryTotal, progress.PrimaryProcessed, progress.SecondaryTotal, progress.SecondaryProcessed));
            var group = Assert.Single(DuplicateAnalysisService.Analyze(photos).Duplicates);
            Assert.Equal("a.jpg", group.Keeper.FileName);
        }
        finally { backup.Delete(true); }
    }

    [Fact]
    public async Task A_File_Reachable_From_Both_Folders_Is_Scanned_Once_As_Primary()
    {
        Write(Path.Combine("sub", "a.jpg"), "picture a");
        var summary = await ScanAsync(secondary: Path.Combine(_root.FullName, "sub"));

        Assert.Equal(1, summary.Total);
        Assert.Equal(PhotoSet.Primary, Assert.Single(await _repo.GetAllAsync()).Set);
    }

    [Fact]
    public async Task Moving_A_Folder_Between_Sets_Updates_The_Record_Without_Reading_The_File_Again()
    {
        Write(Path.Combine("sub", "a.jpg"), "picture a");
        await ScanAsync();
        var sub = Path.Combine(_root.FullName, "sub");
        var other = Directory.CreateTempSubdirectory("photosense-other-");
        try
        {
            var summary = await _service.RunAsync(new ScanRequest(other.FullName, sub, true), "swap");
            Assert.Equal(1, summary.Unchanged);
            var photo = Assert.Single(await _repo.GetAllAsync());
            Assert.Equal((PhotoSet.Secondary, sub), (photo.Set, photo.ScanRoot));
            Assert.Equal(1, _analyzer.Calls);
        }
        finally { other.Delete(true); }
    }

    [Fact]
    public async Task Top_Level_Scan_Leaves_Subfolders_Out_And_Removed_Files_Are_Never_Scanned()
    {
        Write("a.jpg", "picture a");
        Write(Path.Combine("sub", "b.jpg"), "picture b");
        Write(Path.Combine(PhotoStorageOptions.RemovedFolderName, "a (1).jpg"), "picture a");

        Assert.Equal(2, (await ScanAsync()).Total);
        var topOnly = await ScanAsync(recursive: false, instance: "top");
        Assert.Equal((1, 1), (topOnly.Total, topOnly.Pruned));
    }

    [Fact]
    public async Task Stops_Without_Forgetting_Anything_When_Cancelled()
    {
        Write("a.jpg", "picture a");
        await ScanAsync();
        Write("b.jpg", "picture b");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.RunAsync(new ScanRequest(_root.FullName, null, true), "cancelled", cts.Token));

        Assert.Single(await _repo.GetAllAsync());
        Assert.NotNull(_progress.Get("cancelled").CompletedUtc);
    }

    [Fact]
    public async Task A_Video_Is_Read_In_Full_Only_When_Another_Video_Has_Its_Size()
    {
        Write("clip.mov", "1234567890");
        Write("a.mp4", "same size AAAA");
        Write(Path.Combine("backup", "a.mp4"), "same size AAAA");
        Write("b.mp4", "same size BBBB");
        Write("picture.jpg", "1234567890"); // a picture's size says nothing about a video

        var summary = await ScanAsync();

        Assert.Equal(new ScanSummary(Total: 5, Analyzed: 5, Unchanged: 0, Unreadable: 0, Pruned: 0), summary);
        var photos = (await _repo.GetAllAsync()).ToDictionary(p => Path.GetRelativePath(_root.FullName, p.SourcePath));
        var clip = photos["clip.mov"];
        Assert.True(clip.IsVideo);
        Assert.Null(clip.ContentHash); // nothing else is its size, so nothing can be identical to it
        Assert.Equal(("MOV", (string?)null, 0, ScanExecutionService.AnalysisVersion), (clip.Format, clip.PerceptualHash, clip.Width, clip.AnalysisVersion));
        Assert.Equal(photos["a.mp4"].ContentHash, photos[Path.Combine("backup", "a.mp4")].ContentHash);
        Assert.NotNull(photos["a.mp4"].ContentHash);
        Assert.NotEqual(photos["a.mp4"].ContentHash, photos["b.mp4"].ContentHash);
        Assert.Equal(1, _analyzer.Calls);        // only the picture is decoded
        Assert.Single(_thumbnails.Saved);

        var group = Assert.Single(DuplicateAnalysisService.Analyze(photos.Values.ToList()).Duplicates);
        Assert.Equal(("a.mp4", "a.mp4"), (group.Keeper.FileName, Assert.Single(group.Members).Photo.FileName));

        Assert.Equal(5, (await ScanAsync(instance: "again")).Unchanged);

        // A second copy of the lone clip turns up: now both must be read to see whether they match.
        Write("clip copy.mov", "1234567890");
        var third = await ScanAsync(instance: "third");
        Assert.Equal((2, 4), (third.Analyzed, third.Unchanged));
        Assert.Equal(2, DuplicateAnalysisService.Analyze(await _repo.GetAllAsync()).Duplicates.Count);
    }

    [Fact]
    public async Task Empty_Files_Are_Recorded_But_Never_Matched_With_One_Another()
    {
        // Three zero-byte videos sat side by side in the sample library: transfers that never completed.
        Write("IMG_1501.MOV", "");
        Write("IMG_1502.MOV", "");
        Write("IMG_1503.JPG", "");
        Write("IMG_1504.JPG", "");

        var summary = await ScanAsync();

        Assert.Equal(4, summary.Total);
        var photos = await _repo.GetAllAsync();
        Assert.All(photos, p => Assert.Null(p.ContentHash));
        Assert.Empty(DuplicateAnalysisService.Analyze(photos).Duplicates);
        Assert.Equal(4, (await ScanAsync(instance: "again")).Unchanged);
    }

    [Fact]
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

        Assert.Equal((1, 0), (summary.Analyzed, summary.Unchanged));
        var photo = Assert.Single(await _repo.GetAllAsync());
        Assert.Equal(ScanExecutionService.AnalysisVersion, photo.AnalysisVersion);
        Assert.NotEqual("RECORDED-BEFORE", photo.ContentHash);
        Assert.True(photo.IsKept);
    }

    [Fact]
    public async Task Reports_Progress_As_It_Goes()
    {
        for (int i = 0; i < 100; i++) Write($"{i}.jpg", $"picture {i}");
        var service = new ScanExecutionService(_repo, new Sha256ImageHashingService(), _analyzer, Mock.Of<IPhotoMetadataExtractor>(), _thumbnails, _progress, _log.Object);
        await service.RunAsync(new ScanRequest(_root.FullName, null, true), "many");
        _log.Verify(l => l.Log("many", "Info", "Processed 100/100 files"), Times.Once);
        Assert.Equal(100, _progress.Get("many").OverallPercent);
    }


    [Fact]
    public async Task A_File_Touched_Without_Changing_Size_Is_Read_Again()
    {
        var path = Write("a.jpg", "picture a");
        await ScanAsync();
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(5));

        var summary = await ScanAsync(instance: "again");

        Assert.Equal((1, 0), (summary.Analyzed, summary.Unchanged));
        Assert.Equal(File.GetLastWriteTimeUtc(path), Assert.Single(await _repo.GetAllAsync()).FileModifiedUtc);
    }

    [Fact]
    public async Task A_Record_Without_A_Modified_Time_Is_Read_Again()
    {
        var path = Write("a.jpg", "picture a");
        await _repo.AddOrUpdateAsync(new Photo
        {
            SourcePath = path, FileName = "a.jpg", FileSizeBytes = new FileInfo(path).Length,
            ContentHash = "RECORDED-BEFORE", AnalysisVersion = ScanExecutionService.AnalysisVersion
        });

        var summary = await ScanAsync();

        Assert.Equal((1, 0), (summary.Analyzed, summary.Unchanged));
        var photo = Assert.Single(await _repo.GetAllAsync());
        Assert.Equal(new FileInfo(path).LastWriteTimeUtc, photo.FileModifiedUtc);
        Assert.NotEqual("RECORDED-BEFORE", photo.ContentHash);
    }

    [Fact]
    public async Task A_Video_That_Vanishes_As_The_Scan_Starts_Is_Counted_Unreadable()
    {
        var gone = Write("a.mov", "same size AAAA");
        Write("b.mov", "same size BBBB");
        // The folder has been listed by the time the totals are reported; the file goes just after.
        var progress = new Mock<IScanProgressStore>();
        progress.Setup(p => p.SetTotals(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>())).Callback(() => File.Delete(gone));
        var service = new ScanExecutionService(_repo, new Sha256ImageHashingService(), _analyzer, Mock.Of<IPhotoMetadataExtractor>(), _thumbnails, progress.Object, _log.Object, parallelism: 1);

        var summary = await service.RunAsync(new ScanRequest(_root.FullName, null, true), "scan");

        Assert.Equal(new ScanSummary(Total: 2, Analyzed: 1, Unchanged: 0, Unreadable: 1, Pruned: 0), summary);
        var remaining = Assert.Single(await _repo.GetAllAsync());
        Assert.Equal("b.mov", remaining.FileName);
        Assert.Null(remaining.ContentHash); // with the other gone, nothing shares its size
    }

    [Fact]
    public async Task Scans_The_Same_With_Nowhere_To_Log()
    {
        var quiet = new ScanExecutionService(_repo, new Sha256ImageHashingService(), _analyzer, Mock.Of<IPhotoMetadataExtractor>(), _thumbnails, _progress, parallelism: 1);
        var missing = Path.Combine(_root.FullName, "no-such-folder");
        Assert.Equal(new ScanSummary(0, 0, 0, 0, 0), await quiet.RunAsync(new ScanRequest(missing, null, true), "typo"));

        for (int i = 0; i < 98; i++) Write($"{i}.jpg", $"picture {i}");
        Write("broken.heic", FakeAnalyzer.Undecodable);
        var locked = Write("locked.jpg", "another picture");
        ScanSummary summary;
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            summary = await quiet.RunAsync(new ScanRequest(_root.FullName, null, true), "quiet");

        Assert.Equal(new ScanSummary(Total: 100, Analyzed: 98, Unchanged: 0, Unreadable: 2, Pruned: 0), summary);
        Assert.Equal(100, _progress.Get("quiet").OverallPercent);
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
    }
}
