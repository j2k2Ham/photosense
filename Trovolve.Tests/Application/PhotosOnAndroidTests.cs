using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Trovolve.Domain.Configuration;
using Microsoft.Extensions.Options;
using Trovolve.Api;
using Trovolve.Api.About;
using Trovolve.Api.Hosting;
using Trovolve.Api.Http;

using Trovolve.Infrastructure.Library;
using Trovolve.Infrastructure.Transfer;

namespace Trovolve.Tests.Application;

/// <summary>
/// Trovolve as the Android app runs it, all but Android's own media store and picture decoder: the page's requests
/// answered inside the app, on the photos of a phone's storage that is on no disk.
/// </summary>
public sealed class PhotosOnAndroidTests : IDisposable
{
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("trovolve-android-");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "trovolve-android-storage-" + Guid.NewGuid().ToString("N"), "Phone");
    private readonly FakeMediaStore _store = new();
    private readonly ServiceProvider _services;
    private readonly ApiRouter _router;

    public PhotosOnAndroidTests()
    {
        // Magick.NET stands in for the phone's own decoder, which measures pictures the same way.
        _services = new ServiceCollection()
            .AddTrovolveAndroid(_store, new FakePhotoAccess(), MagickPictureDecoder.Analyzer, In("files"), In("cache"), new FakeAdvertiser(), new FakeAppStore(), new FakeCodeScanner(), new FakeComputerFinder(), new FakeDevice { Kind = "Android phone" }, new FakeNotifier(), new FakeShareSheet(), new FakeVaultKeys(), new FakeUnlocker(), new FakeWidgets(), new MemoryLocationSecrets(), new FakeWebSignIn(), "Pixel 8", _root, new TcpTransferListener(0))
            .AddTrovolveApi()
            .AddSingleton(new ProgramInfo("android", "1.2.0.57", () => "Third-party notices for Trovolve for Android"))
            .AddStandInStores()
            .BuildServiceProvider();
        _router = _services.GetRequiredService<TrovolveRoutes>().Router;
    }

    public void Dispose()
    {
        _services.Dispose();
        TestFiles.Remove(_data);
    }

    private string In(string folder) => Path.Combine(_data.FullName, folder);

    private async Task<(int Status, string Text)> AskAsync(string method, string pathAndQuery, CancellationToken ct, object? body = null)
    {
        var answer = await _router.AnswerAsync(method, new Uri("https://appassets.androidplatform.net/api/" + pathAndQuery), [new("x-trovolve-client", "phone")],
            body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body), ct);
        return (answer.Status, Encoding.UTF8.GetString(answer.Body));
    }

    private async Task<JsonElement> JsonAsync(string method, string pathAndQuery, CancellationToken ct, object? body = null)
    {
        var (status, text) = await AskAsync(method, pathAndQuery, ct, body);
        await Assert.That(status / 100).IsEqualTo(2).Because(text);
        return JsonDocument.Parse(text).RootElement;
    }

    private async Task ScanAsync(CancellationToken ct)
    {
        await JsonAsync("POST", "scan/start", ct, new { primaryLocation = _root, recursive = true });
        for (var waited = 0; (await JsonAsync("GET", "scan/status", ct)).GetProperty("completed").ValueKind == JsonValueKind.Null; waited++)
        {
            if (waited == 600) throw new TimeoutException("The scan did not finish.");
            await Task.Delay(50, ct);
        }
    }

    [Test]
    public async Task The_Page_Scans_The_Phone_Removes_A_Copy_Into_Pictures_And_Takes_That_Back_Then_Deletes_It_Into_The_Bin(CancellationToken ct)
    {
        var picture = MediaSamples.Picture();
        _store.Add("11", "DCIM/Camera/", "IMG_0001.jpg", picture);
        _store.Add("12", "Pictures/WhatsApp/", "IMG_0001.jpg", picture);
        _store.Add("13", "Movies/", "VID_0001.mp4", MediaSamples.Movie(), video: true);
        // Removed before Trovolve deleted to the bin, and waiting still.
        _store.Add("14", "Pictures/Trovolve Removed/DCIM/Camera/", "IMG_0009.jpg", picture);

        // The phone's storage is the one folder offered, with its own folders inside it.
        var places = await JsonAsync("GET", "folders", ct);
        await Assert.That(places.GetProperty("folders").EnumerateArray().Single().GetProperty("path").GetString()).IsEqualTo(_root);
        var inside = await JsonAsync("GET", $"folders?path={Uri.EscapeDataString(_root)}", ct);
        await Assert.That(inside.GetProperty("folders").EnumerateArray().Select(f => f.GetProperty("name").GetString()!)).IsEquivalentTo(["DCIM", "Movies", "Pictures"], CollectionOrdering.Matching);

        await ScanAsync(ct);
        await Assert.That((await JsonAsync("GET", "scan/status", ct)).GetProperty("totalPhotos").GetInt32()).IsEqualTo(3);
        var groups = await JsonAsync("GET", "scan/groups?mode=duplicates&page=1&pageSize=50", ct);
        await Assert.That(groups.GetProperty("total").GetInt32()).IsEqualTo(1);

        // The copy deleted from its review goes straight to Android's bin (Jamie, 2026-11-17): nothing waits in
        // Pictures/Trovolve Removed, and there is nothing to undo.
        var copy = groups.GetProperty("items")[0].GetProperty("members")[0].GetProperty("photo").GetProperty("id").GetString();
        var one = await JsonAsync("DELETE", $"photos/{copy}?physical=true", ct);
        await Assert.That((one.GetProperty("heldAt").ValueKind, one.GetProperty("batchId").ValueKind, _store.Trashed.Count)).IsEqualTo((JsonValueKind.Null, JsonValueKind.Null, 1));
        await Assert.That(_store.Places().Values.Count(p => p.StartsWith("Pictures/Trovolve Removed/", StringComparison.Ordinal))).IsEqualTo(1);

        // Library's Delete does the same.
        var listed = await JsonAsync("GET", $"organize/files?root={Uri.EscapeDataString(_root)}", ct);
        var movie = listed.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("name").GetString() == "VID_0001.mp4").GetProperty("id").GetString();
        var deleted = await JsonAsync("POST", "organize/remove", ct, new { root = _root, files = new[] { new { id = movie } } });
        await Assert.That((deleted.GetProperty("done").GetInt32(), deleted.GetProperty("batchId").ValueKind, _store.Trashed.Count, _store.Places().Count)).IsEqualTo((1, JsonValueKind.Null, 2, 2));

        // What waited in Trovolve Removed from before goes to the bin too, by Delete permanently.
        var waiting = await JsonAsync("GET", $"removed?root={Uri.EscapeDataString(_root)}", ct);
        await Assert.That((waiting.GetProperty("files").GetInt32(), waiting.GetProperty("folders")[0].GetProperty("path").GetString())).IsEqualTo((1, Path.Combine(_root, "_Trovolve_Removed")));
        var erased = await JsonAsync("POST", "removed/erase", ct, new { folders = new[] { Path.Combine(_root, "_Trovolve_Removed") } });
        await Assert.That((erased.GetProperty("erased").GetInt32(), _store.Trashed.Count, _store.Places().Count)).IsEqualTo((1, 3, 1));

        await Assert.That((_services.GetRequiredService<IOptions<PhotoStorageOptions>>().Value.ResolveDatabasePath() == Path.Combine(In("files"), "trovolve.db"), Directory.EnumerateFiles(Path.Combine(In("cache"), "thumbnails"), "*", SearchOption.AllDirectories).Any()))
            .IsEqualTo((true, true));
    }

    [Test]
    public async Task Organize_Moves_Photos_Into_A_Folder_Inside_DCIM_Refuses_One_Outside_And_Copies_Nothing(CancellationToken ct)
    {
        _store.Add("21", "DCIM/Camera/", "IMG_0001.jpg", MediaSamples.Picture());
        _store.Add("22", "DCIM/Camera/", "IMG_0002.jpg", MediaSamples.Picture());
        var camera = Path.Combine(_root, "DCIM", "Camera");
        var listed = await JsonAsync("GET", $"organize/files?root={Uri.EscapeDataString(camera)}", ct);
        var files = listed.GetProperty("files").EnumerateArray().Select(f => new { id = f.GetProperty("id").GetString(), name = f.GetProperty("name").GetString() }).ToArray();

        var moved = await JsonAsync("POST", "organize/apply", ct, new { basePath = camera, folderName = "Butte\\2022", direct = false, mode = "move", companions = true, label = "Butte\\2022", files });
        await Assert.That(moved.GetProperty("done").GetInt32()).IsEqualTo(2);
        await Assert.That(_store.Places().Values.All(p => p.StartsWith("DCIM/Camera/Butte/2022/", StringComparison.Ordinal))).IsTrue();
        await Assert.That(_store.Changes.Count).IsEqualTo(1);

        await JsonAsync("POST", $"organize/undo/{moved.GetProperty("batchId").GetString()}", ct);
        await Assert.That(_store.Places().Values.Order(StringComparer.Ordinal)).IsEquivalentTo(["DCIM/Camera/IMG_0001.jpg", "DCIM/Camera/IMG_0002.jpg"], CollectionOrdering.Matching);

        // Android keeps no favorites Trovolve can see: Safe to delete keeps none for being one.
        var safe = await JsonAsync("GET", "backup/safe", ct);
        await Assert.That((safe.GetProperty("favorites").GetProperty("count").GetInt32(), safe.GetProperty("cloudPhotos").GetBoolean())).IsEqualTo((0, false));
        await Assert.That(await _services.GetRequiredService<Trovolve.Application.Transfer.PhoneBackup>().RemovableAsync(ct)).IsEmpty();

        // The phone's storage itself holds no folder of photos, and a copy is not made on the phone.
        var outside = await JsonAsync("POST", "organize/apply", ct, new { basePath = _root, folderName = "Butte", direct = false, mode = "move", companions = true, label = "Butte", files });
        await Assert.That(outside.GetRawText()).Contains("photos are kept inside DCIM, Pictures or Movies");
        var copied = await JsonAsync("POST", "organize/apply", ct, new { basePath = camera, folderName = "Copies", direct = false, mode = "copy", companions = true, label = "Copies", files });
        await Assert.That(copied.GetRawText()).Contains("Photos are not copied on the phone");
        await Assert.That(_store.Places().Values.Order(StringComparer.Ordinal)).IsEquivalentTo(["DCIM/Camera/IMG_0001.jpg", "DCIM/Camera/IMG_0002.jpg"], CollectionOrdering.Matching);
    }
}
