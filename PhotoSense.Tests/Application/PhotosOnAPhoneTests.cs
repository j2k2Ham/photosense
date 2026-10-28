using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Api;
using PhotoSense.Api.Hosting;
using PhotoSense.Api.Http;
using PhotoSense.Desktop.Imaging;
using PhotoSense.Infrastructure.Library;

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
    private readonly ServiceProvider _services;
    private readonly ApiRouter _router;

    public PhotosOnAPhoneTests()
    {
        // Magick.NET stands in for the phone's own decoder, which measures pictures the same way.
        _services = new ServiceCollection()
            .AddPhotoSensePhone(_library, new MagickImageAnalyzer(), In("Documents"), In("Caches"), _root)
            .AddPhotoSenseApi()
            .BuildServiceProvider();
        _router = _services.GetRequiredService<PhotoSenseRoutes>().Router;
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
    public async Task The_Page_Scans_The_Phones_Library_Finds_The_Copies_In_It_And_Changes_Nothing_There()
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

        // Taking a copy out is refused before anything moves, and says why.
        var (removed, said) = await AskAsync("POST", "photos/bulk/remove-duplicates");
        await Assert.That(said).Contains(PhotoLibraryFileSystem.ReadOnly);
        await Assert.That(removed).IsNotEqualTo(500);
        await Assert.That(_services.GetRequiredService<PhotoLibraryFileSystem>().FilesIn(_root).Count).IsEqualTo(4);

        // What PhotoSense records is kept where the phone keeps an app's own files, and the previews where it may clear them.
        await Assert.That((File.Exists(Path.Combine(In("Documents"), "photosense.db")), Directory.EnumerateFiles(Path.Combine(In("Caches"), "thumbnails"), "*", SearchOption.AllDirectories).Any()))
            .IsEqualTo((true, true));
    }
}
