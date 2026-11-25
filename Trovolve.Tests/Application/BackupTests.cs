using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Trovolve.Api;
using Trovolve.Api.About;
using Trovolve.Api.Hosting;
using Trovolve.Api.Http;
using Trovolve.Application.Plans;
using Trovolve.Application.Transfer;
using Trovolve.Desktop.Imaging;
using Trovolve.Domain.Entities;
using Trovolve.Domain.ValueObjects;
using Trovolve.Domain.Repositories;
using Trovolve.Domain.Services;
using Trovolve.Infrastructure.Library;
using Trovolve.Infrastructure.Transfer;
using Trovolve.Tests.Endpoints;

namespace Trovolve.Tests.Application;

/// <summary>
/// Backing up from a phone to the computer it was paired with: the phone's whole stack on a photo library in memory,
/// the computer's door and backups on a folder of its own, the two talking over this computer's own network.
/// </summary>
public sealed class BackupTests : IDisposable
{
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("trovolve-backup-");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "trovolve-backup-library-" + Guid.NewGuid().ToString("N"), "Photo Library");
    private readonly FakePhotoLibrary _library = new();
    private readonly FakeAppStore _app = new();
    private readonly FakeComputerFinder _computers = new();
    private readonly FakeDevice _device = new();
    private readonly ServiceProvider _phone;
    private readonly ApiRouter _router;
    private readonly MemoryPairs _computerPairs = new("computer-1");
    private readonly MemoryBackupStore _kept = new();
    private readonly TermsAcceptance _computerTerms = TestPlans.Terms();
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
            .AddTrovolvePhone(_library, new FakePhotoAccess(), new MagickImageAnalyzer(), In("Documents"), In("Caches"), new FakeAdvertiser(), _app, new FakeCodeScanner(), _computers, _device,
                new FakeNotifier(), new FakeShareSheet(), new FakeVaultKeys(), new FakeUnlocker(), new FakeWidgets(), new MemoryLocationSecrets(), new FakeWebSignIn(), "Jamie's iPhone", _root, new TcpTransferListener(0))
            .AddTrovolveApi()
            .AddSingleton(new ProgramInfo("iphone", "1.2.0.57", () => "notices"))
            .AddStandInStores()
            // The phone's day is the test's, so that what was taken in the last 30 days is known.
            .AddSingleton<TimeProvider>(new TestClock())
            .BuildServiceProvider();
        _router = _phone.GetRequiredService<TrovolveRoutes>().Router;
        _backups = TestBackups.Computer(Path.Combine(In("Home"), "Pictures", "Trovolve"), _kept, home: In("Home"), terms: _computerTerms, pairs: _computerPairs);
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

    private string Folder => Path.Combine(In("Home"), "Pictures", "Trovolve", "Jamie's iPhone");

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
        await _phone.GetRequiredService<IBackupLedger>().SaveComputerAsync(new BackupComputer("computer-1", "Studio iMac", "Pictures/Trovolve", null, null), ct);
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
        await _phone.GetRequiredService<IBackupLedger>().SaveComputerAsync(new BackupComputer("computer-1", "Studio iMac", "Pictures/Trovolve", null, null), ct);
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
        var answer = await _router.AnswerAsync(method, new Uri("app://trovolve/api/" + path), [new("x-trovolve-client", "phone")], body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body), ct);
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

    /// <summary>
    /// The phone as the computer has it once the computer has noted <paramref name="last"/>: it notes what it received
    /// as the connection closes, which can be just after the phone has finished.
    /// </summary>
    private async Task<JsonElement> NotedOnComputerAsync(string last, CancellationToken ct)
    {
        for (var tries = 0; ; tries++)
        {
            var phone = await PhoneOnComputerAsync(ct);
            if (Names(phone).FirstOrDefault() == last || tries == 100) return phone;
            await Task.Delay(50, ct);
        }
    }

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
            .IsEqualTo((true, "Pictures/Trovolve/Jamie's iPhone", JsonValueKind.String));
        await Assert.That(after.GetProperty("strip").EnumerateArray().All(t => t.GetProperty("done").GetBoolean())).IsTrue();
        await Assert.That(Directory.GetFiles(Folder).Select(f => Path.GetFileName(f)!).Order()).IsEquivalentTo(["IMG_0001.JPG", "IMG_0001.MOV", "IMG_0002.JPG", "IMG_0003.MOV"], CollectionOrdering.Matching);
        await Assert.That(await File.ReadAllBytesAsync(Path.Combine(Folder, "IMG_0001.MOV"), ct)).IsEquivalentTo(MediaSamples.Movie("LIVE-1"), CollectionOrdering.Matching);
        var library = await PhoneAsync("GET", "backup/library", ct);
        await Assert.That((library.GetProperty("computer").GetString(), library.GetProperty("onComputer").GetArrayLength(), library.GetProperty("waiting").GetArrayLength())).IsEqualTo(("Studio iMac", 3, 0));

        var phone = await NotedOnComputerAsync("4 items backed up and checked", ct);
        await Assert.That((phone.GetProperty("name").GetString(), phone.GetProperty("kind").GetString(), phone.GetProperty("held").GetInt32(), phone.GetProperty("folder").GetString(), phone.GetProperty("path").GetString()))
            .IsEqualTo(("Jamie's iPhone", "iPhone", 4, "Pictures/Trovolve/Jamie's iPhone", Folder));
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
            .IsEqualTo(("failed", "Studio iMac was not found on this Wi-Fi. Check that Trovolve is open on it, and that both are on the same Wi-Fi."));

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
        await _phone.GetRequiredService<IBackupLedger>().SaveComputerAsync(new BackupComputer("computer-3", "Gone PC", "Pictures/Trovolve", null, null), ct);
        var forgotten = await BackedUpAsync(ct);
        await Assert.That(forgotten.GetProperty("problem").GetString()).IsEqualTo("This phone no longer knows Gone PC. Pair them again: in Settings, choose Computer.");
    }

    [Test]
    public async Task Forgetting_The_Computer_On_The_Phone_Tells_It_And_Its_Copies_Count_Again_Only_Once_Paired_Again(CancellationToken ct)
    {
        Library();
        await PairedAsync(ct);
        var phoneId = await _phone.GetRequiredService<IPairingStore>().SelfIdAsync(ct);
        // Paired but never backed up: the computer has no record of the phone, and forgets its pairing all the same.
        var first = await PhoneAsync("POST", "backup/forget", ct);
        await Assert.That((first.GetProperty("computer").GetString(), first.GetProperty("told").GetBoolean())).IsEqualTo(("Studio iMac", true));
        await Assert.That(_computerPairs.Paired.Count).IsEqualTo(0);

        await PairedAsync(ct);
        await BackedUpAsync(ct);
        await Assert.That((await PhoneAsync("GET", "backup/safe", ct)).GetProperty("checked").GetProperty("count").GetInt32()).IsEqualTo(3);
        await NotedOnComputerAsync("4 items backed up and checked", ct);

        var forgot = await PhoneAsync("POST", "backup/forget", ct);
        await Assert.That(forgot.GetProperty("told").GetBoolean()).IsTrue();
        var state = await PhoneAsync("GET", "backup", ct);
        await Assert.That((state.GetProperty("stage").GetString(), state.GetProperty("problem").ValueKind, state.GetProperty("waiting").GetInt32())).IsEqualTo(("unpaired", JsonValueKind.Null, 3));
        // Nothing counts as backed up any more, though every copy stays on the computer.
        var safe = await PhoneAsync("GET", "backup/safe", ct);
        await Assert.That((safe.GetProperty("checked").GetProperty("count").GetInt32(), safe.GetProperty("removable").GetProperty("count").GetInt32())).IsEqualTo((0, 0));
        await Assert.That(await _phone.GetRequiredService<IPairingStore>().GetAsync("computer-1", ct)).IsNull();
        await Assert.That((await _computer.PhonesAsync(Http.Get("backup/phones").FromClient(), ct)).Json().GetArrayLength()).IsEqualTo(0);
        await Assert.That((_computerPairs.Paired.Count, Directory.GetFiles(Folder).Length)).IsEqualTo((0, 4));
        await Assert.That((await _kept.EventsAsync(phoneId, 1, ct))[0].Text).IsEqualTo("Forgotten. Its backups stay here");

        // Paired again: the same folder, what is there counted again once the computer says so, and nothing sent twice.
        await PairedAsync(ct);
        await _backups.PairedAsync(new Device(phoneId, "Jamie's iPhone"), ct);
        var again = await BackedUpAsync(ct);
        await Assert.That((again.GetProperty("total").GetInt32(), again.GetProperty("onComputer").GetInt32(), again.GetProperty("folder").GetString())).IsEqualTo((0, 3, "Pictures/Trovolve/Jamie's iPhone"));
        await Assert.That((await PhoneOnComputerAsync(ct)).GetProperty("held").GetInt32()).IsEqualTo(4);

        // Not found, it is forgotten on the phone all the same, and still listed on the computer.
        _computers.Computers.Clear();
        var alone = await PhoneAsync("POST", "backup/forget", ct);
        await Assert.That(alone.GetProperty("told").GetBoolean()).IsFalse();
        await Assert.That((await _computer.PhonesAsync(Http.Get("backup/phones").FromClient(), ct)).Json().GetArrayLength()).IsEqualTo(1);
        var none = await AskAsync("POST", "backup/forget", ct);
        await Assert.That(none).IsEqualTo((HttpStatusCode.Conflict, "This phone is not paired with a computer."));
    }

    [Test]
    public async Task A_Phone_Forgotten_On_The_Computer_Is_Turned_Away_And_Forgets_The_Computer_Too_Saying_Why(CancellationToken ct)
    {
        Library();
        await PairedAsync(ct);
        await BackedUpAsync(ct);
        var phoneId = (await NotedOnComputerAsync("4 items backed up and checked", ct)).GetProperty("id").GetString()!;

        var left = (await _computer.ForgetPhoneAsync(Http.Post("backup/phones/x/forget").FromClient(), phoneId, ct)).Json();
        await Assert.That((left.GetArrayLength(), _computerPairs.Paired.Count, Directory.GetFiles(Folder).Length)).IsEqualTo((0, 0, 4));
        var twice = await _computer.ForgetPhoneAsync(Http.Post("backup/phones/x/forget").FromClient(), phoneId, ct);
        await Assert.That((twice.StatusCode, twice.Text())).IsEqualTo((HttpStatusCode.NotFound, "That phone is not paired with this computer."));

        // The phone learns of it at its next connection, here as it tells the computer the note was accepted.
        await PhoneAsync("POST", "terms/accept", ct);
        await Assert.That(await Backup.Telling).IsFalse();
        var state = await PhoneAsync("GET", "backup", ct);
        await Assert.That((state.GetProperty("stage").GetString(), state.GetProperty("problem").GetString()))
            .IsEqualTo(("unpaired", "Studio iMac does not know this phone any more. Pair them again: on the computer, open Phones and choose Pair a phone."));
        await Assert.That((await PhoneAsync("GET", "backup/safe", ct)).GetProperty("checked").GetProperty("count").GetInt32()).IsEqualTo(0);

        // A backup learns of it the same way.
        await PairedAsync(ct);
        await _computerPairs.ForgetAsync(phoneId, ct);
        var refused = await BackedUpAsync(ct);
        await Assert.That((refused.GetProperty("stage").GetString(), refused.GetProperty("problem").GetString()))
            .IsEqualTo(("unpaired", "Studio iMac does not know this phone any more. Pair them again: on the computer, open Phones and choose Pair a phone."));
        await Assert.That(await _phone.GetRequiredService<IBackupLedger>().ComputerAsync(ct)).IsNull();
    }

    [Test]
    public async Task The_Note_Accepted_On_The_Phone_Reaches_The_Computer_With_A_Backup(CancellationToken ct)
    {
        Library();
        await PairedAsync(ct);
        await _phone.GetRequiredService<TermsAcceptance>().HeardAsync(new DateOnly(2026, 10, 3), ct);

        await BackedUpAsync(ct);

        await Assert.That(await _computerTerms.AcceptedAsync(ct)).IsEqualTo(new DateOnly(2026, 10, 3));
    }

    [Test]
    public async Task The_Note_Accepted_On_The_Computer_Reaches_The_Phone_With_A_Backup(CancellationToken ct)
    {
        Library();
        await PairedAsync(ct);
        await _computerTerms.HeardAsync(new DateOnly(2026, 10, 2), ct);
        await Assert.That((await PhoneAsync("GET", "terms", ct)).GetProperty("accepted").ValueKind).IsEqualTo(JsonValueKind.Null);

        await BackedUpAsync(ct);

        await Assert.That((await PhoneAsync("GET", "terms", ct)).GetProperty("accepted").GetString()).IsEqualTo("2026-10-02");
    }

    [Test]
    public async Task Accepting_On_A_Paired_Phone_Tells_The_Computer_At_Once_Or_With_The_Next_Backup_When_It_Is_Not_Found(CancellationToken ct)
    {
        Library();
        await PairedAsync(ct);
        var found = _computers.Computers.ToList();
        _computers.Computers.Clear();

        var today = (await PhoneAsync("POST", "terms/accept", ct)).GetProperty("accepted").GetString();

        await Assert.That(await Backup.Telling).IsFalse();
        await Assert.That(await _computerTerms.AcceptedAsync(ct)).IsNull();
        _computers.Computers.AddRange(found);
        // Accepted again, as by a second press before the first answered, the computer is told now.
        await Assert.That((await PhoneAsync("POST", "terms/accept", ct)).GetProperty("accepted").GetString()).IsEqualTo(today);
        await Assert.That(await Backup.Telling).IsTrue();
        for (var waited = 0; await _computerTerms.AcceptedAsync(ct) is null; waited++)
        {
            if (waited == 400) throw new TimeoutException("Not accepted on the computer within 20 seconds.");
            await Task.Delay(50, ct);
        }
        await Assert.That(TermsAcceptance.Write(await _computerTerms.AcceptedAsync(ct))).IsEqualTo(today);
    }

    [Test]
    public async Task Paused_On_The_Computer_The_Phone_Is_Told_Where_To_Resume_And_Backups_Can_Go_To_Another_Folder(CancellationToken ct)
    {
        Library();
        await PairedAsync(ct);
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
        // Paused on the computer while a backup arrives: that backup is let finish, and the pause stays once it has.
        var phoneId = (await PhoneOnComputerAsync(ct)).GetProperty("id").GetString()!;
        var paused = (await _computer.PausePhoneAsync(Http.Post("backup/phones/x/pause", "{\"paused\":true}").FromClient(), phoneId, ct)).Json();
        await Assert.That(paused.GetProperty("paused").GetBoolean()).IsTrue();
        _library.Fetching = null;
        held.TrySetResult();
        await Backup.Running;
        await Assert.That((await NotedOnComputerAsync("4 items backed up and checked", ct)).GetProperty("paused").GetBoolean()).IsTrue();
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
        await Assert.That(Names(await NotedOnComputerAsync("1 item backed up and checked", ct)).Take(6)).IsEquivalentTo(
            ["1 item backed up and checked", "Receiving 1 item", $"Backups now go to {elsewhere}", "Backups resumed", "4 items backed up and checked", "Backups paused on this computer"],
            CollectionOrdering.Matching);

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
        await Assert.That((safe.GetProperty("computer").GetString(), safe.GetProperty("folder").GetString(), safe.GetProperty("lastChecked").ValueKind)).IsEqualTo(("Studio iMac", "Pictures/Trovolve/Jamie's iPhone", JsonValueKind.String));
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
            await neither.SafeAsync(Http.Get("backup/safe").FromClient(), ct), await neither.ForgetComputerAsync(Http.Post("backup/forget").FromClient(), ct),
        })
            await Assert.That((asked.StatusCode, asked.Text())).IsEqualTo((HttpStatusCode.NotFound, "Only a phone backs up."));
        foreach (var asked in new[]
        {
            await neither.PhonesAsync(Http.Get("backup/phones").FromClient(), ct), await neither.FolderAsync(Http.Post("backup/phones/x/folder", "{}").FromClient(), "x", ct),
            await neither.PausePhoneAsync(Http.Post("backup/phones/x/pause", "{}").FromClient(), "x", ct),
            await neither.ForgetPhoneAsync(Http.Post("backup/phones/x/forget").FromClient(), "x", ct),
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
        await channel.SendAsync(new System.Text.Json.Nodes.JsonObject { ["t"] = "held", ["sha256"] = new System.Text.Json.Nodes.JsonArray(), ["more"] = false, ["folder"] = "Pictures/Trovolve" }, ct);
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
        _library.Refuse = "You chose not to let Trovolve delete these photos.";
        await Assert.That(await AskAsync("POST", "backup/remove", ct, new { atMost = 2 })).IsEqualTo((HttpStatusCode.Conflict, "You chose not to let Trovolve delete these photos."));
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
        var computer = new PhoneBackup(new MemoryBackupLedger(), new FakeComputerFinder(), new MemoryPairs("pc"), services.GetRequiredService<Trovolve.Application.Organizing.OrganizeLibrary>(),
            TestFiles.Disk, TestPlans.Subscription(), TestPlans.Removals(), new FakeDevice(), new FakeFavorites(), "PC", In("Pictures"), new TestClock(), new MemoryVaultFiles(), new MemoryLocationStore(), TestPlans.Terms());
        var refused = await new BackupApi(new TestApi(s => s.AddSingleton(computer))).RemoveAsync(Http.Post("backup/remove").FromClient(), ct);
        await Assert.That((refused.StatusCode, refused.Text())).IsEqualTo((HttpStatusCode.Conflict, "Only a phone deletes what is on its computer."));
        var neither = await new BackupApi(new TestApi()).RemoveAsync(Http.Post("backup/remove").FromClient(), ct);
        await Assert.That(neither.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    private string TripFolder => Path.Combine(In("Home"), "Pictures", "Trovolve", "2026-07 Glacier National Park");

    /// <summary>
    /// Ten pictures taken at Glacier National Park on one day, in the library and as the scan recorded them, and home,
    /// Butte, photographed on three days; the trip's id.
    /// </summary>
    private async Task<string> TripAsync(CancellationToken ct)
    {
        for (var n = 0; n < 10; n++) _library.Add($"EEEEEEEE-{n}", (LibraryFileKind.Photo, $"GLACIER_{n}.JPG", MediaSamples.Picture($"TRIP-{n}")));
        var files = _phone.GetRequiredService<IPhotoFileSystem>();
        var repo = _phone.GetRequiredService<IPhotoRepository>();
        var minute = 0;
        foreach (var path in files.FilesIn(_root).Where(p => Path.GetFileName(p).StartsWith("GLACIER_", StringComparison.Ordinal)).Order())
            await repo.AddOrUpdateAsync(new Photo
            {
                SourcePath = path, FileName = Path.GetFileName(path), FileSizeBytes = files.Describe(path)!.Length, TakenOn = new DateTime(2026, 7, 18, 9, minute++, 0),
                Latitude = 48.6831, Longitude = -113.8003,
            }, ct);
        for (var day = 1; day <= 3; day++)
            await repo.AddOrUpdateAsync(new Photo { SourcePath = $"/elsewhere/HOME_{day}.JPG", FileName = $"HOME_{day}.JPG", TakenOn = new DateTime(2026, 1, day, 12, 0, 0), Latitude = 46.0038, Longitude = -112.5348 }, ct);
        return "20260718090000";
    }

    [Test]
    public async Task A_Trips_Folder_Is_Made_On_The_Computer_Sending_First_What_Is_Not_There_And_Its_Copies_Are_Not_Sent_Again(CancellationToken ct)
    {
        Library();
        var id = await TripAsync(ct);
        // Trip folders are part of Pro (Jamie, 2026-11-20).
        _app.Purchases.Add(new StorePurchase(PlanProducts.Lifetime, TestClock.Day, null, Trial: false));
        await PairedAsync(ct);

        var trips = await PhoneAsync("GET", "trips", ct);
        await Assert.That((trips.GetProperty("home").GetProperty("town").GetString(), trips.GetProperty("homeChosen").GetBoolean(), trips.GetProperty("computer").GetString(), trips.GetProperty("folder").ValueKind))
            .IsEqualTo(("Butte", false, "Studio iMac", JsonValueKind.Null));
        // Where the computer makes trip folders is known once it has said.
        await Assert.That(trips.GetProperty("tripsFolder").ValueKind).IsEqualTo(JsonValueKind.Null);
        var trip = trips.GetProperty("trips").EnumerateArray().Single();
        await Assert.That((trip.GetProperty("id").GetString(), trip.GetProperty("name").GetString(), trip.GetProperty("folder").GetString(), trip.GetProperty("start").GetString(), trip.GetProperty("photos").GetInt32(), trip.GetProperty("folderMade").GetBoolean()))
            .IsEqualTo((id, "Glacier National Park", "2026-07 Glacier National Park", "2026-07-18", 10, false));
        var days = (await PhoneAsync("GET", $"trips/{id}", ct)).GetProperty("days");
        await Assert.That((days.GetArrayLength(), days[0].GetProperty("date").GetString(), days[0].GetProperty("photos").GetArrayLength())).IsEqualTo((1, "2026-07-18", 10));

        await Assert.That((await AskAsync("POST", $"trips/{id}/folder", ct)).Status).IsEqualTo(HttpStatusCode.Accepted);
        await Backup.Running;
        var made = await PhoneAsync("GET", "trips", ct);
        var folder = made.GetProperty("folder");
        await Assert.That((folder.GetProperty("id").GetString(), folder.GetProperty("stage").GetString(), folder.GetProperty("made").GetString(), made.GetProperty("trips")[0].GetProperty("folderMade").GetBoolean()))
            .IsEqualTo((id, "ready", "Pictures/Trovolve/2026-07 Glacier National Park", true));
        await Assert.That(made.GetProperty("tripsFolder").GetString()).IsEqualTo("Pictures/Trovolve");
        await Assert.That(Directory.GetFiles(TripFolder).Length).IsEqualTo(10);
        await Assert.That(Directory.GetFiles(Folder).Length).IsEqualTo(0);
        var onComputer = await NotedOnComputerAsync("10 items backed up and checked", ct);
        await Assert.That(onComputer.GetProperty("tripFolders").GetInt32()).IsEqualTo(1);
        await Assert.That(Names(onComputer)).Contains("Made the trip folder Pictures/Trovolve/2026-07 Glacier National Park with 10 items");

        // Home chosen elsewhere, the trip is no longer one: what was made is said of no trip.
        var towns = await PhoneAsync("GET", "trips/towns", ct);
        await Assert.That((towns[0].GetProperty("town").GetString(), towns[0].GetProperty("days").GetInt32(), towns.GetArrayLength())).IsEqualTo(("Butte", 3, 2));
        await Assert.That((await AskAsync("POST", "trips/home", ct, new { town = towns[1] })).Status).IsEqualTo(HttpStatusCode.NoContent);
        var moved = await PhoneAsync("GET", "trips", ct);
        await Assert.That((moved.GetProperty("homeChosen").GetBoolean(), moved.GetProperty("trips").GetArrayLength(), moved.GetProperty("folder").ValueKind)).IsEqualTo((true, 0, JsonValueKind.Null));
        await Assert.That((await AskAsync("POST", "trips/home", ct, new { })).Status).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That((await PhoneAsync("GET", "trips", ct)).GetProperty("homeChosen").GetBoolean()).IsFalse();
        await Assert.That(await AskAsync("POST", "trips/home", ct, new { town = new { town = "" } })).IsEqualTo((HttpStatusCode.BadRequest, "Say which town is home."));
        await Assert.That((await AskAsync("POST", "trips/home", ct, "not a request")).Status).IsEqualTo(HttpStatusCode.BadRequest);

        // The next backup sends the rest, and none of the trip's copies again.
        var after = await BackedUpAsync(ct);
        await Assert.That((after.GetProperty("total").GetInt32(), after.GetProperty("onComputer").GetInt32())).IsEqualTo((4, 13));
        await Assert.That((Directory.GetFiles(TripFolder).Length, Directory.GetFiles(Folder).Length)).IsEqualTo((10, 4));
        // A backup of its own is no trip folder.
        await Assert.That((await PhoneAsync("GET", "trips", ct)).GetProperty("folder").ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    [Test]
    public async Task A_Trip_Named_By_The_Person_Gets_Its_Folder_Where_They_Chose_And_The_Phone_Learns_The_Computers_New_Name(CancellationToken ct)
    {
        Library();
        var id = await TripAsync(ct);
        _app.Purchases.Add(new StorePurchase(PlanProducts.Lifetime, TestClock.Day, null, Trial: false));
        await PairedAsync(ct);
        await BackedUpAsync(ct);
        var phoneId = (await NotedOnComputerAsync("14 items backed up and checked", ct)).GetProperty("id").GetString()!;

        await Assert.That((await AskAsync("POST", $"trips/{id}/name", ct, new { name = "Glacier with Sam" })).Status).IsEqualTo(HttpStatusCode.NoContent);
        var trip = (await PhoneAsync("GET", "trips", ct)).GetProperty("trips")[0];
        await Assert.That((trip.GetProperty("name").GetString(), trip.GetProperty("folder").GetString(), trip.GetProperty("suggested").GetString()))
            .IsEqualTo(("Glacier with Sam", "2026-07 Glacier with Sam", "Glacier National Park"));
        await Assert.That(await AskAsync("POST", $"trips/{id}/name", ct, new { name = new string('x', 61) })).IsEqualTo((HttpStatusCode.BadRequest, "A trip's name can be at most 60 letters."));
        await Assert.That((await AskAsync("POST", "trips/nope/name", ct, new { name = "x" })).Status).IsEqualTo(HttpStatusCode.NotFound);
        // Anything but a name gives it Trovolve's again; then the person's once more.
        await Assert.That((await AskAsync("POST", $"trips/{id}/name", ct, "not a request")).Status).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That((await PhoneAsync("GET", "trips", ct)).GetProperty("trips")[0].GetProperty("name").GetString()).IsEqualTo("Glacier National Park");
        await AskAsync("POST", $"trips/{id}/name", ct, new { name = "Glacier with Sam" });

        // On the computer: trip folders go in Pictures/Trovolve until the person chooses another folder.
        var phone = await PhoneOnComputerAsync(ct);
        await Assert.That((phone.GetProperty("tripsFolder").GetString(), phone.GetProperty("tripsPath").GetString())).IsEqualTo(("Pictures/Trovolve", Path.Combine(In("Home"), "Pictures", "Trovolve")));
        var trips = Directory.CreateDirectory(In("Trips")).FullName;
        var chosen = (await _computer.TripsFolderAsync(Http.Post("backup/phones/x/trips", JsonSerializer.Serialize(new { folder = trips })).FromClient(), phoneId, ct)).Json();
        await Assert.That(chosen.GetProperty("tripsFolder").GetString()).IsEqualTo(trips);
        var nowhere = await _computer.TripsFolderAsync(Http.Post("backup/phones/x/trips", JsonSerializer.Serialize(new { folder = In("Nowhere") })).FromClient(), phoneId, ct);
        await Assert.That((nowhere.StatusCode, nowhere.Text())).IsEqualTo((HttpStatusCode.BadRequest, $"Folder not found: {In("Nowhere")}"));
        foreach (var body in new[] { "{}", "not json" })
        {
            var none = await _computer.TripsFolderAsync(Http.Post("backup/phones/x/trips", body).FromClient(), phoneId, ct);
            await Assert.That((none.StatusCode, none.Text())).IsEqualTo((HttpStatusCode.BadRequest, "Choose a folder."));
        }
        var stranger = await _computer.TripsFolderAsync(Http.Post("backup/phones/x/trips", JsonSerializer.Serialize(new { folder = trips })).FromClient(), "nobody", ct);
        await Assert.That(stranger.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await new BackupApi(new TestApi()).TripsFolderAsync(Http.Post("backup/phones/x/trips", "{}").FromClient(), phoneId, ct)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        // The computer named again: the phone learns it at its next connection.
        await _door.RenameAsync("Office PC", ct);
        await Assert.That((await AskAsync("POST", $"trips/{id}/folder", ct)).Status).IsEqualTo(HttpStatusCode.Accepted);
        await Backup.Running;
        var made = await PhoneAsync("GET", "trips", ct);
        await Assert.That((made.GetProperty("computer").GetString(), made.GetProperty("tripsFolder").GetString(), made.GetProperty("folder").GetProperty("made").GetString()))
            .IsEqualTo(("Office PC", trips, Path.Combine(trips, "2026-07 Glacier with Sam")));
        await Assert.That(Directory.GetFiles(Path.Combine(trips, "2026-07 Glacier with Sam")).Length).IsEqualTo(10);
        await Assert.That((await PhoneAsync("GET", "backup", ct)).GetProperty("computer").GetString()).IsEqualTo("Office PC");
        await Assert.That(Names(await PhoneOnComputerAsync(ct))).Contains($"Trip folders now go in {trips}");
    }

    [Test]
    public async Task The_Phone_Has_The_Computer_Name_Its_Copies_By_Date_On_Pro_And_The_Computer_Shows_What_It_Would_Rename(CancellationToken ct)
    {
        Library();
        await Assert.That(await AskAsync("POST", "fixes/names", ct, new { kind = "date" })).IsEqualTo((HttpStatusCode.Conflict, "Pair with a computer first: in Settings, choose Computer."));
        await PairedAsync(ct);
        await BackedUpAsync(ct);
        var phoneId = (await NotedOnComputerAsync("4 items backed up and checked", ct)).GetProperty("id").GetString()!;
        var names = (await PhoneAsync("GET", "fixes", ct)).GetProperty("names");
        await Assert.That((names.GetProperty("computer").GetString(), names.GetProperty("files").GetInt32(), names.GetProperty("style").GetString(), names.GetProperty("now").ValueKind, names.GetProperty("stage").ValueKind))
            .IsEqualTo(("Studio iMac", 4, "asOnPhone", JsonValueKind.Null, JsonValueKind.Null));

        // On the computer: what it would rename, and how many.
        var preview = (await _computer.RenamingAsync(Http.Get("backup/phones/x/names?style=date").FromClient(), phoneId, ct)).Json();
        await Assert.That((preview.GetProperty("count").GetInt32(), preview.GetProperty("rows")[0].GetProperty("now").GetString())).IsEqualTo((4, "IMG_0001.JPG"));
        await Assert.That((await _computer.RenamingAsync(Http.Get("backup/phones/x/names?style=sideways").FromClient(), phoneId, ct)).StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await _computer.RenamingAsync(Http.Get("backup/phones/x/names?style=date").FromClient(), "nobody", ct)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        var free = await _computer.RenameAsync(Http.Post("backup/phones/x/names", JsonSerializer.Serialize(new { kind = "date" })).FromClient(), phoneId, ct);
        await Assert.That((free.StatusCode, free.Text().StartsWith("Renaming copies by date is part of Pro.", StringComparison.Ordinal))).IsEqualTo((HttpStatusCode.Conflict, true));
        foreach (var body in new[] { "{\"kind\":\"sideways\"}", "not json" })
            await Assert.That((await _computer.RenameAsync(Http.Post("backup/phones/x/names", body).FromClient(), phoneId, ct)).StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await _computer.RenameAsync(Http.Post("backup/phones/x/names", "{\"kind\":\"date\"}").FromClient(), "nobody", ct)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        var neither = new BackupApi(new TestApi());
        await Assert.That((await neither.RenamingAsync(Http.Get("backup/phones/x/names?style=date").FromClient(), phoneId, ct)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await neither.RenameAsync(Http.Post("backup/phones/x/names", "{}").FromClient(), phoneId, ct)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        // From the phone: Pro first.
        await Assert.That(await AskAsync("POST", "fixes/names", ct, new { kind = "date" })).IsEqualTo((HttpStatusCode.Conflict, "Renaming copies by date is part of Pro."));
        foreach (var body in new object[] { new { kind = "sideways" }, "not a request" })
            await Assert.That(await AskAsync("POST", "fixes/names", ct, body)).IsEqualTo((HttpStatusCode.BadRequest, "Say how to name the files."));
        _app.Purchases.Add(new StorePurchase(PlanProducts.Lifetime, TestClock.Day, null, Trial: false));
        await Assert.That((await AskAsync("POST", "fixes/names", ct, new { kind = "date" })).Status).IsEqualTo(HttpStatusCode.Accepted);
        await Backup.Running;
        names = (await PhoneAsync("GET", "fixes", ct)).GetProperty("names");
        await Assert.That((names.GetProperty("stage").GetString(), names.GetProperty("renamed").GetInt32(), names.GetProperty("style").GetString())).IsEqualTo(("ready", 4, "date"));
        await Assert.That(Directory.GetFiles(Folder).All(f => char.IsDigit(Path.GetFileName(f)[0]))).IsTrue();
        await Assert.That((await NotedOnComputerAsync("Renamed 4 items by date", ct)).GetProperty("names").GetString()).IsEqualTo("date");
        // Copies renamed are held where they are now: none is sent again.
        var again = await BackedUpAsync(ct);
        await Assert.That((again.GetProperty("total").GetInt32(), Directory.GetFiles(Folder).Length)).IsEqualTo((0, 4));
        var kept = (await _computer.RenameAsync(Http.Post("backup/phones/x/names", "{\"kind\":\"asOnPhone\"}").FromClient(), phoneId, ct)).Json();
        await Assert.That(kept.GetProperty("changed").GetInt32()).IsEqualTo(0);
    }

    [Test]
    public async Task Renaming_From_The_Phone_Waits_For_A_Backup_Running_And_Says_Why_The_Computer_Did_Not(CancellationToken ct)
    {
        Library();
        // The latest photo that says when it was taken gives the example of a name by date.
        var files = _phone.GetRequiredService<IPhotoFileSystem>();
        var picture = files.FilesIn(_root).Single(p => p.EndsWith("IMG_0002.JPG", StringComparison.Ordinal));
        await _phone.GetRequiredService<IPhotoRepository>().AddOrUpdateAsync(new Photo { SourcePath = picture, FileName = "IMG_0002.JPG", TakenOn = new DateTime(2026, 3, 2, 14, 31, 7) }, ct);
        _app.Purchases.Add(new StorePurchase(PlanProducts.Lifetime, TestClock.Day, null, Trial: false));
        var computer = await ScriptedComputerAsync(ct);
        await Assert.That((await AskAsync("POST", "fixes/names", ct, new { kind = "datePlace" })).Status).IsEqualTo(HttpStatusCode.Accepted);
        var channel = await computer.WaitAsync(ct);
        await channel.ReceiveAsync(ct, "backup");
        // A way of naming this phone does not know is left as it was.
        await channel.SendAsync(new System.Text.Json.Nodes.JsonObject { ["t"] = "held", ["sha256"] = new System.Text.Json.Nodes.JsonArray(), ["more"] = false, ["folder"] = "Pictures/Trovolve", ["names"] = "sideways" }, ct);
        var offer = await channel.ReceiveAsync(ct, "offer");
        await Assert.That(((System.Text.Json.Nodes.JsonArray?)offer["files"])?.Count ?? 0).IsEqualTo(0);
        await Assert.That(await AskAsync("POST", "fixes/names", ct, new { kind = "date" })).IsEqualTo((HttpStatusCode.Conflict, "A backup to Studio iMac is running. Rename the files once it ends."));
        await Assert.That((await PhoneAsync("GET", "fixes", ct)).GetProperty("names").GetProperty("stage").GetString()).IsEqualTo("running");
        await channel.SendAsync(new System.Text.Json.Nodes.JsonObject { ["t"] = "done" }, ct);
        await Assert.That(Wire.Text(await channel.ReceiveAsync(ct, "names"), "style")).IsEqualTo("DatePlace");
        await channel.SendAsync(new System.Text.Json.Nodes.JsonObject { ["t"] = "named", ["problem"] = "The copies could not be renamed. The disk is full." }, ct);
        await Backup.Running;

        var names = (await PhoneAsync("GET", "fixes", ct)).GetProperty("names");
        await Assert.That((names.GetProperty("stage").GetString(), names.GetProperty("problem").GetString(), names.GetProperty("renamed").ValueKind, names.GetProperty("style").GetString()))
            .IsEqualTo(("failed", "The copies could not be renamed. The disk is full.", JsonValueKind.Null, "asOnPhone"));
        await Assert.That((names.GetProperty("now").GetString(), names.GetProperty("after").GetString())).IsEqualTo(("IMG_0002.JPG", "2026-03-02 14.31.07.JPG"));
    }

    [Test]
    public async Task A_Trip_Folder_Needs_A_Computer_Waits_For_A_Backup_Running_And_Says_Why_The_Computer_Refused(CancellationToken ct)
    {
        Library();
        var id = await TripAsync(ct);
        // On the free plan a trip folder is refused before anything else: it is part of Pro (Jamie, 2026-11-20).
        await Assert.That(await AskAsync("POST", $"trips/{id}/folder", ct)).IsEqualTo((HttpStatusCode.PaymentRequired, "Trip folders on your computer are part of Pro."));
        _app.Purchases.Add(new StorePurchase(PlanProducts.Lifetime, TestClock.Day, null, Trial: false));
        await Assert.That(await AskAsync("POST", $"trips/{id}/folder", ct)).IsEqualTo((HttpStatusCode.Conflict, "Pair with a computer first: in Settings, choose Computer."));
        await Assert.That((await PhoneAsync("GET", "trips", ct)).GetProperty("computer").ValueKind).IsEqualTo(JsonValueKind.Null);
        foreach (var (method, path) in new[] { ("GET", "trips/nope"), ("POST", "trips/nope/folder") })
            await Assert.That(await AskAsync(method, path, ct)).IsEqualTo((HttpStatusCode.NotFound, "That trip is no longer there. Its photos may have changed since."));

        var computer = await ScriptedComputerAsync(ct);
        await Assert.That((await AskAsync("POST", $"trips/{id}/folder", ct)).Status).IsEqualTo(HttpStatusCode.Accepted);
        var channel = await computer.WaitAsync(ct);
        await channel.ReceiveAsync(ct, "backup");
        await channel.SendAsync(new System.Text.Json.Nodes.JsonObject { ["t"] = "held", ["sha256"] = new System.Text.Json.Nodes.JsonArray(), ["more"] = false, ["folder"] = "Pictures/Trovolve" }, ct);
        var offer = await channel.ReceiveAsync(ct, "offer");
        // Only the trip's pictures are offered.
        await Assert.That(((System.Text.Json.Nodes.JsonArray)offer["files"]!).Count).IsEqualTo(10);
        await Assert.That(await AskAsync("POST", $"trips/{id}/folder", ct)).IsEqualTo((HttpStatusCode.Conflict, "A backup to Studio iMac is running. Make the folder once it ends."));
        await Assert.That((await PhoneAsync("GET", "trips", ct)).GetProperty("folder").GetProperty("stage").GetString()).IsEqualTo("running");
        await channel.SendAsync(new System.Text.Json.Nodes.JsonObject { ["t"] = "done" }, ct);
        var asked = await channel.ReceiveAsync(ct, "trip");
        await Assert.That(Wire.Text(asked, "folder")).IsEqualTo("2026-07 Glacier National Park");
        await channel.SendAsync(new System.Text.Json.Nodes.JsonObject { ["t"] = "trip-made", ["problem"] = "Pictures/Trovolve/2026-07 Glacier National Park could not be made. The disk is full." }, ct);
        await Backup.Running;

        var failed = (await PhoneAsync("GET", "trips", ct)).GetProperty("folder");
        await Assert.That((failed.GetProperty("stage").GetString(), failed.GetProperty("problem").GetString(), failed.GetProperty("made").ValueKind))
            .IsEqualTo(("failed", "Pictures/Trovolve/2026-07 Glacier National Park could not be made. The disk is full.", JsonValueKind.Null));
        var after = await PhoneAsync("GET", "trips", ct);
        // A computer from before trip folders does not say where it makes them.
        await Assert.That((after.GetProperty("trips")[0].GetProperty("folderMade").GetBoolean(), after.GetProperty("tripsFolder").ValueKind)).IsEqualTo((false, JsonValueKind.Null));
    }

    [Test]
    public async Task Only_A_Phone_Has_Trips_And_Only_The_Page_May_Ask(CancellationToken ct)
    {
        var computer = await new TestApi().SendAsync(Http.Get("trips").FromClient(), ct, "trips");
        await Assert.That((computer.StatusCode, computer.Text())).IsEqualTo((HttpStatusCode.NotFound, "Only a phone has Trips."));
        foreach (var (method, path) in new[] { ("GET", "trips"), ("GET", "trips/towns"), ("POST", "trips/home"), ("GET", "trips/x"), ("POST", "trips/x/folder"), ("POST", "trips/x/name") })
        {
            var stranger = await _router.AnswerAsync(method, new Uri("app://trovolve/api/" + path), [], null, ct);
            await Assert.That(stranger.Status).IsEqualTo((int)HttpStatusCode.Unauthorized);
        }
        // No photo says where it was taken: there is no home, and no trip.
        var none = await PhoneAsync("GET", "trips", ct);
        await Assert.That((none.GetProperty("home").ValueKind, none.GetProperty("homeChosen").GetBoolean(), none.GetProperty("trips").GetArrayLength())).IsEqualTo((JsonValueKind.Null, false, 0));
    }
}
