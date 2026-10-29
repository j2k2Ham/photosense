using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Api;
using PhotoSense.Api.About;
using PhotoSense.Api.Hosting;
using PhotoSense.Api.Http;
using PhotoSense.Desktop.Imaging;
using PhotoSense.Infrastructure.Library;
using PhotoSense.Infrastructure.Transfer;

namespace PhotoSense.Tests.Application;

/// <summary>
/// PhotoSense as the phone app runs it, all but the phone's own photo framework and picture decoder: the page's
/// requests answered inside the app, on a photo library that is on no disk.
/// </summary>
// A scan's lines go to the one queue the page reads, which other classes count.
[NotInParallel("ScanLogQueue")]
public sealed class PhotosOnAPhoneTests : IDisposable
{
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("photosense-phone-");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "photosense-phone-library-" + Guid.NewGuid().ToString("N"), "Photo Library");
    private readonly FakePhotoLibrary _library = new();
    private readonly FakePhotoAccess _access = new() { Current = PhotoSense.Domain.Services.PhotoAccess.Selected };
    private readonly ServiceProvider _services;
    internal FakeAdvertiser Advertiser { get; } = new();
    private readonly ApiRouter _router;

    public PhotosOnAPhoneTests()
    {
        // Magick.NET stands in for the phone's own decoder, which measures pictures the same way.
        _services = new ServiceCollection()
            .AddPhotoSensePhone(_library, _access, new MagickImageAnalyzer(), In("Documents"), In("Caches"), Advertiser, "Jamie's iPhone", _root, new TcpTransferListener(0))
            .AddPhotoSenseApi()
            .AddSingleton(new ProgramInfo("iphone", "1.2.0.57", () => "Third-party notices for PhotoSense for iPhone and iPad"))
            .BuildServiceProvider();
        _router = _services.GetRequiredService<PhotoSenseRoutes>().Router;
    }

    [Test]
    public async Task About_Says_It_Is_The_Phone_App_And_Gives_Its_Documents()
    {
        var about = await JsonAsync("GET", "about");
        await Assert.That(about.GetProperty("program").GetString()).IsEqualTo("iphone");
        await Assert.That(about.GetProperty("version").GetString()).IsEqualTo("1.2.0.57");
        await Assert.That((await AskAsync("GET", "about/privacy")).Text).StartsWith("# Privacy Policy");
        await Assert.That((await AskAsync("GET", "about/notices")).Text).StartsWith("Third-party notices for PhotoSense for iPhone and iPad");
        await Assert.That((await JsonAsync("GET", "library/access")).GetProperty("access").GetString()).IsEqualTo("selected");
        await Assert.That((await JsonAsync("POST", "library/choose-more")).GetProperty("access").GetString()).IsEqualTo("everything");
        await Assert.That(_access.PickersShown).IsEqualTo(1);
        var icon = await _router.AnswerAsync("GET", new Uri("app://photosense/api/about/icon"), [], null);
        await Assert.That(icon.Headers["Content-Type"]).IsEqualTo("image/png");
    }

    [Test]
    public async Task Organize_Puts_Pictures_In_An_Album_Inside_A_Folder_And_Takes_Them_Out_Again_On_Undo()
    {
        _library.Add("AAAAAAAA-1", (LibraryFileKind.Photo, "IMG_0001.JPG", MediaSamples.Picture("BUTTE-1")), (LibraryFileKind.PairedVideo, "IMG_0001.MOV", MediaSamples.Movie("BUTTE-1")));
        _library.Add("BBBBBBBB-2", (LibraryFileKind.Photo, "IMG_0002.JPG", MediaSamples.Picture("BUTTE-2")));
        var listed = await JsonAsync("GET", $"organize/files?root={Uri.EscapeDataString(_root)}");
        var picture = listed.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("name").GetString() == "IMG_0001.JPG").GetProperty("id").GetString();

        var moved = await JsonAsync("POST", "organize/apply", new
        {
            basePath = _root, folderName = "Butte\\2022", direct = false, mode = "move", companions = true, label = "Butte\\2022",
            files = new[] { new { id = picture, name = "IMG_0001.JPG" } },
        });
        await Assert.That((moved.GetProperty("done").GetInt32(), moved.GetProperty("companions").GetInt32())).IsEqualTo((1, 1));
        await Assert.That(_library.InAlbums()["Butte/2022"]).IsEquivalentTo(["AAAAAAAA-1"], CollectionOrdering.Matching);
        var photos = _services.GetRequiredService<PhotoLibraryFileSystem>();
        await Assert.That(photos.FilesIn(Path.Combine(_root, "Butte", "2022")).Count).IsEqualTo(2);

        // A copy is not something a phone's library has: it is refused, and says why.
        var copied = await JsonAsync("POST", "organize/apply", new
        {
            basePath = _root, folderName = "Elsewhere", direct = false, mode = "copy", companions = false, label = "Elsewhere",
            files = new[] { new { id = listed.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("name").GetString() == "IMG_0002.JPG").GetProperty("id").GetString(), name = "IMG_0002.JPG" } },
        });
        await Assert.That(copied.GetRawText()).Contains("it can be moved there, not copied.");

        await JsonAsync("POST", $"organize/undo/{moved.GetProperty("batchId").GetString()}");
        // The album stays, empty: the phone would ask before an app took it away.
        await Assert.That((photos.FilesIn(_root).Count, _library.InAlbums()["Butte/2022"].Length)).IsEqualTo((3, 0));
    }

    [Test]
    public async Task Delete_Permanently_Sends_The_Removed_Pictures_To_Recently_Deleted_After_One_Question_And_Says_So_When_Refused()
    {
        var picture = MediaSamples.Picture("TWICE-1");
        _library.Add("AAAAAAAA-1", (LibraryFileKind.Photo, "IMG_0001.JPG", picture));
        _library.Add("BBBBBBBB-2", (LibraryFileKind.Photo, "IMG_0001.JPG", picture));
        _library.Album([PhotoLibraryFileSystem.RemovedAlbum], "BBBBBBBB-2");
        var removedFolder = Path.Combine(_root, "_PhotoSense_Removed");

        var waiting = await JsonAsync("GET", $"removed?root={Uri.EscapeDataString(_root)}");
        await Assert.That((waiting.GetProperty("files").GetInt32(), waiting.GetProperty("folders")[0].GetProperty("path").GetString())).IsEqualTo((1, removedFolder));

        _library.Refuse = "You chose not to delete these photos.";
        var refused = await JsonAsync("POST", "removed/erase", new { folders = new[] { removedFolder } });
        await Assert.That((refused.GetProperty("erased").GetInt32(), refused.GetProperty("skipped").GetInt32(), refused.GetProperty("problems")[0].GetString())).IsEqualTo((0, 1, "You chose not to delete these photos."));

        var erased = await JsonAsync("POST", "removed/erase", new { folders = new[] { removedFolder } });
        await Assert.That((erased.GetProperty("erased").GetInt32(), _library.RecentlyDeleted.Single(), _library.InAlbums()[PhotoLibraryFileSystem.RemovedAlbum].Length)).IsEqualTo((1, "BBBBBBBB-2", 0));
    }

    public void Dispose()
    {
        _services.Dispose();
        TestFiles.Remove(_data);
        while (PhotoSense.Application.Scanning.InMemoryScanLogSink.TryDequeuePending(out _)) { }
    }

    private string In(string folder) => Path.Combine(_data.FullName, folder);

    private async Task<(int Status, string Text)> AskAsync(string method, string pathAndQuery, object? body = null)
    {
        var answer = await _router.AnswerAsync(method, new Uri("app://photosense/api/" + pathAndQuery), [new("x-photosense-client", "phone")],
            body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body));
        return (answer.Status, Encoding.UTF8.GetString(answer.Body));
    }

    private async Task<JsonElement> JsonAsync(string method, string pathAndQuery, object? body = null)
    {
        var (status, text) = await AskAsync(method, pathAndQuery, body);
        await Assert.That(status / 100).IsEqualTo(2).Because(text);
        return JsonDocument.Parse(text).RootElement;
    }

    [Test]
    public async Task The_Page_Scans_The_Phones_Library_Removes_A_Copy_Into_An_Album_Of_Its_Own_And_Takes_That_Back()
    {
        // A Live Photo, and a copy of it saved again under the same name.
        var picture = MediaSamples.Picture("LIVE-1");
        var video = MediaSamples.Movie("LIVE-1");
        _library.Add("AAAAAAAA-1", (LibraryFileKind.Photo, "IMG_0001.JPG", picture), (LibraryFileKind.PairedVideo, "IMG_0001.MOV", video));
        _library.Add("BBBBBBBB-2", (LibraryFileKind.Photo, "IMG_0001.JPG", picture), (LibraryFileKind.PairedVideo, "IMG_0001.MOV", video));

        // The library is the one folder offered.
        var places = await JsonAsync("GET", "folders");
        await Assert.That(places.GetProperty("folders").EnumerateArray().Single().GetProperty("path").GetString()).IsEqualTo(_root);

        await JsonAsync("POST", "scan/start", new { primaryLocation = _root, recursive = true });
        for (var waited = 0; (await JsonAsync("GET", "scan/status")).GetProperty("completed").ValueKind == JsonValueKind.Null; waited++)
        {
            if (waited == 600) throw new TimeoutException("The scan did not finish.");
            await Task.Delay(50);
        }
        var status = await JsonAsync("GET", "scan/status");
        await Assert.That(status.GetProperty("totalPhotos").GetInt32()).IsEqualTo(4);

        var groups = await JsonAsync("GET", "scan/groups?mode=duplicates&page=1&pageSize=50");
        await Assert.That(groups.GetRawText()).Contains("IMG_0001 (AAAAAAAA)").And.Contains("IMG_0001 (BBBBBBBB)");

        var listed = await JsonAsync("GET", $"organize/files?root={Uri.EscapeDataString(_root)}");
        await Assert.That(listed.GetProperty("files").GetArrayLength()).IsEqualTo(4);

        // The copy goes, with its Live Photo video, into the album of removed pictures, in one change; the person
        // is asked nothing, and Undo takes it back, leaving the album empty.
        var photos = _services.GetRequiredService<PhotoLibraryFileSystem>();
        var removed = await JsonAsync("POST", "photos/bulk/remove-duplicates");
        await Assert.That((removed.GetProperty("removed").GetInt32(), removed.GetProperty("companions").GetInt32(), _library.Changes.Count)).IsEqualTo((1, 1, 1));
        await Assert.That(_library.InAlbums()[PhotoLibraryFileSystem.RemovedAlbum].Length).IsEqualTo(1);
        await Assert.That((photos.FilesIn(_root).Count, photos.FilesIn(Path.Combine(_root, "_PhotoSense_Removed")).Count)).IsEqualTo((2, 2));
        await JsonAsync("POST", $"organize/undo/{removed.GetProperty("batchId").GetString()}");
        await Assert.That((photos.FilesIn(_root).Count, _library.InAlbums()[PhotoLibraryFileSystem.RemovedAlbum].Length, _library.RecentlyDeleted.Count)).IsEqualTo((4, 0, 0));

        // What PhotoSense records is kept where the phone keeps an app's own files, and the previews where it may clear them.
        await Assert.That((File.Exists(Path.Combine(In("Documents"), "photosense.db")), Directory.EnumerateFiles(Path.Combine(In("Caches"), "thumbnails"), "*", SearchOption.AllDirectories).Any()))
            .IsEqualTo((true, true));
    }
}
