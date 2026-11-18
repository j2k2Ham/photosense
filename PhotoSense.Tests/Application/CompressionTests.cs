using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Api;
using PhotoSense.Api.About;
using PhotoSense.Api.Hosting;
using PhotoSense.Api.Http;
using PhotoSense.Application.Plans;
using PhotoSense.Application.Removal;
using PhotoSense.Desktop.Imaging;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Infrastructure.Library;
using PhotoSense.Infrastructure.Transfer;
using PhotoSense.Tests.Endpoints;

namespace PhotoSense.Tests.Application;

/// <summary>
/// Compressing large videos on a phone: the phone's whole stack on a library in memory, asked as the page asks. Each
/// video is made into a smaller copy by the phone, and the originals then go to its Recently Deleted after its question.
/// </summary>
public sealed class CompressionTests : IDisposable
{
    private const long Large = 400L * 1024 * 1024;
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("photosense-compress-");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "photosense-compress-library-" + Guid.NewGuid().ToString("N"), "Photo Library");
    private readonly FakePhotoLibrary _library = new();
    private readonly FakeAppStore _app = new();
    private readonly ServiceProvider _phone;
    private readonly ApiRouter _router;

    public CompressionTests()
    {
        _phone = new ServiceCollection()
            .AddPhotoSensePhone(_library, new FakePhotoAccess(), new MagickImageAnalyzer(), In("Documents"), In("Caches"), new FakeAdvertiser(), _app, new FakeCodeScanner(),
                new FakeComputerFinder(), new FakeDevice(), new FakeNotifier(), new FakeShareSheet(), "Jamie's iPhone", _root, new TcpTransferListener(0))
            .AddPhotoSenseApi()
            .AddSingleton(new ProgramInfo("iphone", "1.2.0.57", () => "notices"))
            .AddStandInStores()
            .AddSingleton<TimeProvider>(new TestClock())
            .BuildServiceProvider();
        _router = _phone.GetRequiredService<PhotoSenseRoutes>().Router;
    }

    public void Dispose()
    {
        _phone.Dispose();
        TestFiles.Remove(_data);
    }

    private string In(string folder) => Path.Combine(_data.FullName, folder);

    private VideoCompression Compression => _phone.GetRequiredService<VideoCompression>();

    private async Task<(HttpStatusCode Status, string Text)> AskAsync(string method, string path, CancellationToken ct, object? body = null, bool asPage = true)
    {
        var answer = await _router.AnswerAsync(method, new Uri("app://photosense/api/" + path), asPage ? [new("x-photosense-client", "phone")] : [],
            body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body), ct);
        return ((HttpStatusCode)answer.Status, Encoding.UTF8.GetString(answer.Body));
    }

    private async Task<JsonElement> PhoneAsync(string method, string path, CancellationToken ct, object? body = null)
    {
        var (status, text) = await AskAsync(method, path, ct, body);
        await Assert.That(status).IsEqualTo(HttpStatusCode.OK).Because(text);
        return JsonDocument.Parse(text).RootElement;
    }

    private async Task ScanAsync(CancellationToken ct)
    {
        await Assert.That((await AskAsync("POST", "scan/start", ct, new { primaryLocation = _root, recursive = true })).Status).IsEqualTo(HttpStatusCode.Accepted);
        for (var waited = 0; (await PhoneAsync("GET", "scan/status", ct)).GetProperty("completed").ValueKind == JsonValueKind.Null; waited++)
        {
            if (waited == 600) throw new TimeoutException("The scan did not finish.");
            await Task.Delay(50, ct);
        }
    }

    private void Pro() => _app.Purchases.Add(new StorePurchase(PlanProducts.Lifetime, TestClock.Day, null, Trial: false));

    /// <summary>
    /// Videos as the phone films them, recorded as 4K, a minute long and large, and a picture; a video whose frame the scan
    /// could not read when <paramref name="unknownFrame"/>. Answers the videos' records, first to last.
    /// </summary>
    private async Task<List<Photo>> VideosAsync(CancellationToken ct, int count = 1, bool unknownFrame = false)
    {
        for (var n = 1; n <= count; n++)
        {
            _library.Add($"VVVVVVV{n}-{n}", (LibraryFileKind.Video, $"IMG_000{n}.MOV", MediaSamples.Movie(null)));
            _library.Sized($"VVVVVVV{n}-{n}", Large);
        }
        _library.Add("PPPPPPPP-9", (LibraryFileKind.Photo, "IMG_0009.JPG", MediaSamples.Picture("NINE")));
        await ScanAsync(ct);
        var repo = _phone.GetRequiredService<IPhotoRepository>();
        var videos = (await repo.GetAllAsync(ct)).Where(p => p.IsVideo).OrderBy(p => p.FileName, StringComparer.Ordinal).ToList();
        foreach (var video in videos)
        {
            video.Width = unknownFrame ? 0 : 3840;
            video.Height = unknownFrame ? 0 : 2160;
            video.DurationSeconds = 60;
            await repo.AddOrUpdateAsync(video, ct);
        }
        return videos;
    }

    private async Task<JsonElement> FinishedAsync(CancellationToken ct)
    {
        await Compression.Finished.WaitAsync(ct);
        return await PhoneAsync("GET", "compress", ct);
    }

    [Test]
    public async Task Large_Videos_Are_Offered_With_About_What_Each_Copy_Takes_At_Each_Quality(CancellationToken ct)
    {
        var video = (await VideosAsync(ct)).Single();
        var offered = (await PhoneAsync("GET", "compress/videos", ct)).EnumerateArray().Single();
        await Assert.That(offered.GetProperty("video").GetProperty("fileName").GetString()).IsEqualTo("IMG_0001.MOV");
        var estimates = offered.GetProperty("estimates");
        foreach (var (name, quality) in new[] { ("high", CompressionQuality.High), ("balanced", CompressionQuality.Balanced), ("small", CompressionQuality.Small) })
            await Assert.That(estimates.GetProperty(name).GetInt64()).IsEqualTo(VideoTargets.Estimate(quality, 3840, 2160, 60, Large)!.Value);
        await Assert.That(video.Id.Value).IsEqualTo(offered.GetProperty("video").GetProperty("id").GetGuid());

        // Nothing is compressing yet.
        var idle = await PhoneAsync("GET", "compress", ct);
        await Assert.That((idle.GetProperty("stage").GetString(), idle.GetProperty("total").GetInt32())).IsEqualTo(("idle", 0));
    }

    [Test]
    public async Task Compressing_Is_Part_Of_Pro(CancellationToken ct)
    {
        var video = (await VideosAsync(ct)).Single();
        var refused = await AskAsync("POST", "compress/start", ct, new { ids = new[] { video.Id.Value }, quality = "balanced" });
        await Assert.That(refused).IsEqualTo((HttpStatusCode.Conflict, "Video compression is part of Pro."));
        await Assert.That(_library.Compressed).IsEmpty();
    }

    [Test]
    public async Task On_Pro_Each_Video_Becomes_A_Smaller_Copy_And_The_Originals_Go_To_Recently_Deleted_After_The_Phones_Question(CancellationToken ct)
    {
        Pro();
        var videos = await VideosAsync(ct, count: 2);
        var started = await PhoneAsync("POST", "compress/start", ct, new { ids = videos.Select(v => v.Id.Value).ToArray(), quality = "balanced" });
        await Assert.That((started.GetProperty("stage").GetString(), started.GetProperty("quality").GetString(), started.GetProperty("total").GetInt32()))
            .IsEqualTo(("running", "balanced", 2));

        var done = await FinishedAsync(ct);
        var target = VideoTargets.For(CompressionQuality.Balanced, 3840, 2160);
        await Assert.That(_library.Compressed.Select(c => c.Target)).IsEquivalentTo([target, target]);
        await Assert.That((done.GetProperty("stage").GetString(), done.GetProperty("done").GetInt32(), done.GetProperty("progress").GetDouble(), done.GetProperty("originalsDeleted").GetBoolean()))
            .IsEqualTo(("done", 2, 1.0, true));
        await Assert.That(done.GetProperty("current").ValueKind).IsEqualTo(JsonValueKind.Null);
        var after = target.VideoBitsPerSecond / 1000;
        await Assert.That(done.GetProperty("results").EnumerateArray().Select(r => (r.GetProperty("fileName").GetString()!, r.GetProperty("before").GetInt64(), r.GetProperty("after").GetInt64())))
            .IsEquivalentTo([("IMG_0001.MOV", Large, after), ("IMG_0002.MOV", Large, after)], CollectionOrdering.Matching);
        await Assert.That(done.GetProperty("saved").GetInt64()).IsEqualTo(2 * (Large - after));

        // The originals went together, in one question; their records went with them, and the copies are in the library.
        await Assert.That(_library.Changes.Count(c => c.Delete.Count > 0)).IsEqualTo(1);
        await Assert.That(_library.RecentlyDeleted).IsEquivalentTo(["VVVVVVV1-1", "VVVVVVV2-2"]);
        var left = await _phone.GetRequiredService<IPhotoRepository>().GetAllAsync(ct);
        await Assert.That(left.Any(p => p.FileName is "IMG_0001.MOV" or "IMG_0002.MOV")).IsFalse();
        await Assert.That(_library.Items().Select(i => i.Files[0].FileName)).Contains("IMG_0001 (smaller).MOV");
    }

    [Test]
    public async Task Said_No_The_Originals_And_Their_Copies_Are_Both_Kept(CancellationToken ct)
    {
        Pro();
        var video = (await VideosAsync(ct)).Single();
        _library.Refuse = "You chose not to delete it.";
        await PhoneAsync("POST", "compress/start", ct, new { ids = new[] { video.Id.Value }, quality = "small" });
        var done = await FinishedAsync(ct);
        await Assert.That((done.GetProperty("stage").GetString(), done.GetProperty("originalsDeleted").GetBoolean())).IsEqualTo(("done", false));
        await Assert.That(_library.RecentlyDeleted).IsEmpty();
        await Assert.That(await _phone.GetRequiredService<IPhotoRepository>().GetAsync(video.Id, ct)).IsNotNull();
        await Assert.That(_library.Compressed.Single().Target).IsEqualTo(VideoTargets.For(CompressionQuality.Small, 3840, 2160));

        // Ended, another can start.
        await PhoneAsync("POST", "compress/start", ct, new { ids = new[] { video.Id.Value }, quality = "high" });
        await Assert.That((await FinishedAsync(ct)).GetProperty("quality").GetString()).IsEqualTo("high");
        await Assert.That(_library.Compressed.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Cancelled_It_Stops_After_The_Video_It_Is_On_And_What_Was_Compressed_Before_Is_Kept(CancellationToken ct)
    {
        Pro();
        var videos = await VideosAsync(ct, count: 2);
        var second = new TaskCompletionSource();
        _library.Compressing = async (id, token) =>
        {
            if (id != "VVVVVVV2-2") return;
            second.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        };
        await PhoneAsync("POST", "compress/start", ct, new { ids = videos.Select(v => v.Id.Value).ToArray(), quality = "high" });
        await second.Task.WaitAsync(ct);

        // One at a time: another cannot start while this runs.
        var busy = await AskAsync("POST", "compress/start", ct, new { ids = new[] { videos[0].Id.Value }, quality = "high" });
        await Assert.That(busy).IsEqualTo((HttpStatusCode.Conflict, "Videos are being compressed already. Wait for them, or cancel."));
        var running = await PhoneAsync("GET", "compress", ct);
        await Assert.That((running.GetProperty("current").GetString(), running.GetProperty("done").GetInt32(), running.GetProperty("progress").GetDouble())).IsEqualTo(("IMG_0002.MOV", 1, 0.75));

        await Assert.That((await PhoneAsync("POST", "compress/cancel", ct)).GetProperty("stage").GetString()).IsEqualTo("running");
        var stopped = await FinishedAsync(ct);
        await Assert.That((stopped.GetProperty("stage").GetString(), stopped.GetProperty("done").GetInt32(), stopped.GetProperty("results").GetArrayLength(), stopped.GetProperty("originalsDeleted").GetBoolean()))
            .IsEqualTo(("stopped", 1, 1, true));
        await Assert.That(_library.RecentlyDeleted).IsEquivalentTo(["VVVVVVV1-1"]);

        // Cancelling when nothing runs changes nothing.
        await Assert.That((await PhoneAsync("POST", "compress/cancel", ct)).GetProperty("stage").GetString()).IsEqualTo("stopped");
    }

    [Test]
    public async Task A_Video_The_Phone_Cannot_Compress_Ends_It_Saying_Why_And_Keeps_What_Was_Compressed(CancellationToken ct)
    {
        Pro();
        var videos = await VideosAsync(ct, count: 2);
        _library.Compressing = (id, _) => id == "VVVVVVV2-2" ? throw new IOException("IMG_0002.MOV could not be read.") : Task.CompletedTask;
        await PhoneAsync("POST", "compress/start", ct, new { ids = videos.Select(v => v.Id.Value).ToArray(), quality = "balanced" });
        var failed = await FinishedAsync(ct);
        await Assert.That((failed.GetProperty("stage").GetString(), failed.GetProperty("problem").GetString(), failed.GetProperty("done").GetInt32(), failed.GetProperty("progress").GetDouble()))
            .IsEqualTo(("failed", "IMG_0002.MOV could not be read.", 1, 0.5));
        await Assert.That(_library.RecentlyDeleted).IsEquivalentTo(["VVVVVVV1-1"]);
    }

    [Test]
    public async Task Nothing_Compressed_Nothing_Is_Asked_About(CancellationToken ct)
    {
        Pro();
        var video = (await VideosAsync(ct)).Single();
        _library.Compressing = (_, _) => throw new IOException("The video is in iCloud and could not be fetched.");
        await PhoneAsync("POST", "compress/start", ct, new { ids = new[] { video.Id.Value }, quality = "balanced" });
        var failed = await FinishedAsync(ct);
        await Assert.That((failed.GetProperty("stage").GetString(), failed.GetProperty("originalsDeleted").ValueKind)).IsEqualTo(("failed", JsonValueKind.Null));
        await Assert.That(_library.Changes).IsEmpty();
    }

    [Test]
    public async Task Only_Videos_Whose_Frame_The_Scan_Read_Are_Compressed_And_A_Request_Says_Which_And_How_Small(CancellationToken ct)
    {
        Pro();
        var video = (await VideosAsync(ct, unknownFrame: true)).Single();
        var picture = (await _phone.GetRequiredService<IPhotoRepository>().GetAllAsync(ct)).Single(p => !p.IsVideo);
        // Its frame unknown, no saving can be said for it.
        var offered = (await PhoneAsync("GET", "compress/videos", ct)).EnumerateArray().Single().GetProperty("estimates");
        await Assert.That(offered.GetProperty("balanced").ValueKind).IsEqualTo(JsonValueKind.Null);

        foreach (var ids in new Guid[][] { [video.Id.Value], [picture.Id.Value], [Guid.NewGuid()] })
            await Assert.That(await AskAsync("POST", "compress/start", ct, new { ids, quality = "balanced" }))
                .IsEqualTo((HttpStatusCode.Conflict, "None of these is a video PhotoSense can compress. Scan again, then try again."));
        foreach (object body in new object[] { new { ids = Array.Empty<Guid>(), quality = "balanced" }, new { ids = new[] { video.Id.Value }, quality = "tiny" } })
            await Assert.That(await AskAsync("POST", "compress/start", ct, body))
                .IsEqualTo((HttpStatusCode.BadRequest, "Say which videos, and how small: high, balanced or small."));
        await Assert.That(await AskAsync("POST", "compress/start", ct))
            .IsEqualTo((HttpStatusCode.BadRequest, "Say which videos, and how small: high, balanced or small."));
    }

    [Test]
    public async Task Only_A_Phone_Compresses_And_Only_The_Page_May_Ask(CancellationToken ct)
    {
        await Assert.That((await AskAsync("GET", "compress", ct, asPage: false)).Status).IsEqualTo(HttpStatusCode.Unauthorized);
        var computer = new TestApi();
        foreach (var (method, path) in new[] { ("GET", "compress/videos"), ("GET", "compress"), ("POST", "compress/start"), ("POST", "compress/cancel") })
        {
            var refused = await computer.SendAsync((method == "GET" ? Http.Get(path) : Http.Post(path)).FromClient(), ct, path);
            await Assert.That((refused.StatusCode, refused.Text())).IsEqualTo((HttpStatusCode.NotFound, "Only a phone compresses videos."));
        }
    }

    [Test]
    public async Task A_Phone_Compresses_Only_Its_Videos(CancellationToken ct)
    {
        _library.Add("PPPPPPPP-1", (LibraryFileKind.Photo, "IMG_0001.JPG", MediaSamples.Picture("ONE")));
        var photos = new PhotoLibraryFileSystem(_library, _root);
        var target = VideoTargets.For(CompressionQuality.Balanced, 1920, 1080);
        var picture = await Assert.ThrowsAsync<IOException>(async () => await photos.CompressAsync(Path.Combine(_root, "IMG_0001.JPG"), target, new Progress<double>(), ct));
        await Assert.That(picture!.Message).IsEqualTo("IMG_0001.JPG is not a video in the library.");
        var gone = await Assert.ThrowsAsync<IOException>(async () => await photos.CompressAsync(Path.Combine(_root, "IMG_0404.MOV"), target, new Progress<double>(), ct));
        await Assert.That(gone!.Message).IsEqualTo("IMG_0404.MOV is not a video in the library.");

        var store = new FakeMediaStore();
        store.Add("1", "DCIM/Camera/", "VID_1.mp4", [1, 2, 3], video: true);
        store.Add("2", "DCIM/Camera/", "IMG_2.jpg", [4, 5, 6]);
        // Shown below a folder of this computer's, as the other Android tests have it.
        var phone = Path.Combine(_data.FullName, "Phone");
        var camera = Path.Combine(phone, "DCIM", "Camera");
        var android = new MediaFileSystem(store, phone);
        await Assert.That(await android.CompressAsync(Path.Combine(camera, "VID_1.mp4"), target, new Progress<double>(), ct)).IsEqualTo(target.VideoBitsPerSecond / 1000);
        await Assert.That(android.FileExists(Path.Combine(camera, "VID_1 (smaller).mp4"))).IsTrue();
        var still = await Assert.ThrowsAsync<IOException>(async () => await android.CompressAsync(Path.Combine(camera, "IMG_2.jpg"), target, new Progress<double>(), ct));
        await Assert.That(still!.Message).IsEqualTo("IMG_2.jpg is not a video on the phone.");
    }
}
