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
/// PhotoSense as the Android app runs it, all but Android's own media store and picture decoder: the page's requests
/// answered inside the app, on the photos of a phone's storage that is on no disk.
/// </summary>
// A scan's lines go to the one queue the page reads, which other classes count.
[NotInParallel("ScanLogQueue")]
public sealed class PhotosOnAndroidTests : IDisposable
{
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("photosense-android-");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "photosense-android-storage-" + Guid.NewGuid().ToString("N"), "Phone");
    private readonly FakeMediaStore _store = new();
    private readonly ServiceProvider _services;
    private readonly ApiRouter _router;

    public PhotosOnAndroidTests()
    {
        // Magick.NET stands in for the phone's own decoder, which measures pictures the same way.
        _services = new ServiceCollection()
            .AddPhotoSenseAndroid(_store, new FakePhotoAccess(), new MagickImageAnalyzer(), In("files"), In("cache"), new FakeAdvertiser(), "Pixel 8", _root, new TcpTransferListener(0))
            .AddPhotoSenseApi()
            .AddSingleton(new ProgramInfo("android", "1.2.0.57", () => "Third-party notices for PhotoSense for Android"))
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
        var answer = await _router.AnswerAsync(method, new Uri("https://appassets.androidplatform.net/api/" + pathAndQuery), [new("x-photosense-client", "phone")],
            body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body));
        return (answer.Status, Encoding.UTF8.GetString(answer.Body));
    }

    private async Task<JsonElement> JsonAsync(string method, string pathAndQuery, object? body = null)
    {
        var (status, text) = await AskAsync(method, pathAndQuery, body);
        await Assert.That(status / 100).IsEqualTo(2).Because(text);
        return JsonDocument.Parse(text).RootElement;
    }

    private async Task ScanAsync()
    {
        await JsonAsync("POST", "scan/start", new { primaryLocation = _root, recursive = true });
        for (var waited = 0; (await JsonAsync("GET", "scan/status")).GetProperty("completed").ValueKind == JsonValueKind.Null; waited++)
        {
            if (waited == 600) throw new TimeoutException("The scan did not finish.");
            await Task.Delay(50);
        }
    }

    [Test]
    public async Task The_Page_Scans_The_Phone_Removes_A_Copy_Into_Pictures_And_Takes_That_Back_Then_Deletes_It_Into_The_Bin()
    {
        var picture = MediaSamples.Picture();
        _store.Add("11", "DCIM/Camera/", "IMG_0001.jpg", picture);
        _store.Add("12", "Pictures/WhatsApp/", "IMG_0001.jpg", picture);
        _store.Add("13", "Movies/", "VID_0001.mp4", MediaSamples.Movie(), video: true);

        // The phone's storage is the one folder offered, with its own folders inside it.
        var places = await JsonAsync("GET", "folders");
        await Assert.That(places.GetProperty("folders").EnumerateArray().Single().GetProperty("path").GetString()).IsEqualTo(_root);
        var inside = await JsonAsync("GET", $"folders?path={Uri.EscapeDataString(_root)}");
        await Assert.That(inside.GetProperty("folders").EnumerateArray().Select(f => f.GetProperty("name").GetString()!)).IsEquivalentTo(["DCIM", "Movies", "Pictures"], CollectionOrdering.Matching);

        await ScanAsync();
        await Assert.That((await JsonAsync("GET", "scan/status")).GetProperty("totalPhotos").GetInt32()).IsEqualTo(3);
        var groups = await JsonAsync("GET", "scan/groups?mode=duplicates&page=1&pageSize=50");
        await Assert.That(groups.GetProperty("total").GetInt32()).IsEqualTo(1);

        // The copy waits in the removed folder of the whole phone, which Android keeps inside Pictures, and Undo takes it back.
        var removed = await JsonAsync("POST", "photos/bulk/remove-duplicates");
        await Assert.That((removed.GetProperty("removed").GetInt32(), _store.Changes.Count)).IsEqualTo((1, 1));
        var went = _store.Places().Values.Single(p => p.StartsWith("Pictures/PhotoSense Removed/", StringComparison.Ordinal));
        await Assert.That(went).IsEqualTo(went.Contains("WhatsApp", StringComparison.Ordinal) ? "Pictures/PhotoSense Removed/Pictures/WhatsApp/IMG_0001.jpg" : "Pictures/PhotoSense Removed/DCIM/Camera/IMG_0001.jpg");
        var waiting = await JsonAsync("GET", $"removed?root={Uri.EscapeDataString(_root)}");
        await Assert.That((waiting.GetProperty("files").GetInt32(), waiting.GetProperty("folders")[0].GetProperty("path").GetString())).IsEqualTo((1, Path.Combine(_root, "_PhotoSense_Removed")));
        await JsonAsync("POST", $"organize/undo/{removed.GetProperty("batchId").GetString()}");
        await Assert.That(_store.Places().Values.Order(StringComparer.Ordinal)).IsEquivalentTo(["DCIM/Camera/IMG_0001.jpg", "Movies/VID_0001.mp4", "Pictures/WhatsApp/IMG_0001.jpg"], CollectionOrdering.Matching);

        // Removed again and deleted permanently: it goes to the phone's bin.
        await JsonAsync("POST", "photos/bulk/remove-duplicates");
        var erased = await JsonAsync("POST", "removed/erase", new { folders = new[] { Path.Combine(_root, "_PhotoSense_Removed") } });
        await Assert.That((erased.GetProperty("erased").GetInt32(), _store.Trashed.Count, _store.Places().Count)).IsEqualTo((1, 1, 2));

        await Assert.That((File.Exists(Path.Combine(In("files"), "photosense.db")), Directory.EnumerateFiles(Path.Combine(In("cache"), "thumbnails"), "*", SearchOption.AllDirectories).Any()))
            .IsEqualTo((true, true));
    }

    [Test]
    public async Task Organize_Moves_Photos_Into_A_Folder_Inside_DCIM_Refuses_One_Outside_And_Copies_Nothing()
    {
        _store.Add("21", "DCIM/Camera/", "IMG_0001.jpg", MediaSamples.Picture());
        _store.Add("22", "DCIM/Camera/", "IMG_0002.jpg", MediaSamples.Picture());
        var camera = Path.Combine(_root, "DCIM", "Camera");
        var listed = await JsonAsync("GET", $"organize/files?root={Uri.EscapeDataString(camera)}");
        var files = listed.GetProperty("files").EnumerateArray().Select(f => new { id = f.GetProperty("id").GetString(), name = f.GetProperty("name").GetString() }).ToArray();

        var moved = await JsonAsync("POST", "organize/apply", new { basePath = camera, folderName = "Butte\\2022", direct = false, mode = "move", companions = true, label = "Butte\\2022", files });
        await Assert.That(moved.GetProperty("done").GetInt32()).IsEqualTo(2);
        await Assert.That(_store.Places().Values.All(p => p.StartsWith("DCIM/Camera/Butte/2022/", StringComparison.Ordinal))).IsTrue();
        await Assert.That(_store.Changes.Count).IsEqualTo(1);

        await JsonAsync("POST", $"organize/undo/{moved.GetProperty("batchId").GetString()}");
        await Assert.That(_store.Places().Values.Order(StringComparer.Ordinal)).IsEquivalentTo(["DCIM/Camera/IMG_0001.jpg", "DCIM/Camera/IMG_0002.jpg"], CollectionOrdering.Matching);

        // The phone's storage itself holds no folder of photos, and a copy is not made on the phone.
        var outside = await JsonAsync("POST", "organize/apply", new { basePath = _root, folderName = "Butte", direct = false, mode = "move", companions = true, label = "Butte", files });
        await Assert.That(outside.GetRawText()).Contains("photos are kept inside DCIM, Pictures or Movies");
        var copied = await JsonAsync("POST", "organize/apply", new { basePath = camera, folderName = "Copies", direct = false, mode = "copy", companions = true, label = "Copies", files });
        await Assert.That(copied.GetRawText()).Contains("Photos are not copied on the phone");
        await Assert.That(_store.Places().Values.Order(StringComparer.Ordinal)).IsEquivalentTo(["DCIM/Camera/IMG_0001.jpg", "DCIM/Camera/IMG_0002.jpg"], CollectionOrdering.Matching);
    }
}
