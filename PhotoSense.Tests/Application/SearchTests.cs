using System.Net;
using System.Security.Cryptography;
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
using PhotoSense.Domain.Matching;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Infrastructure.Library;
using PhotoSense.Infrastructure.Transfer;
using PhotoSense.Tests.Endpoints;

namespace PhotoSense.Tests.Application;

/// <summary>A search split into what is in the photo, places, a month and a year; and labels matched by their words.</summary>
public class SearchQueryTests
{
    private static readonly string[] Places = ["Glacier National Park", "Butte", "Montana"];

    private static IEnumerable<(SearchPartKind, string)> Parts(string text) => SearchQuery.Parse(text, Places).Parts.Select(p => (p.Kind, p.Text));

    [Test]
    public async Task Places_Come_First_The_Longest_Name_Then_Months_And_Years_And_The_Rest_Less_The_Little_Words(CancellationToken ct)
    {
        await Assert.That(Parts("dog beach glacier national park July 2023")).IsEquivalentTo(
            [(SearchPartKind.InPhoto, "dog"), (SearchPartKind.InPhoto, "beach"), (SearchPartKind.Place, "glacier national park"), (SearchPartKind.Month, "July"), (SearchPartKind.Year, "2023")],
            CollectionOrdering.Matching);
        var query = SearchQuery.Parse("photos of dogs in Butte, jul", Places);
        await Assert.That((string.Join(",", query.Words), string.Join(",", query.Places), query.Month, query.Year)).IsEqualTo(("dogs", "butte", (int?)7, (int?)null));
        await Assert.That(Parts("1899 2101 May")).IsEquivalentTo([(SearchPartKind.InPhoto, "1899"), (SearchPartKind.InPhoto, "2101"), (SearchPartKind.Month, "May")], CollectionOrdering.Matching);
        await Assert.That(SearchQuery.Parse("cat Butte", []).Places).IsEmpty();
        await Assert.That(SearchQuery.Parse("the of", Places).Parts).IsEmpty();
        await Assert.That(SearchQuery.Parse("sunset over mountains", []).Words).IsEquivalentTo(["sunset", "mountains"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_Word_Matches_A_Label_One_Or_Many_Or_A_Word_Of_A_Longer_Label(CancellationToken ct)
    {
        string[] labels = ["golden retriever", "cats"];
        await Assert.That((SearchQuery.Names(labels, "retriever"), SearchQuery.Names(labels, "retrievers"), SearchQuery.Names(labels, "cat"), SearchQuery.Names(labels, "cats"), SearchQuery.Names(labels, "dog")))
            .IsEqualTo((true, true, true, true, false));
        await Assert.That(SearchQuery.Names(["dog"], "dogs")).IsTrue();
    }
}

/// <summary>Search over what the scans recorded: by date and place on any plan, by what is in a photo on Pro.</summary>
public class PhotoSearchTests
{
    private readonly InMemoryPhotoRepository _repo = new();
    private readonly Mock<IPlaceNameResolver> _places = new();
    private readonly FakeAppStore _app = new();

    public PhotoSearchTests()
    {
        _places.Setup(p => p.Locate(It.IsAny<double>(), It.Is<double>(x => x > 0))).Returns(new PlaceMatch("Butte", "Montana", "US", 46, -112.5));
        _places.Setup(p => p.Locate(It.IsAny<double>(), It.Is<double>(x => x < 0))).Returns(new PlaceMatch("West Glacier", "Montana", "US", 48.5, -114, Landmark: "Glacier National Park"));
    }

    private PhotoSearch Search => new(_repo, _places.Object, TestPlans.Subscription(new PlanParts { App = _app }));

    private async Task AddAsync(string name, DateTime taken, double? longitude = null, string[]? labels = null, string? live = null)
    {
        var photo = TestPhotos.Make(name, taken: taken);
        photo.Latitude = longitude is null ? null : 46;
        photo.Longitude = longitude;
        photo.Labels = labels;
        photo.LivePhotoId = live;
        await _repo.AddOrUpdateAsync(photo, CancellationToken.None);
    }

    private static string[] Names(SearchResult found) => [.. found.Photos.Select(p => p.FileName)];

    [Test]
    public async Task Date_And_Place_Are_Searched_On_Any_Plan_Latest_First(CancellationToken ct)
    {
        await AddAsync("IMG_1.HEIC", new DateTime(2023, 7, 4), longitude: -1, labels: ["dog"]);
        await AddAsync("IMG_2.HEIC", new DateTime(2023, 7, 9), longitude: 1, labels: ["beach"]);
        await AddAsync("IMG_3.HEIC", new DateTime(2023, 3, 1), longitude: 1);
        await AddAsync("IMG_4.HEIC", new DateTime(2024, 7, 1));
        await AddAsync("IMG_4.MOV", new DateTime(2024, 7, 1), live: "LIVE");

        await Assert.That(Names(await Search.SearchAsync("2023", ct))).IsEquivalentTo(["IMG_2.HEIC", "IMG_1.HEIC", "IMG_3.HEIC"], CollectionOrdering.Matching);
        await Assert.That(Names(await Search.SearchAsync("july", ct))).IsEquivalentTo(["IMG_4.HEIC", "IMG_2.HEIC", "IMG_1.HEIC"], CollectionOrdering.Matching);
        await Assert.That(Names(await Search.SearchAsync("Glacier National Park", ct))).IsEquivalentTo(["IMG_1.HEIC"]);
        await Assert.That(Names(await Search.SearchAsync("montana jul 2023", ct))).IsEquivalentTo(["IMG_2.HEIC", "IMG_1.HEIC"], CollectionOrdering.Matching);
        // Taken nowhere with a name, and with no date but the file's own: found by the file's date.
        await AddAsync("IMG_5.HEIC", new DateTime(2023, 1, 1), longitude: 0);
        await _repo.AddOrUpdateAsync(new Photo { SourcePath = "/photos/IMG_6.HEIC", FileName = "IMG_6.HEIC", FileModifiedUtc = new DateTime(2022, 5, 1), Latitude = 46 }, ct);
        await Assert.That(Names(await Search.SearchAsync("2022", ct)).Single()).IsEqualTo("IMG_6.HEIC");
        await Assert.That(Names(await Search.SearchAsync("Butte 2023", ct))).IsEquivalentTo(["IMG_2.HEIC", "IMG_3.HEIC"], CollectionOrdering.Matching);
        var nothing = await Search.SearchAsync("the of", ct);
        await Assert.That((nothing.Total, nothing.ProNeeded, nothing.Videos)).IsEqualTo((0, false, 0));
        // A video of its own is counted as one, a Live Photo's not at all.
        await _repo.AddOrUpdateAsync(TestPhotos.Make("IMG_7.MOV", taken: new DateTime(2024, 7, 2)), ct);
        var withVideo = await Search.SearchAsync("2024", ct);
        await Assert.That((withVideo.Total, withVideo.Videos)).IsEqualTo((2, 1));
    }

    [Test]
    public async Task What_Is_In_A_Photo_Is_Searched_On_Pro_And_On_The_Free_Plan_Says_So_Matching_Date_And_Place_Alone(CancellationToken ct)
    {
        await AddAsync("IMG_1.HEIC", new DateTime(2023, 7, 4), labels: ["dog", "grass"]);
        await AddAsync("IMG_2.HEIC", new DateTime(2023, 7, 9), labels: ["beach"]);
        await AddAsync("IMG_3.HEIC", new DateTime(2023, 7, 10));

        var free = await Search.SearchAsync("dogs 2023", ct);
        await Assert.That((free.ProNeeded, free.Total)).IsEqualTo((true, 3));
        var onlyWords = await Search.SearchAsync("dog", ct);
        await Assert.That((onlyWords.ProNeeded, onlyWords.Total)).IsEqualTo((true, 0));

        _app.Purchases.Add(new StorePurchase(PlanProducts.Lifetime, TestClock.Day, null, Trial: false));
        var pro = await Search.SearchAsync("dogs 2023", ct);
        await Assert.That((pro.ProNeeded, Names(pro).Single())).IsEqualTo((false, "IMG_1.HEIC"));
        await Assert.That(Names(await Search.SearchAsync("dog grass", ct)).Single()).IsEqualTo("IMG_1.HEIC");
    }
}

/// <summary>Search, favorites and sharing a copy through the phone's own endpoints, with its labels read as the scan reads each picture.</summary>
public sealed class ViewerOnAPhoneTests : IDisposable
{
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("photosense-viewer-");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "photosense-viewer-library-" + Guid.NewGuid().ToString("N"), "Photo Library");
    private readonly FakePhotoLibrary _library = new();
    private readonly FakeAppStore _app = new();
    private readonly FakeShareSheet _share = new();
    private readonly LabellingAnalyzer _analyzer = new();
    private readonly ServiceProvider _phone;
    private readonly ApiRouter _router;

    public ViewerOnAPhoneTests()
    {
        _phone = new ServiceCollection()
            .AddPhotoSensePhone(_library, new FakePhotoAccess(), _analyzer, In("Documents"), In("Caches"), new FakeAdvertiser(), _app, new FakeCodeScanner(),
                new FakeComputerFinder(), new FakeDevice(), new FakeNotifier(), _share, new FakeVaultKeys(), new FakeUnlocker(), new FakeWidgets(), new MemoryLocationSecrets(), new FakeWebSignIn(), "Jamie's iPhone", _root, new TcpTransferListener(0))
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

    private string PathOf(string name) => Path.Combine(_root, name);

    [Test]
    public async Task A_Phone_Labels_Each_Picture_As_It_Scans_Once_And_Search_Finds_Them_On_Pro(CancellationToken ct)
    {
        var dog = MediaSamples.Picture("DOG");
        _library.Add("AAAAAAAA-1", (LibraryFileKind.Photo, "IMG_0001.JPG", dog));
        _library.Add("BBBBBBBB-2", (LibraryFileKind.Photo, "IMG_0002.JPG", MediaSamples.Picture("BEACH")));
        _library.Add("CCCCCCCC-3", (LibraryFileKind.Video, "IMG_0003.MOV", MediaSamples.Movie(null)));
        // Scanned before the phone labelled pictures: read once more when it does.
        _analyzer.Labelling = false;
        await ScanAsync(ct);
        _analyzer.Labelling = true;
        _analyzer.Labels[Convert.ToHexString(SHA256.HashData(dog))] = ["dog", "grass"];
        await ScanAsync(ct);
        var repo = _phone.GetRequiredService<IPhotoRepository>();
        await Assert.That((await repo.GetByPathAsync(PathOf("IMG_0001.JPG"), ct))!.Labels).IsEquivalentTo(["dog", "grass"]);
        await Assert.That((await repo.GetByPathAsync(PathOf("IMG_0002.JPG"), ct))!.Labels).IsEmpty();
        var analyzed = _analyzer.Analyzed;
        await ScanAsync(ct);
        await Assert.That(_analyzer.Analyzed).IsEqualTo(analyzed);

        var free = await PhoneAsync("GET", "search?q=dog%202024", ct);
        // The two pictures of 2024; the sample video was taken in 2023.
        await Assert.That((free.GetProperty("proNeeded").GetBoolean(), free.GetProperty("total").GetInt32(), free.GetProperty("videos").GetInt32())).IsEqualTo((true, 2, 0));
        await Assert.That(free.GetProperty("parts").EnumerateArray().Select(p => p.GetProperty("kind").GetString()!)).IsEquivalentTo(["inPhoto", "year"], CollectionOrdering.Matching);
        _app.Purchases.Add(new StorePurchase(PlanProducts.Lifetime, TestClock.Day, null, Trial: false));
        var pro = await PhoneAsync("GET", "search?q=dogs", ct);
        await Assert.That(pro.GetProperty("photos").EnumerateArray().Select(p => p.GetProperty("fileName").GetString()!)).IsEquivalentTo(["IMG_0001.JPG"]);
        await Assert.That((await PhoneAsync("GET", "search", ct)).GetProperty("total").GetInt32()).IsEqualTo(0);
    }

    [Test]
    public async Task The_Viewer_Says_A_Pictures_Camera_And_Favorite_Mark_And_Marks_It(CancellationToken ct)
    {
        _library.Add("AAAAAAAA-1", (LibraryFileKind.Photo, "IMG_0001.JPG", MediaSamples.Picture("ONE")));
        _library.Add("BBBBBBBB-2", (LibraryFileKind.Photo, "IMG_0002.JPG", MediaSamples.Picture("TWO")));
        await ScanAsync(ct);
        var path = Uri.EscapeDataString(PathOf("IMG_0001.JPG"));
        var details = await PhoneAsync("GET", $"viewer/details?path={path}", ct);
        await Assert.That(details.GetProperty("favorite").GetBoolean()).IsFalse();
        var marked = await PhoneAsync("POST", "viewer/favorite", ct, new { path = PathOf("IMG_0001.JPG"), favorite = true });
        await Assert.That(marked.GetProperty("favorite").GetBoolean()).IsTrue();
        await Assert.That((await PhoneAsync("GET", $"viewer/details?path={path}", ct)).GetProperty("favorite").GetBoolean()).IsTrue();
        // One the scan has no record of has no camera to say.
        var repo = _phone.GetRequiredService<IPhotoRepository>();
        await repo.DeleteAsync((await repo.GetByPathAsync(PathOf("IMG_0002.JPG"), ct))!.Id, ct);
        var unread = Uri.EscapeDataString(PathOf("IMG_0002.JPG"));
        await Assert.That((await PhoneAsync("GET", $"viewer/details?path={unread}", ct)).GetProperty("camera").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That((await PhoneAsync("POST", "viewer/favorite", ct, new { path = PathOf("IMG_0002.JPG"), favorite = true })).GetProperty("camera").ValueKind).IsEqualTo(JsonValueKind.Null);
        _library.Refuse = "You chose not to let PhotoSense change it.";
        await Assert.That(await AskAsync("POST", "viewer/favorite", ct, new { path = PathOf("IMG_0001.JPG"), favorite = false })).IsEqualTo((HttpStatusCode.Conflict, "You chose not to let PhotoSense change it."));
        await Assert.That(await AskAsync("POST", "viewer/favorite", ct, new { path = PathOf("IMG_0404.JPG"), favorite = true })).IsEqualTo((HttpStatusCode.Conflict, "IMG_0404.JPG is no longer in the library."));
        await Assert.That(await AskAsync("GET", "viewer/details", ct)).IsEqualTo((HttpStatusCode.BadRequest, "Say which picture or video."));
        await Assert.That(await AskAsync("POST", "viewer/favorite", ct, new { path = "" })).IsEqualTo((HttpStatusCode.BadRequest, "Say which picture or video."));
    }

    [Test]
    public async Task A_Copy_Without_Its_Place_Or_Camera_Is_Handed_To_The_Share_Sheet(CancellationToken ct)
    {
        _library.Add("AAAAAAAA-1", (LibraryFileKind.Photo, "IMG_0001.JPG", MediaSamples.Picture("ONE")));
        var (status, _) = await AskAsync("POST", "share/photo", ct, new { path = PathOf("IMG_0001.JPG"), removeLocation = true, removeCamera = true });
        await Assert.That(status).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(_library.Copied.Single()).IsEqualTo(("AAAAAAAA-1", LibraryFileKind.Photo, true, true));
        await Assert.That(Path.GetFileName(_share.Files.Single().File)).IsEqualTo("IMG_0001.JPG");

        _share.Fail = "The share sheet could not open.";
        await Assert.That(await AskAsync("POST", "share/photo", ct, new { path = PathOf("IMG_0001.JPG") })).IsEqualTo((HttpStatusCode.Conflict, "The share sheet could not open."));
        await Assert.That(await AskAsync("POST", "share/photo", ct, new { path = PathOf("IMG_0404.JPG") })).IsEqualTo((HttpStatusCode.Conflict, "IMG_0404.JPG is no longer in the library."));
        await Assert.That(await AskAsync("POST", "share/photo", ct)).IsEqualTo((HttpStatusCode.BadRequest, "Say which picture or video."));
    }

    [Test]
    public async Task Only_A_Phone_Searches_Marks_Favorites_And_Shares_And_Only_The_Page_May_Ask(CancellationToken ct)
    {
        await Assert.That((await AskAsync("GET", "search?q=dog", ct, asPage: false)).Status).IsEqualTo(HttpStatusCode.Unauthorized);
        var computer = new TestApi();
        foreach (var (method, path) in new[] { ("GET", "search"), ("GET", "viewer/details"), ("POST", "viewer/favorite"), ("POST", "share/photo") })
        {
            var refused = await computer.SendAsync((method == "GET" ? Http.Get(path) : Http.Post(path)).FromClient(), ct, path);
            await Assert.That((refused.StatusCode, refused.Text())).IsEqualTo((HttpStatusCode.NotFound, "Only a phone has this."));
        }
    }

    [Test]
    public async Task On_Android_Favorites_Are_Read_And_Marked_And_A_Heic_Is_Shared_As_A_Jpeg(CancellationToken ct)
    {
        var store = new FakeMediaStore();
        store.Add("1", "DCIM/Camera/", "IMG_1.heic", [1, 2, 3]);
        store.Add("2", "DCIM/Camera/", "VID_2.mp4", [4, 5, 6], video: true);
        var phone = Path.Combine(_data.FullName, "Phone");
        var camera = Path.Combine(phone, "DCIM", "Camera");
        var android = new MediaFileSystem(store, phone);
        await Assert.That(await android.FavoritesAsync(ct)).IsEmpty();
        android.SetFavorite(Path.Combine(camera, "IMG_1.heic"), true);
        await Assert.That(await android.FavoritesAsync(ct)).IsEquivalentTo([Path.Combine(camera, "IMG_1.heic")]);
        store.Refuse = "You chose not to.";
        await Assert.That(() => android.SetFavorite(Path.Combine(camera, "IMG_1.heic"), false)).Throws<IOException>();
        await Assert.That(() => android.SetFavorite(Path.Combine(camera, "IMG_9.heic"), false)).Throws<IOException>();
        var copy = await android.CopyForSharingAsync(Path.Combine(camera, "IMG_1.heic"), true, false, ct);
        await Assert.That((Path.GetFileName(copy.Path), copy.MimeType)).IsEqualTo(("IMG_1.jpg", "image/jpeg"));
        await Assert.That((await android.CopyForSharingAsync(Path.Combine(camera, "VID_2.mp4"), true, true, ct)).MimeType).IsEqualTo("video/mp4");
        await Assert.That(async () => await android.CopyForSharingAsync(Path.Combine(camera, "IMG_9.heic"), true, true, ct)).Throws<IOException>();
    }
}

/// <summary>A phone's decoder, Magick.NET standing in for it, that labels each picture as the test says by the SHA-256 of its bytes.</summary>
internal sealed class LabellingAnalyzer : IImageAnalyzer
{
    private readonly MagickImageAnalyzer _decoder = new();

    /// <summary>The labels of each picture, by the hex SHA-256 of its bytes; one not named has none.</summary>
    public Dictionary<string, string[]> Labels { get; } = [];

    /// <summary>Whether it labels pictures, as a phone's does.</summary>
    public bool Labelling { get; set; } = true;

    /// <summary>How many pictures it has read.</summary>
    public int Analyzed { get; private set; }

    /// <inheritdoc />
    public bool LabelsPictures => Labelling;

    /// <inheritdoc />
    public async Task<ImageAnalysis> AnalyzeAsync(Stream imageStream, CancellationToken ct)
    {
        using var copy = new MemoryStream();
        await imageStream.CopyToAsync(copy, ct);
        copy.Position = 0;
        Analyzed++;
        var analysis = await _decoder.AnalyzeAsync(copy, ct);
        return Labelling ? analysis with { Labels = Labels.GetValueOrDefault(Convert.ToHexString(SHA256.HashData(copy.ToArray())), []) } : analysis;
    }

    /// <inheritdoc />
    public Task<byte[]> RenderJpegAsync(Stream imageStream, int maxEdge, CancellationToken ct) => _decoder.RenderJpegAsync(imageStream, maxEdge, ct);
}
