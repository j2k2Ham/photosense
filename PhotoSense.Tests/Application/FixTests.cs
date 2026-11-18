using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PhotoSense.Api;
using PhotoSense.Api.About;
using PhotoSense.Api.Hosting;
using PhotoSense.Api.Http;
using PhotoSense.Application.Organizing;
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
/// Fix dates and places on an iPhone, through its whole stack and the page's addresses: what needs a date or a place,
/// the fixes on Pro after the phone's question, what follows them, and Undo. The library is in memory; the scan's
/// records are written by the test.
/// </summary>
public sealed class FixTests : IDisposable
{
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("photosense-fix-");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "photosense-fix-library-" + Guid.NewGuid().ToString("N"), "Photo Library");
    private readonly FakePhotoLibrary _library = new();
    private readonly FakeAppStore _app = new();
    private readonly ServiceProvider _phone;
    private readonly ApiRouter _router;

    public FixTests()
    {
        _phone = new ServiceCollection()
            .AddPhotoSensePhone(_library, new FakePhotoAccess(), new MagickImageAnalyzer(), Path.Combine(_data.FullName, "Documents"), Path.Combine(_data.FullName, "Caches"), new FakeAdvertiser(), _app,
                new FakeCodeScanner(), new FakeComputerFinder(), new FakeDevice(), new FakeNotifier(), new FakeShareSheet(), "Jamie's iPhone", _root, new TcpTransferListener(0))
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

    private IPhotoRepository Repo => _phone.GetRequiredService<IPhotoRepository>();

    private void Pro() => _app.Purchases.Add(new StorePurchase(PlanProducts.Lifetime, TestClock.Day, null, Trial: false));

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

    /// <summary>
    /// A picture in the library and the scan's record of it: when it was taken, where, and whether it is a screenshot.
    /// A Live Photo has its video too, recorded as a video of the same picture.
    /// </summary>
    private async Task<string> PhotoAsync(string id, string name, DateTime? taken, (double, double)? at = null, bool screenshot = false, bool live = false)
    {
        var files = live
            ? new[] { (LibraryFileKind.Photo, name, MediaSamples.Picture(id)), (LibraryFileKind.PairedVideo, Path.ChangeExtension(name, ".MOV"), MediaSamples.Movie(id)) }
            : [(LibraryFileKind.Photo, name, MediaSamples.Picture())];
        _library.Add(id, files);
        var system = _phone.GetRequiredService<IPhotoFileSystem>();
        foreach (var path in system.FilesIn(_root).Where(p => Path.GetFileNameWithoutExtension(p) == Path.GetFileNameWithoutExtension(name)))
        {
            var stored = system.Describe(path)!;
            await Repo.AddOrUpdateAsync(new Photo
            {
                SourcePath = path, FileName = Path.GetFileName(path), FileSizeBytes = stored.Length, FileModifiedUtc = stored.ModifiedUtc, TakenOn = taken,
                Latitude = at?.Item1, Longitude = at?.Item2, IsScreenshot = screenshot, LivePhotoId = live ? id : null,
            }, CancellationToken.None);
        }
        return id;
    }

    private async Task<Photo> RecordAsync(string name) => (await Repo.GetAllAsync(CancellationToken.None)).Single(p => p.FileName == name);

    [Test]
    public async Task Wrong_Dates_Are_Found_Fixed_On_Pro_To_The_Day_Chosen_Keeping_Each_Time_And_Undone(CancellationToken ct)
    {
        await PhotoAsync("W1", "IMG_0001.JPG", new DateTime(2000, 1, 1, 9, 30, 0));
        await PhotoAsync("W2", "IMG_0002.JPG", new DateTime(2000, 1, 1, 10, 0, 0), live: true);
        await PhotoAsync("W3", "IMG_0003.JPG", new DateTime(2027, 5, 1, 8, 0, 0));
        await PhotoAsync("OK", "IMG_0004.JPG", new DateTime(2026, 3, 2, 12, 0, 0));
        await PhotoAsync("ND", "IMG_0005.JPG", null);

        var report = await PhoneAsync("GET", "fixes", ct);
        var dates = report.GetProperty("dates");
        await Assert.That((report.GetProperty("total").GetInt32(), dates.GetProperty("count").GetInt32(), dates.GetProperty("date").GetString(), dates.GetProperty("undo").GetInt32(), dates.GetProperty("photos").GetArrayLength()))
            .IsEqualTo((3, 3, "2000-01-01", 0, 3));
        await Assert.That(report.GetProperty("names").ValueKind).IsEqualTo(JsonValueKind.Null);

        // Free: nothing is asked or changed.
        await Assert.That(await AskAsync("POST", "fixes/dates", ct, new { date = "2026-03-02" })).IsEqualTo((HttpStatusCode.Conflict, "Fixing dates and places is part of Pro."));
        await Assert.That(_library.DetailsChanged.Count).IsEqualTo(0);

        Pro();
        foreach (var body in new object[] { new { date = "March 2nd" }, "not a request" })
            await Assert.That(await AskAsync("POST", "fixes/dates", ct, body)).IsEqualTo((HttpStatusCode.BadRequest, "Choose the date they were taken."));
        var fixedNow = await PhoneAsync("POST", "fixes/dates", ct, new { date = "2026-03-02" });
        await Assert.That(fixedNow.GetProperty("changed").GetInt32()).IsEqualTo(3);
        // One question; a Live Photo once.
        await Assert.That(_library.DetailsChanged.Single().Select(c => (c.Id, c.Taken)).OrderBy(c => c.Id)).IsEquivalentTo(
            [("W1", (DateTime?)new DateTime(2026, 3, 2, 9, 30, 0)), ("W2", new DateTime(2026, 3, 2, 10, 0, 0)), ("W3", new DateTime(2026, 3, 2, 8, 0, 0))], CollectionOrdering.Matching);
        await Assert.That((await RecordAsync("IMG_0002.MOV")).TakenOn).IsEqualTo(new DateTime(2026, 3, 2, 10, 0, 0));
        var after = (await PhoneAsync("GET", "fixes", ct)).GetProperty("dates");
        await Assert.That((after.GetProperty("count").GetInt32(), after.GetProperty("undo").GetInt32(), after.GetProperty("date").ValueKind)).IsEqualTo((0, 3, JsonValueKind.Null));
        // Library reads it afresh, from the scan's record.
        var listed = await PhoneAsync("GET", $"organize/files?root={Uri.EscapeDataString(_root)}", ct);
        await Assert.That(listed.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("name").GetString() == "IMG_0001.JPG").GetProperty("date").GetString()).StartsWith("2026-03-02T09:30:00");

        await Assert.That((await PhoneAsync("POST", "fixes/undo", ct, new { kind = "dates" })).GetProperty("changed").GetInt32()).IsEqualTo(3);
        await Assert.That(_library.DetailsChanged[1].Single(c => c.Id == "W3").Taken).IsEqualTo(new DateTime(2027, 5, 1, 8, 0, 0));
        await Assert.That((await RecordAsync("IMG_0001.JPG")).TakenOn).IsEqualTo(new DateTime(2000, 1, 1, 9, 30, 0));
        await Assert.That((await PhoneAsync("GET", "fixes", ct)).GetProperty("dates").GetProperty("count").GetInt32()).IsEqualTo(3);
        // Nothing left to undo.
        await Assert.That((await PhoneAsync("POST", "fixes/undo", ct, new { kind = "Dates" })).GetProperty("changed").GetInt32()).IsEqualTo(0);
        foreach (var body in new object[] { new { kind = "sideways" }, "not a request" })
            await Assert.That(await AskAsync("POST", "fixes/undo", ct, body)).IsEqualTo((HttpStatusCode.BadRequest, "Say which fix to undo."));
    }

    [Test]
    public async Task A_Photo_With_No_Place_Gets_That_Of_The_One_Nearest_In_Time_And_A_Date_And_A_Place_Fixed_Both_Stand(CancellationToken ct)
    {
        Pro();
        var glacier = (48.6831, -113.8003);
        await PhotoAsync("P1", "IMG_0001.JPG", new DateTime(2026, 7, 18, 9, 0, 0), glacier);
        await PhotoAsync("N1", "IMG_0002.JPG", new DateTime(2026, 7, 18, 9, 10, 0));
        await PhotoAsync("SS", "IMG_0003.PNG", new DateTime(2026, 7, 18, 9, 12, 0), screenshot: true);
        await PhotoAsync("FAR", "IMG_0004.JPG", new DateTime(2026, 7, 18, 13, 0, 0));
        // A wrong date, which once fixed is taken near a photo with a place.
        await PhotoAsync("W1", "IMG_0005.JPG", new DateTime(2000, 1, 1, 9, 5, 0));
        // A record of a file no longer in the library is left out.
        await Repo.AddOrUpdateAsync(new Photo { SourcePath = Path.Combine(_root, "GONE.JPG"), FileName = "GONE.JPG", TakenOn = new DateTime(2026, 7, 18, 9, 20, 0) }, ct);

        var places = (await PhoneAsync("GET", "fixes", ct)).GetProperty("places");
        await Assert.That((places.GetProperty("count").GetInt32(), places.GetProperty("place").GetString(), places.GetProperty("placeCount").GetInt32()))
            .IsEqualTo((1, "Glacier National Park", 1));

        // The phone says no: nothing is kept.
        _library.Refuse = "You chose not to let PhotoSense change these photos.";
        await Assert.That(await AskAsync("POST", "fixes/places", ct)).IsEqualTo((HttpStatusCode.Conflict, "You chose not to let PhotoSense change these photos."));
        await Assert.That((await PhoneAsync("GET", "fixes", ct)).GetProperty("places").GetProperty("undo").GetInt32()).IsEqualTo(0);

        await Assert.That((await PhoneAsync("POST", "fixes/places", ct)).GetProperty("changed").GetInt32()).IsEqualTo(1);
        var change = _library.DetailsChanged.Single().Single();
        await Assert.That((change.Id, change.Taken, change.Place)).IsEqualTo(("N1", (DateTime?)null, new PlaceChange(48.6831, -113.8003)));
        await Assert.That(((await RecordAsync("IMG_0002.JPG")).Latitude, (await RecordAsync("IMG_0004.JPG")).Latitude)).IsEqualTo(((double?)48.6831, (double?)null));

        // The wrong date fixed, it is near the photo with a place, and gets the place too: both fixes stand.
        await PhoneAsync("POST", "fixes/dates", ct, new { date = "2026-07-18" });
        await Assert.That((await PhoneAsync("POST", "fixes/places", ct)).GetProperty("changed").GetInt32()).IsEqualTo(1);
        var fixes = await _phone.GetRequiredService<IDetailsFixStore>().FixedAsync(ct);
        await Assert.That(fixes["W1"]).IsEqualTo(new FixedDetails(new DateTime(2026, 7, 18, 9, 5, 0), true, 48.6831, -113.8003));

        // Undone, the place goes and the date stays; then the date goes too, and nothing is in force.
        await PhoneAsync("POST", "fixes/undo", ct, new { kind = "places" });
        await Assert.That((await _phone.GetRequiredService<IDetailsFixStore>().FixedAsync(ct))["W1"].Taken).IsEqualTo(new DateTime(2026, 7, 18, 9, 5, 0));
        var place = _library.DetailsChanged.Last().Single();
        await Assert.That((place.Id, place.Place)).IsEqualTo(("W1", new PlaceChange(null, null)));
        await Assert.That((await RecordAsync("IMG_0005.JPG")).Latitude).IsNull();
        await Assert.That((await _phone.GetRequiredService<IDetailsFixStore>().FixedAsync(ct))["W1"].HasPlace).IsFalse();
        await PhoneAsync("POST", "fixes/undo", ct, new { kind = "dates" });
        await Assert.That((await _phone.GetRequiredService<IDetailsFixStore>().FixedAsync(ct)).ContainsKey("W1")).IsFalse();

        // The other way round: the date undone, the place stays; then the place goes.
        await PhoneAsync("POST", "fixes/dates", ct, new { date = "2026-07-18" });
        await PhoneAsync("POST", "fixes/places", ct);
        await PhoneAsync("POST", "fixes/undo", ct, new { kind = "dates" });
        await Assert.That((await _phone.GetRequiredService<IDetailsFixStore>().FixedAsync(ct))["W1"]).IsEqualTo(new FixedDetails(null, true, 48.6831, -113.8003));
        await PhoneAsync("POST", "fixes/undo", ct, new { kind = "places" });
        await Assert.That((await _phone.GetRequiredService<IDetailsFixStore>().FixedAsync(ct)).ContainsKey("W1")).IsFalse();
    }

    [Test]
    public async Task A_Date_Typed_That_Is_Still_A_Clocks_Is_Fixed_Again_And_The_Place_Said_Is_A_Town_Or_None(CancellationToken ct)
    {
        Pro();
        await PhotoAsync("W1", "IMG_0001.JPG", new DateTime(1970, 1, 1, 7, 0, 0));
        await PhoneAsync("POST", "fixes/dates", ct, new { date = "2000-01-01" });
        await Assert.That((await PhoneAsync("GET", "fixes", ct)).GetProperty("dates").GetProperty("count").GetInt32()).IsEqualTo(1);
        await PhoneAsync("POST", "fixes/dates", ct, new { date = "2026-03-02" });
        await Assert.That((await _phone.GetRequiredService<IDetailsFixStore>().FixedAsync(ct))["W1"]).IsEqualTo(new FixedDetails(new DateTime(2026, 3, 2, 7, 0, 0), false, null, null));
        // Undone, it goes back to the date it had before the last fix.
        await PhoneAsync("POST", "fixes/undo", ct, new { kind = "dates" });
        await Assert.That((await RecordAsync("IMG_0001.JPG")).TakenOn).IsEqualTo(new DateTime(2000, 1, 1, 7, 0, 0));

        // A town when no landmark is near; nothing when no town is.
        await PhotoAsync("BU", "IMG_0002.JPG", new DateTime(2026, 7, 19, 10, 0, 0), (46.0038, -112.5348));
        await PhotoAsync("N1", "IMG_0003.JPG", new DateTime(2026, 7, 19, 10, 5, 0));
        await PhotoAsync("SEA", "IMG_0004.JPG", new DateTime(2026, 7, 20, 10, 0, 0), (0.0, -160.0));
        await PhotoAsync("N2", "IMG_0005.JPG", new DateTime(2026, 7, 20, 10, 5, 0));
        var places = (await PhoneAsync("GET", "fixes", ct)).GetProperty("places");
        await Assert.That((places.GetProperty("count").GetInt32(), places.GetProperty("place").GetString(), places.GetProperty("placeCount").GetInt32())).IsEqualTo((2, "Butte", 1));
    }

    [Test]
    public async Task A_Fix_Gone_From_The_Store_Is_Still_Undone_And_Nothing_Is_Asked_Of_Photos_The_Library_No_Longer_Has(CancellationToken ct)
    {
        Pro();
        // Only a record of a file the library no longer has needs a date: nothing is asked.
        await Repo.AddOrUpdateAsync(new Photo { SourcePath = Path.Combine(_root, "GONE.JPG"), FileName = "GONE.JPG", TakenOn = new DateTime(2000, 1, 1) }, ct);
        await Assert.That((await PhoneAsync("POST", "fixes/dates", ct, new { date = "2025-12-25" })).GetProperty("changed").GetInt32()).IsEqualTo(0);
        await Assert.That(_library.DetailsChanged.Count).IsEqualTo(0);

        await PhotoAsync("W1", "IMG_0001.JPG", new DateTime(1970, 1, 1, 0, 0, 1));
        await Assert.That((await PhoneAsync("POST", "fixes/dates", ct, new { date = "2025-12-25" })).GetProperty("changed").GetInt32()).IsEqualTo(1);
        await _phone.GetRequiredService<IDetailsFixStore>().SaveFixedAsync(new Dictionary<string, FixedDetails?> { ["W1"] = null }, ct);
        await Assert.That((await PhoneAsync("POST", "fixes/undo", ct, new { kind = "dates" })).GetProperty("changed").GetInt32()).IsEqualTo(1);
    }

    [Test]
    public async Task A_Fix_Holds_Whatever_The_File_Says_When_Read_Again(CancellationToken ct)
    {
        var details = new Mock<IPhotoDetails>();
        details.Setup(d => d.IdOf("/Photo Library/IMG_1.JPG")).Returns("A");
        var store = new MemoryDetailsFixStore();
        await store.SaveFixedAsync(new Dictionary<string, FixedDetails?> { ["A"] = new FixedDetails(new DateTime(2026, 3, 2, 9, 0, 0), true, 48.7, -113.8) }, ct);
        var inner = new Mock<IPhotoMetadataExtractor>();
        inner.Setup(i => i.ExtractAsync(It.IsAny<Photo>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback((Photo p, Stream _, CancellationToken _) => { p.TakenOn = new DateTime(2000, 1, 1); p.Latitude = null; })
            .Returns(Task.CompletedTask);
        var extractor = new FixedPhotoMetadata(inner.Object, details.Object, store);
        var photo = new Photo { SourcePath = "/Photo Library/IMG_1.JPG", FileName = "IMG_1.JPG" };
        await extractor.ExtractAsync(photo, Stream.Null, ct);
        await Assert.That((photo.TakenOn, photo.Latitude, photo.Longitude)).IsEqualTo(((DateTime?)new DateTime(2026, 3, 2, 9, 0, 0), (double?)48.7, (double?)-113.8));
        var other = new Photo { SourcePath = "/Photo Library/IMG_2.JPG", FileName = "IMG_2.JPG" };
        await extractor.ExtractAsync(other, Stream.Null, ct);
        await Assert.That(other.TakenOn).IsEqualTo(new DateTime(2000, 1, 1));

        var reader = new Mock<IMediaDetailsReader>();
        reader.Setup(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new MediaDetails(new DateTime(2000, 1, 1), 4, 3, null, 1, 2));
        var reading = new FixedMediaDetails(reader.Object, details.Object, store);
        await Assert.That(await reading.ReadAsync("/Photo Library/IMG_1.JPG", ct)).IsEqualTo(new MediaDetails(new DateTime(2026, 3, 2, 9, 0, 0), 4, 3, null, 48.7, -113.8));
        await Assert.That((await reading.ReadAsync("/Photo Library/IMG_2.JPG", ct)).TakenOn).IsEqualTo(new DateTime(2000, 1, 1));
        // A place only leaves the date the file says.
        await store.SaveFixedAsync(new Dictionary<string, FixedDetails?> { ["A"] = new FixedDetails(null, true, 48.7, -113.8) }, ct);
        await Assert.That((await reading.ReadAsync("/Photo Library/IMG_1.JPG", ct)).TakenOn).IsEqualTo(new DateTime(2000, 1, 1));
        // A date only leaves the place the file says; a fix known for a photo not under it changes nothing.
        await store.SaveFixedAsync(new Dictionary<string, FixedDetails?> { ["A"] = new FixedDetails(new DateTime(2026, 3, 2, 9, 0, 0), false, null, null) }, ct);
        await Assert.That((await reading.ReadAsync("/Photo Library/IMG_1.JPG", ct)).Latitude).IsEqualTo(1);
        details.Setup(d => d.IdOf("/Photo Library/IMG_2.JPG")).Returns("B");
        await Assert.That((await reading.ReadAsync("/Photo Library/IMG_2.JPG", ct)).TakenOn).IsEqualTo(new DateTime(2000, 1, 1));
    }

    [Test]
    public async Task Only_A_Phone_Fixes_Dates_And_Places_And_Only_The_Page_May_Ask(CancellationToken ct)
    {
        var computer = await new TestApi().SendAsync(Http.Get("fixes").FromClient(), ct, "fixes");
        await Assert.That((computer.StatusCode, computer.Text())).IsEqualTo((HttpStatusCode.NotFound, "Only a phone fixes dates and places."));
        foreach (var (method, path) in new[] { ("GET", "fixes"), ("POST", "fixes/dates"), ("POST", "fixes/places"), ("POST", "fixes/undo"), ("POST", "fixes/names") })
            await Assert.That((await _router.AnswerAsync(method, new Uri("app://photosense/api/" + path), [], null, ct)).Status).IsEqualTo((int)HttpStatusCode.Unauthorized);
    }
}
