using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Api;
using PhotoSense.Api.About;
using PhotoSense.Api.Hosting;
using PhotoSense.Api.Http;
using PhotoSense.Application.Transfer;
using PhotoSense.Desktop.Imaging;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Library;
using PhotoSense.Infrastructure.Transfer;
using PhotoSense.Tests.Endpoints;

namespace PhotoSense.Tests.Application;

/// <summary>
/// Backing up from a phone to the computer it was paired with: the phone's whole stack on a photo library in memory,
/// the computer's door and backups on a folder of its own, the two talking over this computer's own network.
/// </summary>
public sealed class BackupTests : IDisposable
{
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("photosense-backup-");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "photosense-backup-library-" + Guid.NewGuid().ToString("N"), "Photo Library");
    private readonly FakePhotoLibrary _library = new();
    private readonly FakeAppStore _app = new();
    private readonly FakeComputerFinder _computers = new();
    private readonly FakeDevice _device = new();
    private readonly ServiceProvider _phone;
    private readonly ApiRouter _router;
    private readonly MemoryPairs _computerPairs = new("computer-1");
    private readonly MemoryBackupStore _kept = new();
    private readonly FakeAdvertiser _telling = new();
    private readonly ComputerBackup _backups;
    private readonly ComputerPairing _door;
    private readonly BackupApi _computer;
    private readonly List<IDisposable> _open = [];
    // The scripted computer's end of the connection.
    private Stream? _far;

    public BackupTests()
    {
        _phone = new ServiceCollection()
            .AddPhotoSensePhone(_library, new FakePhotoAccess(), new MagickImageAnalyzer(), In("Documents"), In("Caches"), new FakeAdvertiser(), _app, new FakeCodeScanner(), _computers, _device,
                new FakeNotifier(), new FakeShareSheet(), "Jamie's iPhone", _root, new TcpTransferListener(0))
            .AddPhotoSenseApi()
            .AddSingleton(new ProgramInfo("iphone", "1.2.0.57", () => "notices"))
            .AddStandInStores()
            // The phone's day is the test's, so that what was taken in the last 30 days is known.
            .AddSingleton<TimeProvider>(new TestClock())
            .BuildServiceProvider();
        _router = _phone.GetRequiredService<PhotoSenseRoutes>().Router;
        _backups = TestBackups.Computer(Path.Combine(In("Home"), "Pictures", "PhotoSense"), _kept, home: In("Home"));
        _door = new ComputerPairing(new TcpTransferListener(0), _telling, _computerPairs, "Studio iMac", () => ["127.0.0.1"], _backups, TimeProvider.System);
        _computer = new BackupApi(new TestApi(s => s.AddSingleton(_backups)));
    }

    public void Dispose()
    {
        foreach (var open in _open) open.Dispose();
        _door.StopListening();
        _phone.GetRequiredService<PhoneBackup>().Pause();
        _phone.Dispose();
        TestFiles.Remove(_data);
    }

    private string In(string folder) => Path.Combine(_data.FullName, folder);

    private string Folder => Path.Combine(In("Home"), "Pictures", "PhotoSense", "Jamie's iPhone");

    private PhoneBackup Backup => _phone.GetRequiredService<PhoneBackup>();

    /// <summary>A Live Photo, a picture, and a video taken a few days before the test's day.</summary>
    private void Library()
    {
        _library.Add("AAAAAAAA-1", (LibraryFileKind.Photo, "IMG_0001.JPG", MediaSamples.Picture("LIVE-1")), (LibraryFileKind.PairedVideo, "IMG_0001.MOV", MediaSamples.Movie("LIVE-1")));
        _library.Add("BBBBBBBB-2", (LibraryFileKind.Photo, "IMG_0002.JPG", MediaSamples.Picture()));
        _library.Add("CCCCCCCC-3", (LibraryFileKind.Video, "IMG_0003.MOV", MediaSamples.Movie(creationDate: "2026-10-10T10:00:00+0000")));
    }

    /// <summary>The phone and the computer paired already, the computer waiting and found on the Wi-Fi.</summary>
    private async Task PairedAsync(CancellationToken ct)
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var phonePairs = _phone.GetRequiredService<IPairingStore>();
        await _computerPairs.SaveAsync(new PairedDevice(await phonePairs.SelfIdAsync(ct), "Jamie's iPhone", secret), ct);
        await phonePairs.SaveAsync(new PairedDevice("computer-1", "Studio iMac", secret), ct);
        await _phone.GetRequiredService<IBackupLedger>().SaveComputerAsync(new BackupComputer("computer-1", "Studio iMac", "Pictures/PhotoSense", null, null), ct);
        await _door.ListenAsync(ct);
        var port = _telling.Telling!.Value.Port;
        _computers.Computers.Add(new FoundComputer("computer-1", "Studio iMac", c => Loopback.ConnectAsync(port, c)));
    }

    /// <summary>
    /// A computer of the test's own, paired with the phone already and found on the Wi-Fi: the next backup connects to
    /// it, and the test says what it likes on its behalf.
    /// </summary>
    private async Task<Task<SecureChannel>> ScriptedComputerAsync(CancellationToken ct)
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var phonePairs = _phone.GetRequiredService<IPairingStore>();
        await _computerPairs.SaveAsync(new PairedDevice(await phonePairs.SelfIdAsync(ct), "Jamie's iPhone", secret), ct);
        await phonePairs.SaveAsync(new PairedDevice("computer-1", "Studio iMac", secret), ct);
        await _phone.GetRequiredService<IBackupLedger>().SaveComputerAsync(new BackupComputer("computer-1", "Studio iMac", "Pictures/PhotoSense", null, null), ct);
        var linked = new TaskCompletionSource<SecureChannel>();
        _computers.Computers.Clear();
        _computers.Computers.Add(new FoundComputer("computer-1", "Studio iMac", async c =>
        {
            var (near, far) = await Loopback.PairAsync(c);
            _open.AddRange([near, far]);
            _far = far;
            _ = Task.Run(async () =>
            {
                var computer = await Pairing.ComputerAsync(far, new Device("computer-1", "Studio iMac"), _computerPairs, (_, _, _) => Task.FromResult(false), null, true, CancellationToken.None);
                _open.Add(computer.Channel);
                linked.SetResult(computer.Channel);
            }, CancellationToken.None);
            return near;
        }));
        return linked.Task;
    }

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

    private async Task<JsonElement> BackedUpAsync(CancellationToken ct)
    {
        await PhoneAsync("POST", "backup/start", ct);
        await Backup.Running;
        return await PhoneAsync("GET", "backup", ct);
    }

    private static object Settings(bool automatic = false, bool charging = true, bool videos = true, bool favorites = true, bool recent = true)
        => new { automatic, onlyWhileCharging = charging, includeVideos = videos, keepFavorites = favorites, keepRecent = recent };

    private async Task<JsonElement> PhoneOnComputerAsync(CancellationToken ct) => (await _computer.PhonesAsync(Http.Get("backup/phones").FromClient(), ct)).Json()[0];

    private static string[] Names(JsonElement phone) => [.. phone.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("text").GetString()!)];

    [Test]
    public async Task A_Backup_Sends_What_Waits_Each_File_Checked_By_Its_Checksum_And_The_Next_Sends_Nothing(CancellationToken ct)
    {
        Library();
        await PairedAsync(ct);
        var before = await PhoneAsync("GET", "backup", ct);
        await Assert.That((before.GetProperty("stage").GetString(), before.GetProperty("computer").GetString(), before.GetProperty("waiting").GetInt32(), before.GetProperty("onComputer").GetInt32()))
            .IsEqualTo(("ready", "Studio iMac", 3, 0));
        await Assert.That((before.GetProperty("strip").GetArrayLength(), before.GetProperty("lastBackup").ValueKind)).IsEqualTo((3, JsonValueKind.Null));

        var after = await BackedUpAsync(ct);
        await Assert.That((after.GetProperty("stage").GetString(), after.GetProperty("waiting").GetInt32(), after.GetProperty("onComputer").GetInt32(), after.GetProperty("done").GetInt32(), after.GetProperty("total").GetInt32()))
            .IsEqualTo(("ready", 0, 3, 4, 4));
        await Assert.That((after.GetProperty("bytes").GetInt64() == after.GetProperty("totalBytes").GetInt64(), after.GetProperty("folder").GetString(), after.GetProperty("lastBackup").ValueKind))
            .IsEqualTo((true, "Pictures/PhotoSense/Jamie's iPhone", JsonValueKind.String));
        await Assert.That(after.GetProperty("strip").EnumerateArray().All(t => t.GetProperty("done").GetBoolean())).IsTrue();
        await Assert.That(Directory.GetFiles(Folder).Select(f => Path.GetFileName(f)!).Order()).IsEquivalentTo(["IMG_0001.JPG", "IMG_0001.MOV", "IMG_0002.JPG", "IMG_0003.MOV"], CollectionOrdering.Matching);
        await Assert.That(await File.ReadAllBytesAsync(Path.Combine(Folder, "IMG_0001.MOV"), ct)).IsEquivalentTo(MediaSamples.Movie("LIVE-1"), CollectionOrdering.Matching);
        var library = await PhoneAsync("GET", "backup/library", ct);
        await Assert.That((library.GetProperty("computer").GetString(), library.GetProperty("onComputer").GetArrayLength(), library.GetProperty("waiting").GetArrayLength())).IsEqualTo(("Studio iMac", 3, 0));

        var phone = await PhoneOnComputerAsync(ct);
        await Assert.That((phone.GetProperty("name").GetString(), phone.GetProperty("kind").GetString(), phone.GetProperty("held").GetInt32(), phone.GetProperty("folder").GetString(), phone.GetProperty("path").GetString()))
            .IsEqualTo(("Jamie's iPhone", "iPhone", 4, "Pictures/PhotoSense/Jamie's iPhone", Folder));
        await Assert.That((phone.GetProperty("safe").GetInt32(), phone.GetProperty("arriving").ValueKind, phone.GetProperty("lastChecked").ValueKind)).IsEqualTo((2, JsonValueKind.Null, JsonValueKind.String));
        await Assert.That(Names(phone)).IsEquivalentTo(["4 items backed up and checked", "Receiving 4 items"], CollectionOrdering.Matching);

        // Everything is there, checked: nothing is sent again, and nothing more is noted.
        var again = await BackedUpAsync(ct);
        await Assert.That((again.GetProperty("done").GetInt32(), again.GetProperty("total").GetInt32(), again.GetProperty("onComputer").GetInt32())).IsEqualTo((0, 0, 3));
        await Assert.That(Directory.GetFiles(Folder).Length).IsEqualTo(4);
        await Assert.That(Names(await PhoneOnComputerAsync(ct)).Length).IsEqualTo(2);
    }

    [Test]
    public async Task What_Is_No_Longer_On_The_Computer_As_It_Was_Is_Sent_Again_And_A_File_Only_Touched_Still_Counts(CancellationToken ct)
    {
        Library();
        await PairedAsync(ct);
        await BackedUpAsync(ct);
        File.Delete(Path.Combine(Folder, "IMG_0001.MOV"));
        File.SetLastWriteTimeUtc(Path.Combine(Folder, "IMG_0001.JPG"), File.GetLastWriteTimeUtc(Path.Combine(Folder, "IMG_0001.JPG")).AddMinutes(5));
        await File.WriteAllBytesAsync(Path.Combine(Folder, "IMG_0002.JPG"), [1, 2, 3], ct);

        var before = await PhoneAsync("GET", "backup", ct);
        // The phone knows only what the computer last said: it learns of the change as the next backup starts.
        await Assert.That(before.GetProperty("waiting").GetInt32()).IsEqualTo(0);
        var after = await BackedUpAsync(ct);

        await Assert.That((after.GetProperty("total").GetInt32(), after.GetProperty("done").GetInt32(), after.GetProperty("onComputer").GetInt32())).IsEqualTo((2, 2, 3));
        await Assert.That(File.Exists(Path.Combine(Folder, "IMG_0001.MOV"))).IsTrue();
        // The changed file is the person's own now, and stays; the picture comes again under a new name.
        await Assert.That(Directory.GetFiles(Folder).Length).IsEqualTo(5);
        await Assert.That(await File.ReadAllBytesAsync(Path.Combine(Folder, "IMG_0002.JPG"), ct)).IsEquivalentTo(new byte[] { 1, 2, 3 }, CollectionOrdering.Matching);
        await Assert.That(Names(await PhoneOnComputerAsync(ct))).Contains("2 items no longer in the folder as they were, so the phone sends them again");
    }

    [Test]
    public async Task Automatic_Backup_Is_Pro_And_Runs_Only_While_Charging_When_Asked_To(CancellationToken ct)
    {
        Library();
        await PairedAsync(ct);
        var refused = await AskAsync("POST", "backup/settings", ct, Settings(automatic: true));
        await Assert.That(refused).IsEqualTo((HttpStatusCode.Conflict, "Automatic backup is part of Pro. Sending photos yourself stays free."));
        var notSaid = await AskAsync("POST", "backup/settings", ct, "not settings");
        await Assert.That(notSaid).IsEqualTo((HttpStatusCode.BadRequest, "Say how the phone backs up."));
        // Off it is never due.
        await Assert.That((await PhoneAsync("POST", "backup/auto", ct)).GetProperty("total").GetInt32()).IsEqualTo(0);

        _app.Purchases.Add(new StorePurchase(PlanProducts.Lifetime, TestClock.Day, null, Trial: false));
        var saved = await PhoneAsync("POST", "backup/settings", ct, Settings(automatic: true, videos: false));
        await Assert.That((saved.GetProperty("settings").GetProperty("automatic").GetBoolean(), saved.GetProperty("waiting").GetInt32())).IsEqualTo((true, 2));

        // Not charging: not due.
        await Assert.That((await PhoneAsync("POST", "backup/auto", ct)).GetProperty("stage").GetString()).IsEqualTo("ready");
        await Assert.That((await PhoneAsync("GET", "backup", ct)).GetProperty("onComputer").GetInt32()).IsEqualTo(0);
        _device.Charging = true;
        await PhoneAsync("POST", "backup/auto", ct);
        await Backup.Running;
        var done = await PhoneAsync("GET", "backup", ct);
        // The video stays on the phone only: videos were left out.
        await Assert.That((done.GetProperty("onComputer").GetInt32(), done.GetProperty("waiting").GetInt32(), done.GetProperty("total").GetInt32())).IsEqualTo((2, 0, 3));
        // Nothing waits: not due. Turned off again, it is let be, Pro or not.
        await Assert.That(await Backup.AutomaticAsync(ct)).IsFalse();
        await PhoneAsync("POST", "backup/settings", ct, Settings(charging: false));
        await PhoneAsync("POST", "backup/settings", ct, Settings(automatic: true, charging: false));
        _app.Purchases.Clear();
        await PhoneAsync("POST", "backup/settings", ct, Settings(automatic: true, charging: false, videos: true));
        // Pro has ended: automatic backup no longer runs, though it stays on.
        await Assert.That(await Backup.AutomaticAsync(ct)).IsFalse();
    }

    [Test]
    public async Task A_Phone_Paired_With_No_Computer_Says_So_And_Backs_Up_Nothing(CancellationToken ct)
    {
        Library();
        var state = await PhoneAsync("GET", "backup", ct);
        await Assert.That((state.GetProperty("stage").GetString(), state.GetProperty("computer").ValueKind, state.GetProperty("waiting").GetInt32())).IsEqualTo(("unpaired", JsonValueKind.Null, 3));
        await Assert.That(await AskAsync("POST", "backup/start", ct)).IsEqualTo((HttpStatusCode.Conflict, "Pair with a computer first: in Settings, choose Computer."));
        _app.Purchases.Add(new StorePurchase(PlanProducts.Lifetime, TestClock.Day, null, Trial: false));
        _device.Charging = true;
        await PhoneAsync("POST", "backup/settings", ct, Settings(automatic: true));
        await Assert.That(await Backup.AutomaticAsync(ct)).IsFalse();
        var library = await PhoneAsync("GET", "backup/library", ct);
        await Assert.That((library.GetProperty("computer").ValueKind, library.GetProperty("waiting").GetArrayLength())).IsEqualTo((JsonValueKind.Null, 3));
        var safe = await PhoneAsync("GET", "backup/safe", ct);
        await Assert.That((safe.GetProperty("computer").ValueKind, safe.GetProperty("checked").GetProperty("count").GetInt32())).IsEqualTo((JsonValueKind.Null, 0));
    }

    [Test]
    public async Task A_Computer_That_Is_Not_Found_Or_Does_Not_Know_The_Phone_Stops_The_Backup_Saying_Why(CancellationToken ct)
    {
        Library();
        await PairedAsync(ct);
        var found = _computers.Computers.Single();
        _computers.Computers.Clear();
        var notFound = await BackedUpAsync(ct);
        await Assert.That((notFound.GetProperty("stage").GetString(), notFound.GetProperty("problem").GetString()))
            .IsEqualTo(("failed", "Studio iMac was not found on this Wi-Fi. Check that PhotoSense is open on it, and that both are on the same Wi-Fi."));

        _computers.Computers.Add(found);
        await _computerPairs.SaveAsync(new PairedDevice(await _phone.GetRequiredService<IPairingStore>().SelfIdAsync(ct), "Jamie's iPhone", new byte[32]), ct);
        var notPaired = await BackedUpAsync(ct);
        await Assert.That(notPaired.GetProperty("problem").GetString()).IsEqualTo("Studio iMac is not the computer this phone was paired with. Pair them again.");

        var stranger = new MemoryPairs("computer-1");
        var told = new FakeAdvertiser();
        var door = new ComputerPairing(new TcpTransferListener(0), told, stranger, "Studio iMac", () => ["127.0.0.1"], _backups, TimeProvider.System);
        await door.ListenAsync(ct);
        _computers.Computers.Clear();
        var port = told.Telling!.Value.Port;
        _computers.Computers.Add(new FoundComputer("computer-1", "Studio iMac", c => Loopback.ConnectAsync(port, c)));
        var unknown = await BackedUpAsync(ct);
        door.StopListening();
        await Assert.That(unknown.GetProperty("problem").GetString()).IsEqualTo("Studio iMac does not know this phone any more. Pair them again: on the computer, open Phones and choose Pair a phone.");

        await _phone.GetRequiredService<IPairingStore>().SaveAsync(new PairedDevice("computer-2", "Other", new byte[32]), ct);
        await _phone.GetRequiredService<IBackupLedger>().SaveComputerAsync(new BackupComputer("computer-3", "Gone PC", "Pictures/PhotoSense", null, null), ct);
        var forgotten = await BackedUpAsync(ct);
        await Assert.That(forgotten.GetProperty("problem").GetString()).IsEqualTo("This phone no longer knows Gone PC. Pair them again: in Settings, choose Computer.");
    }

    [Test]
    public async Task Paused_On_The_Computer_The_Phone_Is_Told_Where_To_Resume_And_Backups_Can_Go_To_Another_Folder(CancellationToken ct)
    {
        Library();
        await PairedAsync(ct);
        await BackedUpAsync(ct);
        var phoneId = (await PhoneOnComputerAsync(ct)).GetProperty("id").GetString()!;
        var paused = (await _computer.PausePhoneAsync(Http.Post("backup/phones/x/pause", "{\"paused\":true}").FromClient(), phoneId, ct)).Json();
        await Assert.That(paused.GetProperty("paused").GetBoolean()).IsTrue();
        _library.Add("DDDDDDDD-4", (LibraryFileKind.Photo, "IMG_0004.JPG", MediaSamples.Picture("NEW-4")));
        var refused = await BackedUpAsync(ct);
        await Assert.That((refused.GetProperty("stage").GetString(), refused.GetProperty("problem").GetString(), refused.GetProperty("waiting").GetInt32()))
            .IsEqualTo(("failed", "Backups from Jamie's iPhone are paused on this computer. Resume them there, in Phones.", 1));

        await _computer.PausePhoneAsync(Http.Post("backup/phones/x/pause", "{\"paused\":false}").FromClient(), phoneId, ct);
        var elsewhere = Directory.CreateDirectory(In("Elsewhere")).FullName;
        var moved = (await _computer.FolderAsync(Http.Post("backup/phones/x/folder", JsonSerializer.Serialize(new { folder = elsewhere })).FromClient(), phoneId, ct)).Json();
        await Assert.That((moved.GetProperty("path").GetString(), moved.GetProperty("folder").GetString())).IsEqualTo((elsewhere, elsewhere));
        var resumed = await BackedUpAsync(ct);
        await Assert.That((resumed.GetProperty("stage").GetString(), resumed.GetProperty("waiting").GetInt32(), resumed.GetProperty("folder").GetString())).IsEqualTo(("ready", 0, elsewhere));
        await Assert.That(Directory.GetFiles(elsewhere).Select(f => Path.GetFileName(f)!)).IsEquivalentTo(["IMG_0004.JPG"], CollectionOrdering.Matching);
        await Assert.That(Names(await PhoneOnComputerAsync(ct)).Take(5)).IsEquivalentTo(
            ["1 item backed up and checked", "Receiving 1 item", $"Backups now go to {elsewhere}", "Backups resumed", "Backups paused on this computer"], CollectionOrdering.Matching);

        var nowhere = await _computer.FolderAsync(Http.Post("backup/phones/x/folder", JsonSerializer.Serialize(new { folder = In("Nowhere") })).FromClient(), phoneId, ct);
        await Assert.That((nowhere.StatusCode, nowhere.Text())).IsEqualTo((HttpStatusCode.BadRequest, $"Folder not found: {In("Nowhere")}"));
        var none = await _computer.FolderAsync(Http.Post("backup/phones/x/folder", "{}").FromClient(), phoneId, ct);
        await Assert.That((none.StatusCode, none.Text())).IsEqualTo((HttpStatusCode.BadRequest, "Choose a folder."));
        var stranger = await _computer.FolderAsync(Http.Post("backup/phones/x/folder", JsonSerializer.Serialize(new { folder = elsewhere })).FromClient(), "nobody", ct);
        var strangerPaused = await _computer.PausePhoneAsync(Http.Post("backup/phones/x/pause", "{}").FromClient(), "nobody", ct);
        await Assert.That((stranger.StatusCode, strangerPaused.StatusCode, stranger.Text())).IsEqualTo((HttpStatusCode.NotFound, HttpStatusCode.NotFound, "That phone is not paired with this computer."));
    }

    [Test]
    public async Task A_Backup_Paused_On_The_Phone_Carries_On_From_Where_It_Was(CancellationToken ct)
    {
        Library();
        await PairedAsync(ct);
        // Listed first, as the Backup tab would: what each file says of itself is known, and only sending reads it.
        await PhoneAsync("GET", "backup", ct);
        var reading = new TaskCompletionSource();
        var held = new TaskCompletionSource();
        _library.Fetching = async stop =>
        {
            reading.TrySetResult();
            await held.Task.WaitAsync(stop);
        };
        await PhoneAsync("POST", "backup/start", ct);
        await reading.Task.WaitAsync(ct);
        // Started again while going on, by the person or by itself: it is the one backup.
        await PhoneAsync("POST", "backup/start", ct);
        await Assert.That(await Backup.AutomaticAsync(ct)).IsFalse();
        var running = await PhoneAsync("GET", "backup", ct);
        await Assert.That((running.GetProperty("stage").GetString(), running.GetProperty("total").GetInt32())).IsEqualTo(("running", 4));
        var paused = await PhoneAsync("POST", "backup/pause", ct);
        await Backup.Running;
        await Assert.That((await PhoneAsync("GET", "backup", ct)).GetProperty("stage").GetString()).IsEqualTo("paused");
        // Paused, there is nothing more to pause.
        await Assert.That((await PhoneAsync("POST", "backup/pause", ct)).GetProperty("stage").GetString()).IsEqualTo("paused");
        await Assert.That(paused.GetProperty("stage").GetString()).IsEqualTo("paused");

        _library.Fetching = null;
        var resumed = await BackedUpAsync(ct);
        await Assert.That((resumed.GetProperty("stage").GetString(), resumed.GetProperty("onComputer").GetInt32())).IsEqualTo(("ready", 3));
        // Nothing cut short stays on the computer.
        await Assert.That(Directory.GetFiles(Folder, "*", new EnumerationOptions { AttributesToSkip = 0 }).Length).IsEqualTo(4);
    }

    [Test]
    public async Task Safe_To_Delete_Counts_What_The_Computer_Holds_Less_Favorites_And_The_Last_30_Days_As_The_Person_Chooses(CancellationToken ct)
    {
        Library();
        _library.Favorite("BBBBBBBB-2");
        _device.CloudPhotos = true;
        await PairedAsync(ct);
        var before = await PhoneAsync("GET", "backup/safe", ct);
        await Assert.That((before.GetProperty("checked").GetProperty("count").GetInt32(), before.GetProperty("removable").GetProperty("count").GetInt32())).IsEqualTo((0, 0));
        await BackedUpAsync(ct);

        var safe = await PhoneAsync("GET", "backup/safe", ct);
        await Assert.That((safe.GetProperty("computer").GetString(), safe.GetProperty("folder").GetString(), safe.GetProperty("lastChecked").ValueKind)).IsEqualTo(("Studio iMac", "Pictures/PhotoSense/Jamie's iPhone", JsonValueKind.String));
        int Count(string group) => safe.GetProperty(group).GetProperty("count").GetInt32();
        await Assert.That((Count("checked"), Count("favorites"), Count("recent"), Count("removable"))).IsEqualTo((3, 1, 1, 1));
        await Assert.That(safe.GetProperty("removable").GetProperty("bytes").GetInt64()).IsEqualTo(MediaSamples.Picture("LIVE-1").LongLength + MediaSamples.Movie("LIVE-1").LongLength);
        await Assert.That((safe.GetProperty("storageTotal").GetInt64(), safe.GetProperty("storageFree").GetInt64(), safe.GetProperty("cloudPhotos").GetBoolean())).IsEqualTo((128_000_000_000, 16_000_000_000, true));

        await PhoneAsync("POST", "backup/settings", ct, Settings(favorites: false, recent: false));
        var all = await PhoneAsync("GET", "backup/safe", ct);
        await Assert.That(all.GetProperty("removable").GetProperty("count").GetInt32()).IsEqualTo(3);
        // Largest first, each with every file of it.
        var removable = await Backup.RemovableAsync(ct);
        await Assert.That(removable.Select(r => r.Paths.Count)).IsEquivalentTo([2, 1, 1], CollectionOrdering.Matching);
        await Assert.That(removable[0].Paths.Select(f => Path.GetFileName(f)!)).IsEquivalentTo(["IMG_0001.JPG", "IMG_0001.MOV"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Each_Program_Answers_Its_Own_Half_Of_Backing_Up(CancellationToken ct)
    {
        var neither = new BackupApi(new TestApi());
        foreach (var asked in new[]
        {
            await neither.StateAsync(Http.Get("backup").FromClient(), ct), await neither.StartAsync(Http.Post("backup/start").FromClient(), ct),
            await neither.PauseAsync(Http.Post("backup/pause").FromClient(), ct), await neither.AutomaticAsync(Http.Post("backup/auto").FromClient(), ct),
            await neither.SettingsAsync(Http.Post("backup/settings", "{}").FromClient(), ct), await neither.LibraryAsync(Http.Get("backup/library").FromClient(), ct),
            await neither.SafeAsync(Http.Get("backup/safe").FromClient(), ct),
        })
            await Assert.That((asked.StatusCode, asked.Text())).IsEqualTo((HttpStatusCode.NotFound, "Only a phone backs up."));
        foreach (var asked in new[]
        {
            await neither.PhonesAsync(Http.Get("backup/phones").FromClient(), ct), await neither.FolderAsync(Http.Post("backup/phones/x/folder", "{}").FromClient(), "x", ct),
            await neither.PausePhoneAsync(Http.Post("backup/phones/x/pause", "{}").FromClient(), "x", ct),
        })
            await Assert.That((asked.StatusCode, asked.Text())).IsEqualTo((HttpStatusCode.NotFound, "Only a computer keeps phones' backups."));
        var notJson = await _computer.FolderAsync(Http.Post("backup/phones/x/folder", "not json").FromClient(), "x", ct);
        var pausedNotJson = await _computer.PausePhoneAsync(Http.Post("backup/phones/x/pause", "not json").FromClient(), "x", ct);
        await Assert.That((notJson.StatusCode, pausedNotJson.StatusCode)).IsEqualTo((HttpStatusCode.BadRequest, HttpStatusCode.NotFound));
        await Assert.That((await neither.PhonesAsync(Http.Get("backup/phones"), ct)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await _computer.PhonesAsync(Http.Get("backup/phones").FromClient(), ct)).Json().GetArrayLength()).IsEqualTo(0);
    }

    [Test]
    [Arguments("unknown")]
    [Arguments("mismatch")]
    [Arguments("gone")]
    [Arguments("closed")]
    public async Task A_Computer_That_Asks_Wrongly_Or_Goes_Stops_The_Backup_Saying_Why(string wrong, CancellationToken ct)
    {
        Library();
        // The phone's scan worked out one picture's checksum already: it is offered by it, and not read again to say it.
        var files = _phone.GetRequiredService<IPhotoFileSystem>();
        var picture = files.FilesIn(_root).Single(p => p.EndsWith("IMG_0002.JPG", StringComparison.Ordinal));
        var stored = files.Describe(picture)!;
        await _phone.GetRequiredService<IPhotoRepository>().AddOrUpdateAsync(new Photo
        {
            SourcePath = picture, FileName = "IMG_0002.JPG", FileSizeBytes = stored.Length, FileModifiedUtc = stored.ModifiedUtc, ContentHash = Convert.ToHexString(SHA256.HashData(MediaSamples.Picture())),
        }, ct);
        var computer = await ScriptedComputerAsync(ct);
        await PhoneAsync("POST", "backup/start", ct);
        var channel = await computer.WaitAsync(ct);
        await channel.ReceiveAsync(ct, "backup");
        await channel.SendAsync(new System.Text.Json.Nodes.JsonObject { ["t"] = "held", ["sha256"] = new System.Text.Json.Nodes.JsonArray(), ["more"] = false, ["folder"] = "Pictures/PhotoSense" }, ct);
        var offer = await channel.ReceiveAsync(ct, "offer");
        var offered = ((System.Text.Json.Nodes.JsonArray)offer["files"]!).Select(f => (Name: (string)f!["name"]!, Sha: (string?)f["sha256"], Id: (string)f["id"]!)).ToList();
        await Assert.That(offered.Single(f => f.Name == "IMG_0002.JPG").Sha).IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(MediaSamples.Picture())));
        var unknown = offered.First(f => f.Sha is null).Id;
        if (wrong == "unknown") await channel.SendAsync(new System.Text.Json.Nodes.JsonObject { ["t"] = "get", ["id"] = "never-offered" }, ct);
        else if (wrong == "mismatch") await channel.SendAsync(new System.Text.Json.Nodes.JsonObject { ["t"] = "got", ["id"] = unknown, ["sha256"] = new string('0', 64) }, ct);
        else if (wrong == "gone")
        {
            // A picture iCloud cannot fetch: it cannot be read.
            _library.Fetching = _ => Task.FromException(new IOException("iCloud could not be reached."));
            await channel.SendAsync(new System.Text.Json.Nodes.JsonObject { ["t"] = "get", ["id"] = offered.Single(f => f.Name == "IMG_0003.MOV").Id }, ct);
        }
        else _far!.Dispose();
        await Backup.Running;

        var failed = await PhoneAsync("GET", "backup", ct);
        await Assert.That(failed.GetProperty("stage").GetString()).IsEqualTo("failed");
        var problem = failed.GetProperty("problem").GetString()!;
        if (wrong == "unknown") await Assert.That(problem).IsEqualTo("The computer asked for a file that was not offered.");
        else if (wrong == "mismatch") await Assert.That(problem).IsEqualTo("The computer's check of a file did not match this phone's. Back up again.");
        else if (wrong == "gone") await Assert.That(problem).IsEqualTo("The backup stopped: iCloud could not be reached.");
        else await Assert.That(problem).IsEqualTo("The other device stopped answering.");
    }

    [Test]
    public async Task Safe_To_Delete_Sends_What_The_Computer_Holds_To_Recently_Deleted_After_The_Phones_Question_The_Largest_First(CancellationToken ct)
    {
        Library();
        await PairedAsync(ct);
        await BackedUpAsync(ct);
        await PhoneAsync("POST", "backup/settings", ct, Settings(favorites: false, recent: false));

        // The person says no to the phone's question: nothing goes, and nothing is counted.
        _library.Refuse = "You chose not to let PhotoSense delete these photos.";
        await Assert.That(await AskAsync("POST", "backup/remove", ct, new { atMost = 2 })).IsEqualTo((HttpStatusCode.Conflict, "You chose not to let PhotoSense delete these photos."));
        await Assert.That(_library.RecentlyDeleted.Count).IsEqualTo(0);

        var removed = await PhoneAsync("POST", "backup/remove", ct, new { atMost = 2 });
        await Assert.That((removed.GetProperty("removed").GetInt32(), removed.GetProperty("left").GetInt32(), removed.GetProperty("storageTotal").GetInt64(), removed.GetProperty("storageFreeBefore").GetInt64()))
            .IsEqualTo((2, 1, 128_000_000_000, 16_000_000_000));
        // The Live Photo, with its video, and the next largest; the picture stays.
        await Assert.That(_library.RecentlyDeleted.Order()).IsEquivalentTo(["AAAAAAAA-1", "CCCCCCCC-3"], CollectionOrdering.Matching);
        await Assert.That(removed.GetProperty("bytes").GetInt64()).IsEqualTo(MediaSamples.Picture("LIVE-1").LongLength + MediaSamples.Movie("LIVE-1").LongLength + MediaSamples.Movie(creationDate: "2026-10-10T10:00:00+0000").LongLength);
        await Assert.That((await PhoneAsync("GET", "plan", ct)).GetProperty("leftToday").GetInt32()).IsEqualTo(48);
        await Assert.That((await PhoneAsync("GET", "backup/safe", ct)).GetProperty("removable").GetProperty("count").GetInt32()).IsEqualTo(1);

        // Over what is left today, nothing goes.
        await _phone.GetRequiredService<IPlanStore>().AddRemovedAsync(DateOnly.FromDateTime(TestClock.Day.LocalDateTime), 48, ct);
        var over = await AskAsync("POST", "backup/remove", ct);
        await Assert.That((over.Status, over.Text.Contains("50", StringComparison.Ordinal))).IsEqualTo((HttpStatusCode.Conflict, true));
        await _phone.GetRequiredService<IPlanStore>().AddRemovedAsync(DateOnly.FromDateTime(TestClock.Day.LocalDateTime), -48, ct);
        var rest = await PhoneAsync("POST", "backup/remove", ct, "not a request");
        await Assert.That((rest.GetProperty("removed").GetInt32(), rest.GetProperty("left").GetInt32())).IsEqualTo((1, 0));
        // Nothing left to delete: nothing is asked.
        await Assert.That((await PhoneAsync("POST", "backup/remove", ct)).GetProperty("removed").GetInt32()).IsEqualTo(0);
        await Assert.That(_library.RecentlyDeleted.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Send_Now_Backs_Up_Only_The_Pictures_And_Videos_Chosen_Videos_Too(CancellationToken ct)
    {
        Library();
        await PairedAsync(ct);
        await PhoneAsync("POST", "backup/settings", ct, Settings(videos: false));
        var listed = await PhoneAsync("GET", $"organize/files?root={Uri.EscapeDataString(_root)}", ct);
        var video = listed.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("name").GetString() == "IMG_0003.MOV").GetProperty("id").GetString();

        await PhoneAsync("POST", "backup/start", ct, new { files = new[] { video } });
        await Backup.Running;
        var sent = await PhoneAsync("GET", "backup", ct);
        await Assert.That((sent.GetProperty("total").GetInt32(), sent.GetProperty("onComputer").GetInt32(), sent.GetProperty("waiting").GetInt32())).IsEqualTo((1, 1, 2));
        await Assert.That(Directory.GetFiles(Folder).Select(f => Path.GetFileName(f)!)).IsEquivalentTo(["IMG_0003.MOV"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Only_A_Phone_Deletes_What_Is_On_Its_Computer(CancellationToken ct)
    {
        var services = new TestApi().Services;
        var computer = new PhoneBackup(new MemoryBackupLedger(), new FakeComputerFinder(), new MemoryPairs("pc"), services.GetRequiredService<PhotoSense.Application.Organizing.OrganizeLibrary>(),
            TestFiles.Disk, TestPlans.Subscription(), TestPlans.Removals(), new FakeDevice(), new FakeFavorites(), "PC", In("Pictures"), new TestClock());
        var refused = await new BackupApi(new TestApi(s => s.AddSingleton(computer))).RemoveAsync(Http.Post("backup/remove").FromClient(), ct);
        await Assert.That((refused.StatusCode, refused.Text())).IsEqualTo((HttpStatusCode.Conflict, "Only a phone deletes what is on its computer."));
        var neither = await new BackupApi(new TestApi()).RemoveAsync(Http.Post("backup/remove").FromClient(), ct);
        await Assert.That(neither.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }
}
