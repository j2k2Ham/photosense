using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Api;
using PhotoSense.Api.About;
using PhotoSense.Api.Hosting;
using PhotoSense.Api.Http;
using PhotoSense.Desktop.Imaging;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Infrastructure.Library;
using PhotoSense.Infrastructure.Transfer;
using PhotoSense.Tests.Endpoints;

namespace PhotoSense.Tests.Application;

/// <summary>
/// Clean up's categories on a phone: the phone's whole stack on a photo library in memory, scanned and asked as the page
/// asks. What the phone marks (screenshots, bursts) is found, and what is deleted goes to the phone's Recently Deleted.
/// </summary>
public sealed class CategoriesOnAPhoneTests : IDisposable
{
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("photosense-categories-");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "photosense-categories-library-" + Guid.NewGuid().ToString("N"), "Photo Library");
    private readonly FakePhotoLibrary _library = new();
    private readonly ServiceProvider _phone;
    private readonly ApiRouter _router;

    public CategoriesOnAPhoneTests()
    {
        _phone = new ServiceCollection()
            .AddPhotoSensePhone(_library, new FakePhotoAccess(), new MagickImageAnalyzer(), In("Documents"), In("Caches"), new FakeAdvertiser(), new FakeAppStore(), new FakeCodeScanner(),
                new FakeComputerFinder(), new FakeDevice(), new FakeNotifier(), new FakeShareSheet(), new FakeVaultKeys(), new FakeUnlocker(), "Jamie's iPhone", _root, new TcpTransferListener(0))
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

    private async Task<(HttpStatusCode Status, string Text)> AskAsync(string method, string path, CancellationToken ct, object? body = null)
    {
        var answer = await _router.AnswerAsync(method, new Uri("app://photosense/api/" + path), [new("x-photosense-client", "phone")], body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body), ct);
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

    private static Dictionary<string, (int Count, long Bytes)> Categories(JsonElement report)
        => report.GetProperty("categories").EnumerateArray().ToDictionary(c => c.GetProperty("category").GetString()!, c => (c.GetProperty("count").GetInt32(), c.GetProperty("bytes").GetInt64()));

    private static string[] Names(JsonElement photos) => [.. photos.EnumerateArray().Select(p => p.GetProperty("fileName").GetString()!)];

    /// <summary>Two screenshots, one the phone marks and one by its name; a burst of three the phone marks; and a picture on its own.</summary>
    private void Library()
    {
        _library.Add("AAAAAAAA-1", (LibraryFileKind.Photo, "IMG_0001.PNG", MediaSamples.Picture("SHOT-1")));
        _library.Mark("AAAAAAAA-1", screenshot: true);
        _library.Add("BBBBBBBB-2", (LibraryFileKind.Photo, "Screenshot 2026-11-17.png", MediaSamples.Picture("SHOT-2")));
        foreach (var n in new[] { 3, 4, 5 })
        {
            _library.Add($"CCCCCCC{n}-{n}", (LibraryFileKind.Photo, $"IMG_000{n}.JPG", MediaSamples.Picture($"BURST-{n}")));
            _library.Mark($"CCCCCCC{n}-{n}", burst: "BURST-A");
        }
        _library.Add("DDDDDDDD-6", (LibraryFileKind.Photo, "IMG_0006.JPG", MediaSamples.Picture("ALONE")));
    }

    [Test]
    public async Task The_Screenshots_And_Bursts_The_Phone_Marks_Are_Found_With_Each_Pictures_Sharpness_And_The_Phones_Storage(CancellationToken ct)
    {
        Library();
        await ScanAsync(ct);

        var report = await PhoneAsync("GET", "cleanup/categories", ct);
        var categories = Categories(report);
        await Assert.That(categories.Keys.Order()).IsEquivalentTo(["blurry", "bursts", "duplicates", "largeVideos", "messaging", "screenshots"], CollectionOrdering.Matching);
        await Assert.That((categories["screenshots"].Count, categories["bursts"].Count, categories["largeVideos"].Count, categories["messaging"].Count)).IsEqualTo((2, 2, 0, 0));
        await Assert.That((report.GetProperty("unmeasured").GetInt32(), report.GetProperty("storageTotal").GetInt64(), report.GetProperty("storageFree").GetInt64())).IsEqualTo((0, 128_000_000_000, 16_000_000_000));
        // Largest first.
        var sizes = report.GetProperty("categories").EnumerateArray().Select(c => c.GetProperty("bytes").GetInt64()).ToList();
        await Assert.That(sizes).IsEquivalentTo(sizes.OrderDescending(), CollectionOrdering.Matching);

        await Assert.That(Names(await PhoneAsync("GET", "cleanup/categories/screenshots", ct)).Order()).IsEquivalentTo(["IMG_0001.PNG", "Screenshot 2026-11-17.png"], CollectionOrdering.Matching);
        var burst = (await PhoneAsync("GET", "cleanup/categories/bursts", ct)).EnumerateArray().Single();
        await Assert.That(Names(burst.GetProperty("shots")).Order()).IsEquivalentTo(["IMG_0003.JPG", "IMG_0004.JPG", "IMG_0005.JPG"], CollectionOrdering.Matching);
        await Assert.That(burst.GetProperty("shots").EnumerateArray().Select(s => s.GetProperty("id").GetGuid())).Contains(burst.GetProperty("keeper").GetGuid());
        foreach (var none in new[] { "largeVideos", "messaging" })
            await Assert.That((await PhoneAsync("GET", $"cleanup/categories/{none}", ct)).GetArrayLength()).IsEqualTo(0);
        await Assert.That((await PhoneAsync("GET", "cleanup/categories/blurry", ct)).GetArrayLength()).IsEqualTo(categories["blurry"].Count);

        var photos = await _phone.GetRequiredService<IPhotoRepository>().GetAllAsync(ct);
        await Assert.That(photos.Where(p => !p.IsVideo).All(p => p.Sharpness is not null)).IsTrue();
        await Assert.That(photos.Count(p => p.BurstId == "BURST-A")).IsEqualTo(3);
    }

    [Test]
    public async Task What_Is_Chosen_Goes_To_Recently_Deleted_After_The_Phones_Question_Counting_Toward_The_Free_Removals(CancellationToken ct)
    {
        Library();
        // A Live Photo the phone marks as a screenshot: its video goes with it.
        _library.Add("EEEEEEEE-7", (LibraryFileKind.Photo, "IMG_0007.JPG", MediaSamples.Picture("LIVE-7")), (LibraryFileKind.PairedVideo, "IMG_0007.MOV", MediaSamples.Movie("LIVE-7")));
        _library.Mark("EEEEEEEE-7", screenshot: true);
        await ScanAsync(ct);
        var screenshots = (await PhoneAsync("GET", "cleanup/categories/screenshots", ct)).EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();

        // The person says no to the phone's question: nothing goes, and nothing is counted.
        _library.Refuse = "You chose not to let PhotoSense delete these photos.";
        await Assert.That(await AskAsync("POST", "cleanup/categories/delete", ct, new { ids = screenshots })).IsEqualTo((HttpStatusCode.Conflict, "You chose not to let PhotoSense delete these photos."));
        await Assert.That(_library.RecentlyDeleted).IsEmpty();

        var done = await PhoneAsync("POST", "cleanup/categories/delete", ct, new { ids = screenshots.Append(Guid.NewGuid()) });
        await Assert.That((done.GetProperty("deleted").GetInt32(), done.GetProperty("skipped").GetInt32(), done.GetProperty("batchId").ValueKind)).IsEqualTo((3, 0, JsonValueKind.Null));
        await Assert.That(_library.RecentlyDeleted.Order()).IsEquivalentTo(["AAAAAAAA-1", "BBBBBBBB-2", "EEEEEEEE-7"], CollectionOrdering.Matching);
        await Assert.That((await PhoneAsync("GET", "plan", ct)).GetProperty("leftToday").GetInt32()).IsEqualTo(47);
        // The Live Photo's video went with its picture, and its record is forgotten too.
        await Assert.That((await _phone.GetRequiredService<IPhotoRepository>().GetAllAsync(ct)).Any(p => p.FileName.StartsWith("IMG_0007", StringComparison.Ordinal))).IsFalse();
        await Assert.That(Categories(await PhoneAsync("GET", "cleanup/categories", ct))["screenshots"].Count).IsEqualTo(0);
        // What the record of a deleted photo said is forgotten too.
        await Assert.That((await _phone.GetRequiredService<IPhotoRepository>().GetAllAsync(ct)).Count(p => p.IsScreenshot)).IsEqualTo(0);

        // Nothing left of what was asked: nothing is asked of the phone.
        var nothing = await PhoneAsync("POST", "cleanup/categories/delete", ct, new { ids = new[] { Guid.NewGuid() } });
        await Assert.That(nothing.GetProperty("deleted").GetInt32()).IsEqualTo(0);

        // Over what is left today, nothing goes.
        await _phone.GetRequiredService<IPlanStore>().AddRemovedAsync(DateOnly.FromDateTime(TestClock.Day.LocalDateTime), 47, ct);
        var burst = (await PhoneAsync("GET", "cleanup/categories/bursts", ct)).EnumerateArray().Single().GetProperty("shots").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToList();
        var over = await AskAsync("POST", "cleanup/categories/delete", ct, new { ids = burst });
        await Assert.That((over.Status, over.Text.Contains("50", StringComparison.Ordinal))).IsEqualTo((HttpStatusCode.Conflict, true));
        await Assert.That(_library.RecentlyDeleted).Count().IsEqualTo(3);
    }

    [Test]
    public async Task The_Library_Says_Nothing_Of_A_Picture_It_Does_Not_Hold(CancellationToken ct)
    {
        var kinds = (PhotoSense.Domain.Services.IPhotoKinds)_phone.GetRequiredService<PhotoSense.Domain.Services.IPhotoFileSystem>();
        await Assert.That((kinds.IsScreenshot(Path.Combine(_root, "nowhere.JPG")), kinds.BurstOf(Path.Combine(_root, "nowhere.JPG")))).IsEqualTo((false, (string?)null));
    }

    [Test]
    public async Task Only_The_Page_May_Ask_And_A_Category_Must_Be_One_Clean_Up_Has(CancellationToken ct)
    {
        foreach (var path in new[] { "cleanup/categories", "cleanup/categories/screenshots" })
        {
            var answer = await _router.AnswerAsync("GET", new Uri("app://photosense/api/" + path), [], null, ct);
            await Assert.That((HttpStatusCode)answer.Status).IsEqualTo(HttpStatusCode.Unauthorized);
        }
        var delete = await _router.AnswerAsync("POST", new Uri("app://photosense/api/cleanup/categories/delete"), [], null, ct);
        await Assert.That((HttpStatusCode)delete.Status).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(await AskAsync("GET", "cleanup/categories/receipts", ct)).IsEqualTo((HttpStatusCode.NotFound, "Clean up has no category called receipts."));
        await Assert.That((await AskAsync("POST", "cleanup/categories/delete", ct, new { ids = Array.Empty<Guid>() })).Status).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await AskAsync("POST", "cleanup/categories/delete", ct, "not a request")).Status).IsEqualTo(HttpStatusCode.BadRequest);
    }
}

/// <summary>
/// Clean up's categories on a computer, from what its scans recorded: each category found, what the person marked to
/// keep left out, and what is deleted moved into _PhotoSense_Removed as one change, which Undo puts back.
/// </summary>
public sealed class CategoriesOnAComputerTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("photosense-categories-pc-");
    private readonly TestApi _api = new();

    public void Dispose() => _root.Delete(true);

    private IPhotoRepository Repo => _api.Services.GetRequiredService<IPhotoRepository>();

    private Task<ApiResponse> AskAsync(ApiRequest request, string path, CancellationToken ct) => _api.SendAsync(request.FromClient(), ct, path);

    // A real file with a record that matches it, as a scan would leave it.
    private async Task<Photo> FileAsync(string relativePath, CancellationToken ct, Action<Photo>? said = null)
    {
        var path = Path.Combine(_root.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, relativePath, ct);
        var info = new FileInfo(path);
        var photo = new Photo { SourcePath = path, FileName = info.Name, FileSizeBytes = info.Length, FileModifiedUtc = info.LastWriteTimeUtc, ScanRoot = _root.FullName, ContentHash = relativePath };
        said?.Invoke(photo);
        await Repo.AddOrUpdateAsync(photo, ct);
        return photo;
    }

    [Test]
    public async Task Each_Category_Is_Found_From_What_The_Scans_Recorded_Less_What_Was_Marked_Keep(CancellationToken ct)
    {
        var at = new DateTime(2026, 3, 3, 14, 31, 7);
        await Repo.SaveAllAsync([
            TestPhotos.Video("Trip.MOV", size: 900L * 1024 * 1024), TestPhotos.Video("Short.MOV", size: 50L * 1024 * 1024),
            Said(TestPhotos.Make("Screenshot 2026-11-17.png", format: "PNG"), p => p.IsScreenshot = true),
            Said(TestPhotos.Make("Shaken.JPG"), p => p.Sharpness = 120), Said(TestPhotos.Make("Sharp.JPG"), p => p.Sharpness = 2400),
            Said(TestPhotos.Make("Unmeasured.JPG"), p => p.Sharpness = null),
            TestPhotos.Make("IMG-WA0001.jpg", folder: Path.Combine(_root.FullName, "WhatsApp", "Media")),
            Said(TestPhotos.Make("B1.JPG", taken: at), p => p.Sharpness = 500), Said(TestPhotos.Make("B2.JPG", taken: at.AddMilliseconds(300)), p => p.Sharpness = 900),
            Said(TestPhotos.Make("B3.JPG", taken: at.AddMilliseconds(600)), p => p.Sharpness = 700),
            Said(TestPhotos.Make("Kept blur.JPG", kept: true), p => p.Sharpness = 10),
            // A picture saved twice, and a third copy marked keep: one copy goes, never the one kept nor the one marked.
            TestPhotos.Make("Twice.JPG", contentHash: "SAME", size: 3_000_000), TestPhotos.Make("Twice (1).JPG", contentHash: "SAME", size: 3_000_000),
            TestPhotos.Make("Twice (2).JPG", contentHash: "SAME", size: 3_000_000, kept: true),
        ], ct);

        // Each picture its own: none is a copy or a look-alike of another, but the three saved alike.
        var all = await Repo.GetAllAsync(ct);
        for (var i = 0; i < all.Count; i++)
            if (all[i].ContentHash != "SAME" && !all[i].IsVideo)
                all[i].PerceptualHash = ((ulong)(i + 1) * 0x9E3779B97F4A7C15UL).ToString("X16");
        await Repo.SaveAllAsync(all, ct);

        var report = (await AskAsync(Http.Get("cleanup/categories"), "cleanup/categories", ct)).Json();
        var counts = report.GetProperty("categories").EnumerateArray().ToDictionary(c => c.GetProperty("category").GetString()!, c => c.GetProperty("count").GetInt32());
        await Assert.That((counts["largeVideos"], counts["screenshots"], counts["blurry"], counts["messaging"], counts["bursts"], counts["duplicates"])).IsEqualTo((1, 1, 1, 1, 2, 1));
        var copies = (await AskAsync(Http.Get("cleanup/categories/duplicates"), "cleanup/categories/duplicates", ct)).Json();
        await Assert.That(copies.EnumerateArray().Single().GetProperty("fileName").GetString()).IsNotEqualTo("Twice (2).JPG");
        await Assert.That(report.GetProperty("categories")[0].GetProperty("category").GetString()).IsEqualTo("largeVideos");
        // A computer has no phone's storage to say; the pictures no scan measured are counted, for Blurry to say so.
        await Assert.That((report.GetProperty("storageTotal").ValueKind, report.GetProperty("unmeasured").GetInt32())).IsEqualTo((JsonValueKind.Null, 5));

        var burst = (await AskAsync(Http.Get("cleanup/categories/bursts"), "cleanup/categories/bursts", ct)).Json()[0];
        var keeper = burst.GetProperty("shots").EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == burst.GetProperty("keeper").GetGuid());
        await Assert.That(keeper.GetProperty("fileName").GetString()).IsEqualTo("B2.JPG");
        var blurry = (await AskAsync(Http.Get("cleanup/categories/blurry"), "cleanup/categories/blurry", ct)).Json();
        await Assert.That(blurry.EnumerateArray().Select(p => p.GetProperty("fileName").GetString()!)).IsEquivalentTo(["Shaken.JPG"]);
    }

    [Test]
    public async Task What_Is_Deleted_Goes_Into_PhotoSense_Removed_As_One_Change_Which_Undo_Puts_Back(CancellationToken ct)
    {
        var shot = await FileAsync(Path.Combine("Desktop", "Screenshot 1.png"), ct, p => p.IsScreenshot = true);
        var other = await FileAsync(Path.Combine("Desktop", "Screenshot 2.png"), ct, p => p.IsScreenshot = true);

        var done = (await AskAsync(Http.Post("cleanup/categories/delete", JsonSerializer.Serialize(new { ids = new[] { shot.Id.Value, other.Id.Value } })), "cleanup/categories/delete", ct)).Json();
        await Assert.That((done.GetProperty("deleted").GetInt32(), done.GetProperty("skipped").GetInt32())).IsEqualTo((2, 0));
        await Assert.That(File.Exists(Path.Combine(_root.FullName, "_PhotoSense_Removed", "Desktop", "Screenshot 1.png"))).IsTrue();
        await Assert.That(((MemoryAudit)_api.Services.GetRequiredService<IAuditRepository>()).Entries.Single().Action).IsEqualTo("RemoveChosen");

        var undo = await _api.SendAsync(Http.Post($"organize/undo/{done.GetProperty("batchId").GetGuid()}").FromClient(), ct);
        await Assert.That(undo.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((File.Exists(shot.SourcePath), File.Exists(other.SourcePath))).IsEqualTo((true, true));
    }

    [Test]
    public async Task Bursts_Are_Asked_For_By_Burst(CancellationToken ct)
    {
        var categories = _api.Services.GetRequiredService<PhotoSense.Application.Removal.CleanUpCategories>();
        await Assert.ThrowsAsync<ArgumentException>(async () => await categories.PhotosAsync(PhotoSense.Application.Removal.CleanUpCategory.Bursts, ct));
    }

    private static Photo Said(Photo photo, Action<Photo> said)
    {
        said(photo);
        return photo;
    }
}
