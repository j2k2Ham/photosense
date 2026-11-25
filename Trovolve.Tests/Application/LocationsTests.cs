using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Trovolve.Api;
using Trovolve.Api.About;
using Trovolve.Api.Hosting;
using Trovolve.Api.Http;
using Trovolve.Application.Locations;
using Trovolve.Application.Organizing;
using Trovolve.Application.Plans;
using Trovolve.Application.Transfer;
using Trovolve.Desktop.Imaging;
using Trovolve.Domain.Entities;
using Trovolve.Domain.Repositories;
using Trovolve.Domain.Services;
using Trovolve.Infrastructure.Library;
using Trovolve.Infrastructure.Transfer;
using Trovolve.Domain.ValueObjects;
using Trovolve.Tests.Endpoints;

namespace Trovolve.Tests.Application;

/// <summary>
/// The places an iPhone backs up to, through its whole stack and the page's addresses: the computer, the cloud services
/// signed in to, and those Connect to server adds; backing up to them, each copy checked; checking them again; sending
/// to one and deleting what matched; and Safe to delete counting a copy on any location, or on two.
/// </summary>
public sealed class LocationsTests : IDisposable
{
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("trovolve-locations-");
    private readonly FakePhotoLibrary _library = new();
    private readonly FakeAppStore _app = new();
    private readonly FakeDevice _device = new();
    private readonly FakeWebSignIn _signIn = new();
    private readonly MemoryLocationSecrets _secrets = new();
    private readonly MemoryFolders _folders = new();
    private readonly FakeCloudAccounts _accounts = new();
    private readonly ServiceProvider _phone;
    private readonly ApiRouter _router;

    public LocationsTests()
    {
        _phone = new ServiceCollection()
            .AddTrovolvePhone(_library, new FakePhotoAccess(), new MagickImageAnalyzer(), In("Documents"), In("Caches"), new FakeAdvertiser(), _app, new FakeCodeScanner(),
                new FakeComputerFinder(), _device, new FakeNotifier(), new FakeShareSheet(), new FakeVaultKeys(), new FakeUnlocker(), new FakeWidgets(), _secrets, _signIn,
                "Jamie's iPhone", In("Photo Library"), new TcpTransferListener(0), network: new FakeNetwork())
            .AddTrovolveApi()
            .AddSingleton(new ProgramInfo("iphone", "1.2.0.57", () => "notices"))
            .AddStandInStores()
            .AddSingleton<TimeProvider>(new TestClock())
            // The folders and the services' sign-ins, in memory.
            .AddSingleton<IRemoteFolders>(_folders)
            .AddSingleton<ICloudAccounts>(_accounts)
            .BuildServiceProvider();
        _router = _phone.GetRequiredService<TrovolveRoutes>().Router;
        _signIn.Back = start => new Uri($"com.j2k2ham.trovolve.auth:/{start.Segments[^1]}?code=c&state=state");
    }

    public void Dispose()
    {
        Locations.Pause();
        _phone.GetRequiredService<PhoneBackup>().Pause();
        _phone.Dispose();
        TestFiles.Remove(_data);
    }

    private string In(string folder) => Path.Combine(_data.FullName, folder);

    private BackupLocations Locations => _phone.GetRequiredService<BackupLocations>();

    private ILocationStore Store => _phone.GetRequiredService<ILocationStore>();

    private void Pro() => _app.Purchases.Add(new StorePurchase(PlanProducts.Lifetime, TestClock.Day, null, Trial: false));

    private Task PairedAsync(CancellationToken ct)
        => _phone.GetRequiredService<IBackupLedger>().SaveComputerAsync(new BackupComputer("computer-1", "Studio iMac", "Pictures/Trovolve", null, null), ct);

    private async Task<(HttpStatusCode Status, string Text)> AskAsync(string method, string path, CancellationToken ct, object? body = null)
    {
        var answer = await _router.AnswerAsync(method, new Uri("app://trovolve/api/" + path), [new("x-trovolve-client", "phone")], body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body), ct);
        return ((HttpStatusCode)answer.Status, Encoding.UTF8.GetString(answer.Body));
    }

    private async Task<JsonElement> JsonAsync(string path, CancellationToken ct)
    {
        var (status, text) = await AskAsync("GET", path, ct);
        await Assert.That(status).IsEqualTo(HttpStatusCode.OK).Because(text);
        return JsonDocument.Parse(text).RootElement;
    }

    private async Task SaysAsync(string method, string path, HttpStatusCode expected, string? says, CancellationToken ct, object? body = null)
    {
        var (status, text) = await AskAsync(method, path, ct, body);
        await Assert.That(status).IsEqualTo(expected).Because(text);
        if (says is not null) await Assert.That(text).Contains(says);
    }

    private static Dictionary<string, string> S3Settings => new() { ["region"] = "us-west-2 (Oregon)", ["bucket"] = "photos-archive", ["akid"] = "AKIA7Q2F" };

    private async Task<string> AddS3Async(CancellationToken ct, string name = "Archive")
    {
        var (status, text) = await AskAsync("POST", "locations/connect", ct, new { kind = "amazonS3", name, settings = S3Settings, secrets = new Dictionary<string, string> { ["secret"] = "s" } });
        await Assert.That(status).IsEqualTo(HttpStatusCode.OK).Because(text);
        return JsonDocument.Parse(text).RootElement.GetProperty("id").GetString()!;
    }

    private void Pictures()
    {
        _library.Add("A", (LibraryFileKind.Photo, "IMG_0001.JPG", MediaSamples.Picture("A")));
        _library.Add("B", (LibraryFileKind.Photo, "IMG_0002.JPG", MediaSamples.Picture("B")));
        _library.Add("V", (LibraryFileKind.Video, "IMG_0003.MOV", MediaSamples.Movie("V")));
    }

    [Test]
    public async Task Lists_The_Computer_Then_The_Cloud_Services_Then_Those_Added_And_Turns_Them_On_And_Off(CancellationToken ct)
    {
        var listed = await JsonAsync("locations", ct);
        var ids = listed.GetProperty("locations").EnumerateArray().Select(l => l.GetProperty("id").GetString()!).ToList();
        await Assert.That(ids).IsEquivalentTo(["computer", "google-drive", "onedrive", "dropbox"], CollectionOrdering.Matching);
        // Not paired: the computer cannot be turned on.
        var computer = listed.GetProperty("locations")[0];
        await Assert.That((computer.GetProperty("on").GetBoolean(), computer.GetProperty("connected").GetBoolean(), computer.GetProperty("kind").GetString())).IsEqualTo((false, false, "computer"));
        await SaysAsync("POST", "locations/computer/on", HttpStatusCode.Conflict, "Pair with a computer first", ct);
        await Assert.That(listed.GetProperty("countAt").GetInt32()).IsEqualTo(1);
        await Assert.That(listed.GetProperty("pro").GetBoolean()).IsFalse();
        await Assert.That(listed.GetProperty("activity").ValueKind).IsEqualTo(JsonValueKind.Null);

        // Paired, it is on, named as the computer is, and says where its copies go.
        await PairedAsync(ct);
        computer = (await JsonAsync("locations", ct)).GetProperty("locations")[0];
        await Assert.That((computer.GetProperty("name").GetString(), computer.GetProperty("on").GetBoolean())).IsEqualTo(("Studio iMac", true));
        var details = await JsonAsync("locations/computer", ct);
        await Assert.That(details.GetProperty("where").GetString()).IsEqualTo("Pictures/Trovolve");
        await SaysAsync("POST", "locations/computer/off", HttpStatusCode.NoContent, null, ct);
        await Assert.That((await Locations.StateAsync(ct)).Locations[0].On).IsFalse();
        await SaysAsync("POST", "locations/computer/on", HttpStatusCode.NoContent, null, ct);
        await Assert.That((await Locations.StateAsync(ct)).Locations[0].On).IsTrue();
        await SaysAsync("GET", "locations/nowhere", HttpStatusCode.NotFound, "There is no such location", ct);
    }

    [Test]
    public async Task A_Cloud_Service_Is_Signed_In_To_As_It_Is_Turned_On_And_The_Free_Plan_Keeps_One_On_Besides_The_Computer(CancellationToken ct)
    {
        await PairedAsync(ct);
        // The free plan backs up to the computer and one location besides: Google Drive goes on beside it.
        await SaysAsync("POST", "locations/google-drive/on", HttpStatusCode.NoContent, null, ct);
        await Assert.That(_signIn.Opened.Single().Start.AbsoluteUri).IsEqualTo("https://sign-in.example/google-drive");
        var drive = (await Locations.StateAsync(ct)).Locations.Single(l => l.Id == "google-drive");
        await Assert.That((drive.Connected, drive.On, drive.Account)).IsEqualTo((true, true, "alex.m@gmail.com"));
        // A second besides the computer is offered in Google Drive's place.
        await SaysAsync("POST", "locations/dropbox/on", HttpStatusCode.PaymentRequired, "The free plan backs up to your computer and one other location, and Google Drive is on.", ct);
        var limit = await Assert.ThrowsAsync<LocationLimitException>(() => Locations.TurnOnAsync("dropbox", ct));
        await Assert.That((limit!.Current, limit.Wanted, limit.Held)).IsEqualTo(("Google Drive", "dropbox", false));
        // Signed in already: Use instead asks nothing more, and the computer stays on.
        await SaysAsync("POST", "locations/dropbox/instead", HttpStatusCode.NoContent, null, ct);
        await Assert.That((await Locations.StateAsync(ct)).Locations.Where(l => l.On).Select(l => l.Id)).IsEquivalentTo(["computer", "dropbox"]);
        await Assert.That(_signIn.Opened).Count().IsEqualTo(2);
        // Before any backup has gone to it, the one on goes off, and another goes on in its place.
        await SaysAsync("POST", "locations/dropbox/off", HttpStatusCode.NoContent, null, ct);
        await SaysAsync("POST", "locations/google-drive/on", HttpStatusCode.NoContent, null, ct);
        await SaysAsync("POST", "locations/google-drive/off", HttpStatusCode.NoContent, null, ct);
        await SaysAsync("POST", "locations/dropbox/on", HttpStatusCode.NoContent, null, ct);
        await Assert.That((await Locations.StateAsync(ct)).Locations.Where(l => l.On).Select(l => l.Id)).IsEquivalentTo(["computer", "dropbox"]);
        // The computer goes off and on whatever else is on.
        await SaysAsync("POST", "locations/computer/off", HttpStatusCode.NoContent, null, ct);
        await SaysAsync("POST", "locations/computer/on", HttpStatusCode.NoContent, null, ct);

        // On Pro all are on; when Pro ends, the computer stays, and of the rest the one used last.
        Pro();
        await Locations.TurnOnAsync("google-drive", ct);
        await Assert.That((await Locations.StateAsync(ct)).Locations.Count(l => l.On)).IsEqualTo(3);
        var dropbox = (await Store.ListAsync(ct)).Single(l => l.Id == "dropbox");
        await Store.SaveAsync(dropbox with { LastUsed = TestClock.Day.AddDays(1) }, ct);
        _app.Purchases.Clear();
        await Assert.That((await Locations.StateAsync(ct)).Locations.Where(l => l.On).Select(l => l.Id)).IsEquivalentTo(["computer", "dropbox"]);

        // Signed out: its tokens and what it held are forgotten; only a cloud service is signed out of.
        await SaysAsync("POST", "locations/google-drive/disconnect", HttpStatusCode.NoContent, null, ct);
        await Assert.That(_secrets.Kept.ContainsKey("google-drive")).IsFalse();
        await Assert.That((await Locations.StateAsync(ct)).Locations.Single(l => l.Id == "google-drive").Connected).IsFalse();
        await SaysAsync("POST", "locations/computer/disconnect", HttpStatusCode.Conflict, "Only a cloud service is signed out of.", ct);
    }

    [Test]
    public async Task Once_Backups_Have_Gone_To_A_Location_Besides_The_Computer_The_Free_Plan_Is_Held_To_It_And_Only_Pro_Adds_Another(CancellationToken ct)
    {
        // Backed up to: the computer is never held, and goes off and on as the person likes.
        await _phone.GetRequiredService<IBackupLedger>().SaveComputerAsync(new BackupComputer("computer-1", "Studio iMac", "Pictures/Trovolve", null, TestClock.Day), ct);
        await Assert.That((await JsonAsync("locations", ct)).GetProperty("held").ValueKind).IsEqualTo(JsonValueKind.Null);
        await SaysAsync("POST", "locations/computer/off", HttpStatusCode.NoContent, null, ct);
        await SaysAsync("POST", "locations/computer/on", HttpStatusCode.NoContent, null, ct);

        // Google Drive goes on beside it; once backed up to, the free plan is held to it.
        await Locations.TurnOnAsync("google-drive", ct);
        var drive = (await Store.ListAsync(ct)).Single(l => l.Id == "google-drive");
        await Store.SaveAsync(drive with { LastBackup = TestClock.Day }, ct);
        await Assert.That((await JsonAsync("locations", ct)).GetProperty("held").GetString()).IsEqualTo("google-drive");
        await SaysAsync("POST", "locations/dropbox/on", HttpStatusCode.PaymentRequired, "The free plan backs up to your computer and one other location, and your backups are on Google Drive.", ct);
        var limit = await Assert.ThrowsAsync<LocationLimitException>(() => Locations.TurnOnAsync("dropbox", ct));
        await Assert.That(limit!.Held).IsTrue();
        await SaysAsync("POST", "locations/dropbox/instead", HttpStatusCode.PaymentRequired, "your backups are on Google Drive", ct);
        await SaysAsync("POST", "locations/google-drive/off", HttpStatusCode.PaymentRequired,
            "Google Drive is the only location besides your computer the free plan can use, as your backups are there.", ct);
        await SaysAsync("POST", "locations/google-drive/disconnect", HttpStatusCode.PaymentRequired, "Google Drive is the only location besides your computer", ct);
        // The computer is still free to go off and on.
        await SaysAsync("POST", "locations/computer/off", HttpStatusCode.NoContent, null, ct);
        await SaysAsync("POST", "locations/computer/on", HttpStatusCode.NoContent, null, ct);
        await Assert.That((await Locations.StateAsync(ct)).Locations.Where(l => l.On).Select(l => l.Id)).IsEquivalentTo(["computer", "google-drive"]);

        // On Pro nothing is held: another goes on, and any goes off.
        Pro();
        await Assert.That((await JsonAsync("locations", ct)).GetProperty("held").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Locations.TurnOnAsync("dropbox", ct);
        var archive = await AddS3Async(ct);
        await SaysAsync("POST", "locations/google-drive/off", HttpStatusCode.NoContent, null, ct);
        await SaysAsync("POST", "locations/dropbox/off", HttpStatusCode.NoContent, null, ct);

        // A server backed up to, once Pro ends, is held and cannot be removed; one not held is signed out of as before.
        var server = (await Store.ListAsync(ct)).Single(l => l.Id == archive);
        await Store.SaveAsync(server with { LastBackup = TestClock.Day }, ct);
        _app.Purchases.Clear();
        await Assert.That((await Locations.StateAsync(ct)).Held).IsEqualTo(archive);
        await SaysAsync("POST", $"locations/{archive}/remove", HttpStatusCode.PaymentRequired, "Archive is the only location besides your computer", ct);
        await SaysAsync("POST", "locations/dropbox/disconnect", HttpStatusCode.NoContent, null, ct);
        // On Pro again, it is removed.
        Pro();
        await SaysAsync("POST", $"locations/{archive}/remove", HttpStatusCode.NoContent, null, ct);
        await Assert.That((await Locations.StateAsync(ct)).Locations.Any(l => l.Id == archive)).IsFalse();
    }

    [Test]
    public async Task Signing_In_Closed_Refused_Or_Not_Yet_Possible_Changes_Nothing_And_Says_Why(CancellationToken ct)
    {
        _signIn.Back = _ => null;
        await SaysAsync("POST", "locations/dropbox/on", HttpStatusCode.Conflict, "Signing in was closed. Nothing changed.", ct);
        _signIn.Back = start => new Uri("com.j2k2ham.trovolve.auth:/dropbox?error=access_denied");
        _accounts.Finish = (_, _) => throw new IOException("Dropbox was not connected: access was not allowed.");
        await SaysAsync("POST", "locations/dropbox/on", HttpStatusCode.BadGateway, "access was not allowed", ct);
        _accounts.Registered.Remove(LocationKind.OneDrive);
        await SaysAsync("POST", "locations/onedrive/on", HttpStatusCode.Conflict, "OneDrive can be connected once Trovolve is registered with it.", ct);
        var listed = (await Locations.StateAsync(ct)).Locations;
        await Assert.That(listed.Single(l => l.Id == "onedrive").Available).IsFalse();
        await Assert.That(listed.Where(l => l.Kind != LocationKind.Computer).All(l => !l.Connected && !l.On)).IsTrue();
        // Details of one not connected ask nothing of it.
        await Assert.That((await Locations.DetailsAsync("dropbox", ct)).Space).IsNull();
    }

    [Test]
    public async Task Connect_To_Server_Is_Pro_Checks_The_Location_Step_By_Step_And_Names_It_As_The_Person_Wants(CancellationToken ct)
    {
        await SaysAsync("POST", "locations/connect", HttpStatusCode.PaymentRequired, "Connecting your own server", ct, new { kind = "amazonS3", name = "", settings = S3Settings });
        Pro();
        await SaysAsync("POST", "locations/connect", HttpStatusCode.BadRequest, "Choose the kind of server.", ct, new { kind = "smb" });
        await SaysAsync("POST", "locations/connect", HttpStatusCode.BadRequest, "Choose the kind of server.", ct, new { kind = "googleDrive" });
        await SaysAsync("POST", "locations/connect", HttpStatusCode.Conflict, "Enter the secret key.", ct, new { kind = "amazonS3", settings = S3Settings });
        await SaysAsync("POST", "locations/connect", HttpStatusCode.Conflict, "Enter the bucket.", ct, new { kind = "amazonS3", settings = new Dictionary<string, string> { ["region"] = "x" } });

        // Each step of the check says where it failed, the location's own words after.
        async Task FailsAsync(string step, Exception failure, string says)
        {
            var folder = new MemoryFolder();
            folder.Fails[step] = failure;
            await Assert.That(await CheckAsync(new AnyFolder(folder), ct)).Contains(says);
        }
        await FailsAsync("list", new IOException("The bucket is not there."), "Trovolve could not reach Archive. The bucket is not there.");
        await FailsAsync("put", new IOException("Allow s3:PutObject."), "Archive let Trovolve list its folder but not write to it. Allow s3:PutObject.");
        await FailsAsync("read", new UnauthorizedAccessException("No reading."), "Archive took the test file but would not give it back. No reading.");
        await FailsAsync("delete", new IOException("No deleting."), "Archive let Trovolve write but not delete its test file, .trovolve-check-");
        var damaged = new MemoryFolder { Damage = bytes => [.. bytes, 1] };
        await Assert.That(await CheckAsync(new AnyFolder(damaged), ct)).Contains("Archive gave back something other than the test file Trovolve wrote.");

        // Checked: kept, on, its test file gone, named as asked, its secrets kept apart.
        var id = await AddS3Async(ct);
        var added = (await Locations.StateAsync(ct)).Locations.Single(l => l.Id == id);
        await Assert.That((added.Name, added.Kind, added.On, added.Account)).IsEqualTo(("Archive", LocationKind.AmazonS3, true, (string?)null));
        await Assert.That(_folders.Folders[id].Files).IsEmpty();
        await Assert.That(_secrets.Kept[id]["secret"]).IsEqualTo("s");
        await Assert.That(added.Settings.ContainsKey("secret")).IsFalse();
        // A name left empty is the kind's.
        var plain = await Locations.ConnectServerAsync(new ServerConnection(null, LocationKind.WebDav, " ", new Dictionary<string, string> { ["url"] = "https://nas.local/dav", ["user"] = "alex" },
            new Dictionary<string, string> { ["pass"] = "p" }), ct);
        var webDav = (await Locations.StateAsync(ct)).Locations.Single(l => l.Id == plain);
        await Assert.That((webDav.Name, webDav.Account)).IsEqualTo(("WebDAV", "alex"));

        // Renamed at any time; only one added is, and never to nothing.
        await SaysAsync("POST", $"locations/{id}/name", HttpStatusCode.NoContent, null, ct, new { name = " Photos archive " });
        await Assert.That((await Locations.StateAsync(ct)).Locations.Single(l => l.Id == id).Name).IsEqualTo("Photos archive");
        await SaysAsync("POST", $"locations/{id}/name", HttpStatusCode.Conflict, "Enter a name.", ct, new { name = "" });
        await SaysAsync("POST", "locations/dropbox/name", HttpStatusCode.Conflict, "Dropbox keeps its own name.", ct, new { name = "Mine" });
        // Those added are listed by name after the cloud services.
        await Assert.That((await Locations.StateAsync(ct)).Locations.Skip(4).Select(l => l.Name)).IsEquivalentTo(["Photos archive", "WebDAV"], CollectionOrdering.Matching);

        // Removed: forgotten with its secrets; nothing there is deleted. Only one added is removed.
        await SaysAsync("POST", $"locations/{plain}/remove", HttpStatusCode.NoContent, null, ct);
        await Assert.That(_secrets.Kept.ContainsKey(plain)).IsFalse();
        await SaysAsync("POST", "locations/onedrive/remove", HttpStatusCode.Conflict, "Only a location added by Connect to server is removed.", ct);
    }

    // Connect to server's check against this folder; what it said, or nothing when it passed.
    private async Task<string> CheckAsync(IRemoteFolders folders, CancellationToken ct)
    {
        var locations = new BackupLocations(Store, _secrets, folders, _accounts, _signIn, _phone.GetRequiredService<PhoneBackup>(), _phone.GetRequiredService<IBackupLedger>(),
            _phone.GetRequiredService<OrganizeLibrary>(), _phone.GetRequiredService<IPhotoFileSystem>(), (IPhotoChangesTogether)_phone.GetRequiredService<IPhotoFileSystem>(),
            _phone.GetRequiredService<Subscription>(), _phone.GetRequiredService<DailyRemovals>(), _device, In("Photo Library"), new TestClock(), NullLogger<BackupLocations>.Instance);
        var failed = await Assert.ThrowsAsync<IOException>(() => locations.ConnectServerAsync(new ServerConnection(null, LocationKind.AmazonS3, "Archive", S3Settings,
            new Dictionary<string, string> { ["secret"] = "s" }), ct));
        return failed!.Message;
    }

    /// <summary>Gives every location the one folder.</summary>
    private sealed class AnyFolder(MemoryFolder folder) : IRemoteFolders
    {
        public IRemoteFolder Open(BackupLocation location, IReadOnlyDictionary<string, string> secrets, Func<IReadOnlyDictionary<string, string>, CancellationToken, Task> renewed) => folder;
    }

    [Test]
    public async Task Changing_A_Location_Checks_It_Again_And_Forgets_What_It_Held_Only_When_It_Points_Elsewhere(CancellationToken ct)
    {
        Pro();
        Pictures();
        var id = await AddS3Async(ct);
        await Locations.BackupAsync(ct);
        await Locations.Running;
        await Assert.That((await Store.CopiesAsync(id, ct)).Count).IsEqualTo(3);
        // The same bucket, the name changed: what it holds stays known.
        await Locations.ConnectServerAsync(new ServerConnection(id, LocationKind.AmazonS3, "Same", S3Settings, new Dictionary<string, string> { ["secret"] = "s2" }), ct);
        await Assert.That((await Store.CopiesAsync(id, ct)).Count).IsEqualTo(3);
        await Assert.That(_secrets.Kept[id]["secret"]).IsEqualTo("s2");
        // Another bucket: nothing there is known yet.
        var elsewhere = new Dictionary<string, string>(S3Settings) { ["bucket"] = "other" };
        await Locations.ConnectServerAsync(new ServerConnection(id, LocationKind.AmazonS3, "Other", elsewhere, new Dictionary<string, string> { ["secret"] = "s2" }), ct);
        await Assert.That(await Store.CopiesAsync(id, ct)).IsEmpty();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Locations.ConnectServerAsync(new ServerConnection("gone", LocationKind.AmazonS3, "x", S3Settings,
            new Dictionary<string, string> { ["secret"] = "s" }), ct));
    }

    [Test]
    public async Task Backing_Up_Sends_Each_Location_What_It_Lacks_Checks_Each_Copy_And_Never_Replaces_A_Name_There(CancellationToken ct)
    {
        await SaysAsync("POST", "locations/backup", HttpStatusCode.Conflict, "Turn on a location first", ct);
        Pro();
        Pictures();
        // The same picture twice in the library goes once.
        _library.Add("C", (LibraryFileKind.Photo, "IMG_0009.JPG", MediaSamples.Picture("A")));
        var s3 = await AddS3Async(ct);
        // A name already there, which is not replaced: the copy takes a number.
        _folders.Folders[s3].Files["IMG_0001.JPG"] = [9];
        // WebDAV keeps no checksum: what arrives is read back.
        var nas = await Locations.ConnectServerAsync(new ServerConnection(null, LocationKind.WebDav, "NAS", new Dictionary<string, string> { ["url"] = "https://nas/dav", ["user"] = "a" },
            new Dictionary<string, string> { ["pass"] = "p" }), ct);
        _folders.Folders[nas].Keeps = null;
        await SaysAsync("POST", "locations/backup", HttpStatusCode.Accepted, null, ct);
        await Locations.Running;

        var s3Files = _folders.Folders[s3].Files.Keys.Order().ToList();
        await Assert.That(s3Files).IsEquivalentTo(["IMG_0001 (1).JPG", "IMG_0001.JPG", "IMG_0002.JPG", "IMG_0003.MOV"], CollectionOrdering.Matching);
        await Assert.That(_folders.Folders[s3].Files["IMG_0001.JPG"]).IsEquivalentTo(new byte[] { 9 });
        await Assert.That((await Store.CopiesAsync(s3, ct)).Values.Select(c => c.Checksum?.Kind)).All().Satisfy(k => k.IsEqualTo(ChecksumKind.Sha256));
        await Assert.That((await Store.CopiesAsync(nas, ct)).Values.Select(c => c.Checksum)).All().Satisfy(c => c.IsNull());
        await Assert.That(_folders.Folders[nas].Files).Count().IsEqualTo(3);

        var activity = (await JsonAsync("locations", ct)).GetProperty("activity");
        await Assert.That((activity.GetProperty("task").GetString(), activity.GetProperty("stage").GetString())).IsEqualTo(("backup", "ready"));
        var part = activity.GetProperty("locations").EnumerateArray().First(p => p.GetProperty("id").GetString() == s3);
        await Assert.That((part.GetProperty("done").GetInt32(), part.GetProperty("total").GetInt32())).IsEqualTo((4, 4));
        var listed = (await Locations.StateAsync(ct)).Locations.Single(l => l.Id == s3);
        await Assert.That((listed.Copies, listed.LastBackup, listed.Problem)).IsEqualTo((3, (DateTimeOffset?)TestClock.Day, (string?)null));

        // Again: nothing is sent twice.
        await Locations.BackupAsync(ct);
        await Locations.Running;
        await Assert.That(_folders.Folders[s3].Files).Count().IsEqualTo(4);
    }

    [Test]
    public async Task A_Copy_That_Does_Not_Match_Or_A_Location_That_Fails_Stops_Only_Itself_And_Says_So(CancellationToken ct)
    {
        Pro();
        _library.Add("A", (LibraryFileKind.Photo, "IMG_0001.JPG", MediaSamples.Picture("A")));
        var bad = await AddS3Async(ct, "Damaging");
        var down = await AddS3Async(ct, "Down");
        var fine = await AddS3Async(ct, "Fine");
        _folders.Folders[bad].Damage = bytes => [.. bytes, 1];
        _folders.Folders[down].Fails["list"] = new IOException("Down for the night.");
        // A copy read back that does not match is not kept either.
        var readBack = await AddS3Async(ct, "Read back");
        _folders.Folders[readBack].Keeps = null;
        _folders.Folders[readBack].Damage = bytes => [.. bytes, 2];
        await Locations.BackupAsync(ct);
        await Locations.Running;
        var state = await Locations.StateAsync(ct);
        await Assert.That(state.Locations.Single(l => l.Id == bad).Problem).IsEqualTo("The copy of IMG_0001.JPG there did not match this device's. It is sent again at the next backup.");
        await Assert.That(state.Locations.Single(l => l.Id == down).Problem).IsEqualTo("Down for the night.");
        await Assert.That(state.Locations.Single(l => l.Id == fine).Copies).IsEqualTo(1);
        await Assert.That(state.Locations.Single(l => l.Id == readBack).Copies).IsEqualTo(0);
        await Assert.That(state.Locations.Single(l => l.Id == bad).Copies).IsEqualTo(0);
    }

    [Test]
    public async Task Pause_Stops_A_Backup_Part_Way_And_Only_One_Thing_Runs_At_A_Time(CancellationToken ct)
    {
        Pro();
        Pictures();
        var id = await AddS3Async(ct);
        var putting = new TaskCompletionSource();
        var go = new TaskCompletionSource();
        _folders.Folders[id].Putting = async _ =>
        {
            putting.TrySetResult();
            await go.Task;
        };
        await Locations.BackupAsync(ct);
        await putting.Task;
        await SaysAsync("POST", "locations/check", HttpStatusCode.Conflict, "The locations are busy.", ct);
        await Assert.That(await Locations.AutomaticAsync(ct)).IsFalse();
        await SaysAsync("POST", "locations/pause", HttpStatusCode.NoContent, null, ct);
        go.SetResult();
        await Locations.Running;
        await Assert.That((await Locations.StateAsync(ct)).Activity!.Stage).IsEqualTo(BackupStage.Paused);
        // Paused already: nothing more to pause.
        Locations.Pause();
    }

    [Test]
    public async Task Backs_Up_By_Itself_Only_When_It_Is_On_Part_Of_The_Plan_The_Phone_Is_Charging_And_A_Location_Other_Than_The_Computer_Is_On(CancellationToken ct)
    {
        Pictures();
        var ledger = _phone.GetRequiredService<IBackupLedger>();
        await Assert.That(await Locations.AutomaticAsync(ct)).IsFalse();
        await ledger.SaveSettingsAsync(BackupSettings.Default with { Automatic = true }, ct);
        await Assert.That(await Locations.AutomaticAsync(ct)).IsFalse();
        _device.Charging = true;
        await Assert.That(await Locations.AutomaticAsync(ct)).IsFalse();
        Pro();
        await Assert.That(await Locations.AutomaticAsync(ct)).IsFalse();
        var id = await AddS3Async(ct);
        await SaysAsync("POST", "locations/auto", HttpStatusCode.Accepted, null, ct);
        await Locations.Running;
        await Assert.That(_folders.Folders[id].Files).Count().IsEqualTo(3);
    }

    [Test]
    public async Task Check_All_Backups_Keeps_What_Still_Matches_And_Forgets_A_Copy_Changed_Or_Gone(CancellationToken ct)
    {
        await SaysAsync("POST", "locations/check", HttpStatusCode.Conflict, "Turn on a location other than the computer first", ct);
        Pro();
        Pictures();
        var s3 = await AddS3Async(ct);
        var nas = await AddS3Async(ct, "NAS");
        _folders.Folders[nas].Keeps = null;
        var down = await AddS3Async(ct, "Down");
        await Locations.BackupAsync(ct);
        await Locations.Running;
        // One changed there, one gone; the location that reads back finds one changed too.
        _folders.Folders[s3].Files["IMG_0001.JPG"] = [1];
        _folders.Folders[s3].Files.Remove("IMG_0002.JPG");
        _folders.Folders[nas].Files["IMG_0003.MOV"] = [2];
        _folders.Folders[down].Fails["checksum"] = new IOException("Unreachable.");
        await SaysAsync("POST", "locations/check", HttpStatusCode.Accepted, null, ct);
        await Locations.Running;
        await Assert.That((await Store.CopiesAsync(s3, ct)).Values.Select(c => c.Name)).IsEquivalentTo(["IMG_0003.MOV"]);
        await Assert.That((await Store.CopiesAsync(nas, ct)).Values.Select(c => c.Name).Order()).IsEquivalentTo(["IMG_0001.JPG", "IMG_0002.JPG"], CollectionOrdering.Matching);
        var state = await Locations.StateAsync(ct);
        await Assert.That(state.Locations.Single(l => l.Id == s3).LastChecked).IsEqualTo(TestClock.Day);
        await Assert.That(state.Locations.Single(l => l.Id == down).Problem).IsEqualTo("Unreachable.");
        await Assert.That(state.Activity!.Task).IsEqualTo(LocationTask.Check);
    }

    [Test]
    public async Task Send_To_Sends_What_Was_Chosen_Then_Deletes_What_Matched_From_The_Phone_Within_The_Free_Removals(CancellationToken ct)
    {
        Pictures();
        await PairedAsync(ct);
        await SaysAsync("POST", "locations/computer/send", HttpStatusCode.Conflict, "Turn Studio iMac on first", ct, new { ids = Array.Empty<string>() });
        await SaysAsync("POST", "locations/sent/delete", HttpStatusCode.Conflict, "Nothing sent is waiting to be deleted.", ct, new { });
        Pro();
        var id = await AddS3Async(ct);
        await Locations.TurnOffAsync(id, ct);
        await SaysAsync("POST", $"locations/{id}/send", HttpStatusCode.Conflict, "Turn Archive on first", ct, new { ids = Array.Empty<string>() });
        await Locations.TurnOnAsync(id, ct);
        var files = await _phone.GetRequiredService<OrganizeLibrary>().ListAsync(In("Photo Library"), ct);
        var chosen = files.Where(f => f.Name != "IMG_0002.JPG").Select(f => f.Id).ToArray();
        await SaysAsync("POST", $"locations/{id}/send", HttpStatusCode.Accepted, null, ct, new { ids = chosen });
        await Locations.Running;
        var sent = (await JsonAsync("locations", ct)).GetProperty("activity").GetProperty("sent");
        await Assert.That((sent.GetProperty("items").GetInt32(), sent.GetProperty("matched").GetInt32())).IsEqualTo((2, 2));

        // The free plan, one left today: the largest goes; the phone asks first, and saying no keeps both.
        _app.Purchases.Clear();
        var removals = _phone.GetRequiredService<DailyRemovals>();
        await removals.RecordAsync(49, 0, await removals.TodayAsync(ct), ct);
        await SaysAsync("POST", "locations/sent/delete", HttpStatusCode.Conflict, "you have 1 left today", ct, new { });
        _library.Refuse = "You chose not to delete them.";
        await SaysAsync("POST", "locations/sent/delete", HttpStatusCode.BadGateway, "You chose not to delete them.", ct, new { atMost = 1 });
        var (status, text) = await AskAsync("POST", "locations/sent/delete", ct, new { atMost = 1 });
        await Assert.That(status).IsEqualTo(HttpStatusCode.OK).Because(text);
        var removed = JsonDocument.Parse(text).RootElement;
        await Assert.That((removed.GetProperty("removed").GetInt32(), removed.GetProperty("left").GetInt32())).IsEqualTo((1, 1));
        await Assert.That(_library.RecentlyDeleted).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Safe_To_Delete_Counts_A_Copy_Checked_On_Any_Location_That_Is_On_Or_On_Two_As_The_Person_Chooses(CancellationToken ct)
    {
        Pro();
        Pictures();
        var backup = _phone.GetRequiredService<PhoneBackup>();
        await Assert.That((await backup.SafeAsync(ct)).Checked.Count).IsEqualTo(0);
        var id = await AddS3Async(ct);
        await Locations.BackupAsync(ct);
        await Locations.Running;
        // Videos are kept by the rules all the same: the last 30 days. Every one is checked on the bucket.
        await Assert.That((await backup.SafeAsync(ct)).Checked.Count).IsEqualTo(3);
        await SaysAsync("POST", "locations/count", HttpStatusCode.NoContent, null, ct, new { countAt = 2 });
        await Assert.That((await Locations.StateAsync(ct)).CountAt).IsEqualTo(2);
        await Assert.That((await backup.SafeAsync(ct)).Checked.Count).IsEqualTo(0);
        await SaysAsync("POST", "locations/count", HttpStatusCode.BadRequest, "Choose any location, or 2 locations.", ct, new { countAt = 3 });
        await SaysAsync("POST", "locations/count", HttpStatusCode.BadRequest, null, ct, new { countAt = 0 });
        // A second location with every copy: on both, they count.
        var second = await AddS3Async(ct, "Second");
        await Locations.BackupAsync(ct);
        await Locations.Running;
        await Assert.That((await backup.SafeAsync(ct)).Checked.Count).IsEqualTo(3);
        // Turned off, a location's copies stop counting.
        await Locations.TurnOffAsync(second, ct);
        await Assert.That((await backup.SafeAsync(ct)).Checked.Count).IsEqualTo(0);
        await Assert.That((await backup.RemovableAsync(ct)).Count).IsEqualTo(0);
        // The computer turned off is not backed up to by itself.
        await PairedAsync(ct);
        await Locations.TurnOffAsync("computer", ct);
        await _phone.GetRequiredService<IBackupLedger>().SaveSettingsAsync(BackupSettings.Default with { Automatic = true, OnlyWhileCharging = false }, ct);
        await Assert.That(await backup.AutomaticAsync(ct)).IsFalse();
        await Assert.That(id).IsNotNull();
    }

    [Test]
    public async Task Details_Say_Where_The_Folder_Is_And_How_Much_The_Location_Holds_Or_Why_It_Could_Not_Say(CancellationToken ct)
    {
        Pro();
        var id = await AddS3Async(ct);
        _folders.Folders[id].Space = new RemoteSpace(2_000_000_000_000, 641_800_000_000);
        var details = await JsonAsync($"locations/{id}", ct);
        await Assert.That(details.GetProperty("where").GetString()).IsEqualTo("photos-archive/Trovolve");
        await Assert.That(details.GetProperty("space").GetProperty("used").GetInt64()).IsEqualTo(641_800_000_000);
        _folders.Folders[id].Space = null;
        await Assert.That((await JsonAsync($"locations/{id}", ct)).GetProperty("space").ValueKind).IsEqualTo(JsonValueKind.Null);
        _folders.Folders[id].Fails["space"] = new IOException("No answer.");
        await Assert.That((await JsonAsync($"locations/{id}", ct)).GetProperty("problem").GetString()).IsEqualTo("No answer.");
        // A cloud service signed in to is asked too, with its tokens; those it renews are kept.
        await Locations.TurnOnAsync("onedrive", ct);
        _folders.Folders["onedrive"] = new MemoryFolder { Space = new RemoteSpace(1, 1) };
        await Assert.That((await Locations.DetailsAsync("onedrive", ct)).Space).IsEqualTo(new RemoteSpace(1, 1));
        await Assert.That(_folders.OpenedWith["onedrive"]["access"]).IsEqualTo("a");
        await _folders.Renewed!(new Dictionary<string, string> { ["access"] = "b" }, ct);
        await Assert.That(_secrets.Kept["onedrive"]["access"]).IsEqualTo("b");
    }


    [Test]
    public async Task An_Address_Asked_With_No_Body_Changes_Nothing_And_Says_What_Is_Missing(CancellationToken ct)
    {
        Pro();
        var id = await AddS3Async(ct);
        await SaysAsync("POST", $"locations/{id}/name", HttpStatusCode.Conflict, "Enter a name.", ct);
        await SaysAsync("POST", "locations/count", HttpStatusCode.BadRequest, "Choose any location, or 2 locations.", ct);
        await SaysAsync("POST", "locations/connect", HttpStatusCode.BadRequest, "Choose the kind of server.", ct);
        await SaysAsync("POST", $"locations/{id}/send", HttpStatusCode.Accepted, null, ct);
        await Locations.Running;
        await Assert.That((await Locations.StateAsync(ct)).Activity!.Sent!.Items).IsEqualTo(0);
        await SaysAsync("POST", "locations/sent/delete", HttpStatusCode.Conflict, "Nothing sent is waiting to be deleted.", ct);
        // Only the page may change them.
        var answer = await _router.AnswerAsync("POST", new Uri($"app://trovolve/api/locations/{id}/off"), [], null, ct);
        await Assert.That(answer.Status).IsEqualTo(401);
    }

    [Test]
    public async Task Backing_Up_With_Only_The_Computer_On_Starts_The_Computers_Backup_Alone(CancellationToken ct)
    {
        await _phone.GetRequiredService<IBackupLedger>().SaveComputerAsync(new BackupComputer("computer-1", "Studio iMac", "Pictures/Trovolve", null, null), ct);
        await Locations.TurnOnAsync("computer", ct);
        var computer = (await Locations.StateAsync(ct)).Locations[0];
        await Assert.That((computer.Settings["folder"], computer.On)).IsEqualTo(("Pictures/Trovolve", true));
        await SaysAsync("POST", "locations/backup", HttpStatusCode.Accepted, null, ct);
        await _phone.GetRequiredService<PhoneBackup>().Running;
        await Assert.That((await Locations.StateAsync(ct)).Activity).IsNull();
    }

    [Test]
    public async Task Backs_Up_By_Itself_When_Told_To_Whether_Or_Not_It_Is_Charging(CancellationToken ct)
    {
        Pro();
        Pictures();
        var id = await AddS3Async(ct);
        await _phone.GetRequiredService<IBackupLedger>().SaveSettingsAsync(BackupSettings.Default with { Automatic = true, OnlyWhileCharging = false }, ct);
        await Assert.That(await Locations.AutomaticAsync(ct)).IsTrue();
        await Locations.Running;
        await Assert.That(_folders.Folders[id].Files).Count().IsEqualTo(3);
    }

    [Test]
    public async Task A_Location_Changed_To_Ask_For_More_Points_Elsewhere_And_Forgets_What_It_Held(CancellationToken ct)
    {
        Pro();
        Pictures();
        var id = await AddS3Async(ct);
        await Locations.BackupAsync(ct);
        await Locations.Running;
        var more = new Dictionary<string, string>(S3Settings) { ["cls"] = "Standard-IA" };
        await Locations.ConnectServerAsync(new ServerConnection(id, LocationKind.AmazonS3, "Archive", more, new Dictionary<string, string> { ["secret"] = "s" }), ct);
        await Assert.That(await Store.CopiesAsync(id, ct)).IsEmpty();
    }

    [Test]
    public async Task Pause_Stops_A_Check_Part_Way(CancellationToken ct)
    {
        Pro();
        Pictures();
        var id = await AddS3Async(ct);
        await Locations.BackupAsync(ct);
        await Locations.Running;
        var checking = new TaskCompletionSource();
        var go = new TaskCompletionSource();
        _folders.Folders[id].Checking = async _ =>
        {
            checking.TrySetResult();
            await go.Task;
        };
        await Locations.CheckAllAsync(ct);
        await checking.Task;
        Locations.Pause();
        go.SetResult();
        await Locations.Running;
        await Assert.That((await Locations.StateAsync(ct)).Activity!.Stage).IsEqualTo(BackupStage.Paused);
        await Assert.That((await Store.CopiesAsync(id, ct)).Count).IsEqualTo(3);
    }

    [Test]
    public async Task A_Checksum_The_Scan_Worked_Out_Is_Used_As_It_Is(CancellationToken ct)
    {
        Pro();
        _library.Add("A", (LibraryFileKind.Photo, "IMG_0001.JPG", MediaSamples.Picture("A")));
        var files = _phone.GetRequiredService<IPhotoFileSystem>();
        var picture = files.FilesIn(In("Photo Library")).Single();
        var stored = files.Describe(picture)!;
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(MediaSamples.Picture("A")));
        await _phone.GetRequiredService<IPhotoRepository>().AddOrUpdateAsync(new Photo
        {
            SourcePath = picture, FileName = "IMG_0001.JPG", FileSizeBytes = stored.Length, FileModifiedUtc = stored.ModifiedUtc, ContentHash = sha,
        }, ct);
        var id = await AddS3Async(ct);
        await Locations.BackupAsync(ct);
        await Locations.Running;
        await Assert.That((await Store.CopiesAsync(id, ct)).Keys).IsEquivalentTo([sha.ToLowerInvariant()]);
    }

    [Test]
    public async Task Only_A_Phone_Backs_Up_To_Locations_And_Only_The_Page_May_Ask(CancellationToken ct)
    {
        var answer = await _router.AnswerAsync("GET", new Uri("app://trovolve/api/locations"), [], null, ct);
        await Assert.That(answer.Status).IsEqualTo(401);
        var computer = new TestApi();
        var asked = await computer.SendAsync(Http.Get("locations").FromClient(), ct, "locations");
        await Assert.That((asked.StatusCode, asked.Text())).IsEqualTo((HttpStatusCode.NotFound, "Only a phone backs up to locations."));
    }
}
