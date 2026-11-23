using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Api;
using PhotoSense.Api.About;
using PhotoSense.Api.Hosting;
using PhotoSense.Api.Http;
using PhotoSense.Application.Organizing;
using PhotoSense.Application.Plans;
using PhotoSense.Application.Transfer;
using PhotoSense.Application.Vault;
using PhotoSense.Desktop.Imaging;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Infrastructure.Library;
using PhotoSense.Infrastructure.Transfer;
using PhotoSense.Tests.Endpoints;

namespace PhotoSense.Tests.Application;

/// <summary>
/// The hidden vault on an iPhone, through its whole stack and the page's addresses: turning it on with a recovery
/// key, the phone's question before it opens, hiding (sealed and read back before anything leaves the library),
/// Unhide and Delete, and the vault's files carried to a real computer, sealed, and brought back to a new phone with
/// the recovery key. The libraries are in memory; the computer keeps its backups on this computer's disk.
/// </summary>
public sealed class VaultTests : IDisposable
{
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("photosense-vault-");
    private readonly MemoryPairs _computerPairs = new("computer-1");
    private readonly MemoryBackupStore _kept = new();
    private readonly FakeAdvertiser _telling = new();
    private readonly ComputerBackup _backups;
    private readonly ComputerPairing _door;
    private readonly Phone _phone;
    private readonly List<Phone> _phones = [];
    private readonly List<IDisposable> _open = [];

    public VaultTests()
    {
        _backups = TestBackups.Computer(Path.Combine(In("Home"), "Pictures", "PhotoSense"), _kept, home: In("Home"));
        _door = new ComputerPairing(new TcpTransferListener(0), _telling, _computerPairs, "Studio iMac", () => ["127.0.0.1"], _backups, TimeProvider.System);
        _phone = NewPhone("Jamie's iPhone");
    }

    public void Dispose()
    {
        foreach (var open in _open) open.Dispose();
        _door.StopListening();
        foreach (var phone in _phones) phone.Dispose();
        TestFiles.Remove(_data);
    }

    private string In(string folder) => Path.Combine(_data.FullName, folder);

    private string Hidden => Path.Combine(In("Home"), "Pictures", "PhotoSense", "Jamie's iPhone", ComputerBackup.HiddenFolder);

    /// <summary>An iPhone with PhotoSense: its library, store, vault and key in memory, and its page's addresses.</summary>
    private sealed class Phone : IDisposable
    {
        public readonly FakePhotoLibrary Library = new();
        public readonly FakeAppStore App = new();
        public readonly FakeComputerFinder Computers = new();
        public readonly FakeVaultKeys Keys = new();
        public readonly FakeUnlocker Unlocker = new();
        public readonly FakeWidgets Widgets = new();
        public readonly string Root;
        public readonly ServiceProvider Services;
        private readonly ApiRouter _router;

        public Phone(string data, string name)
        {
            Root = Path.Combine(data, "Photo Library");
            Services = new ServiceCollection()
                .AddPhotoSensePhone(Library, new FakePhotoAccess(), new MagickImageAnalyzer(), Path.Combine(data, "Documents"), Path.Combine(data, "Caches"), new FakeAdvertiser(), App,
                    new FakeCodeScanner(), Computers, new FakeDevice(), new FakeNotifier(), new FakeShareSheet(), Keys, Unlocker, Widgets, new MemoryLocationSecrets(), new FakeWebSignIn(), name, Root, new TcpTransferListener(0))
                .AddPhotoSenseApi()
                .AddSingleton(new ProgramInfo("iphone", "1.2.0.57", () => "notices"))
                .AddStandInStores()
                .AddSingleton<TimeProvider>(new TestClock())
                .BuildServiceProvider();
            _router = Services.GetRequiredService<PhotoSenseRoutes>().Router;
        }

        public PhoneBackup Backup => Services.GetRequiredService<PhoneBackup>();

        public void Pro() => App.Purchases.Add(new StorePurchase(PlanProducts.Lifetime, TestClock.Day, null, Trial: false));

        public async Task<(HttpStatusCode Status, string Text)> AskAsync(string method, string path, CancellationToken ct, object? body = null)
        {
            var answer = await _router.AnswerAsync(method, new Uri("app://photosense/api/" + path), [new("x-photosense-client", "phone")], body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body), ct);
            return ((HttpStatusCode)answer.Status, Encoding.UTF8.GetString(answer.Body));
        }

        public async Task<JsonElement> JsonAsync(string method, string path, CancellationToken ct, object? body = null)
        {
            var (status, text) = await AskAsync(method, path, ct, body);
            await Assert.That(status).IsEqualTo(HttpStatusCode.OK).Because(text);
            return JsonDocument.Parse(text).RootElement;
        }

        public async Task<byte[]?> ImageAsync(string path, CancellationToken ct)
        {
            var answer = await _router.AnswerAsync("GET", new Uri("app://photosense/api/" + path), [], null, ct);
            return answer.Status == 200 ? answer.Body : null;
        }

        /// <summary>Library's ids for these names, as the page has them.</summary>
        public async Task<string[]> IdsAsync(CancellationToken ct, params string[] names)
        {
            var files = await Services.GetRequiredService<OrganizeLibrary>().ListAsync(Root, ct);
            return [.. names.Select(n => files.Single(f => f.Name == n).Id)];
        }

        public async Task<int> HiddenAsync(CancellationToken ct, params string[] names)
            => (await JsonAsync("POST", "vault/hide", ct, new { ids = await IdsAsync(ct, names) })).GetProperty("changed").GetInt32();

        public void Dispose()
        {
            Backup.Pause();
            Services.Dispose();
        }
    }

    private Phone NewPhone(string name)
    {
        var phone = new Phone(In(name), name);
        _phones.Add(phone);
        return phone;
    }

    /// <summary>A Live Photo, a picture, and a video that says where it was taken.</summary>
    private static void Library(Phone phone)
    {
        phone.Library.Add("AAAAAAAA-1", (LibraryFileKind.Photo, "IMG_0001.JPG", MediaSamples.Picture("LIVE-1")), (LibraryFileKind.PairedVideo, "IMG_0001.MOV", MediaSamples.Movie("LIVE-1")));
        phone.Library.Add("BBBBBBBB-2", (LibraryFileKind.Photo, "IMG_0002.JPG", MediaSamples.Picture()));
        phone.Library.Add("CCCCCCCC-3", (LibraryFileKind.Video, "IMG_0003.MOV", MediaSamples.Movie()));
    }

    /// <summary>The vault turned on, on Pro, unlocked; its recovery key.</summary>
    private static async Task<string> OnAsync(Phone phone, CancellationToken ct)
    {
        phone.Pro();
        return (await phone.JsonAsync("POST", "vault/on", ct)).GetProperty("recoveryKey").GetString()!;
    }

    /// <summary>The phone and the computer paired already, the computer waiting and found on the Wi-Fi.</summary>
    private async Task PairedAsync(Phone phone, string name, CancellationToken ct)
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var phonePairs = phone.Services.GetRequiredService<IPairingStore>();
        await _computerPairs.SaveAsync(new PairedDevice(await phonePairs.SelfIdAsync(ct), name, secret), ct);
        await phonePairs.SaveAsync(new PairedDevice("computer-1", "Studio iMac", secret), ct);
        await phone.Services.GetRequiredService<IBackupLedger>().SaveComputerAsync(new BackupComputer("computer-1", "Studio iMac", "Pictures/PhotoSense", null, null), ct);
        if (_telling.Telling is null) await _door.ListenAsync(ct);
        var port = _telling.Telling!.Value.Port;
        phone.Computers.Computers.Add(new FoundComputer("computer-1", "Studio iMac", c => Loopback.ConnectAsync(port, c)));
    }

    private static async Task<JsonElement> BackedUpAsync(Phone phone, CancellationToken ct)
    {
        await phone.JsonAsync("POST", "backup/start", ct);
        await phone.Backup.Running;
        return await phone.JsonAsync("GET", "backup", ct);
    }

    private static string[] Names(JsonElement vault) => [.. vault.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()!)];

    [Test]
    public async Task The_Vault_Is_Pro_Turned_On_With_A_Recovery_Key_And_Opens_Only_After_The_Phones_Question(CancellationToken ct)
    {
        var off = await _phone.JsonAsync("GET", "vault", ct);
        await Assert.That((off.GetProperty("stage").GetString(), off.GetProperty("count").GetInt32(), off.GetProperty("computer").ValueKind, off.GetProperty("restore").ValueKind))
            .IsEqualTo(("off", 0, JsonValueKind.Null, JsonValueKind.Null));
        await Assert.That(await _phone.AskAsync("POST", "vault/on", ct)).IsEqualTo((HttpStatusCode.Conflict, HiddenVault.ProOnly));
        await Assert.That(await _phone.AskAsync("POST", "vault/unlock", ct)).IsEqualTo((HttpStatusCode.Conflict, "Turn on the hidden vault first."));

        var key = await OnAsync(_phone, ct);
        await Assert.That(key).Matches("^([0-9A-HJKMNP-TV-Z]{4}-){7}[0-9A-HJKMNP-TV-Z]{4}$");
        await Assert.That(_phone.Keys.Kept).IsEqualTo(key);
        await Assert.That((await _phone.JsonAsync("GET", "vault", ct)).GetProperty("stage").GetString()).IsEqualTo("open");
        await Assert.That(await _phone.AskAsync("POST", "vault/on", ct)).IsEqualTo((HttpStatusCode.Conflict, "The hidden vault is on already."));
        await Assert.That((await _phone.JsonAsync("GET", "vault/key", ct)).GetProperty("recoveryKey").GetString()).IsEqualTo(key);

        await Assert.That((await _phone.AskAsync("POST", "vault/lock", ct)).Status).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That((await _phone.JsonAsync("GET", "vault", ct)).GetProperty("stage").GetString()).IsEqualTo("locked");
        await Assert.That(await _phone.AskAsync("GET", "vault/key", ct)).IsEqualTo((HttpStatusCode.Conflict, "Unlock Hidden first."));

        // The phone's question, failed then passed.
        _phone.Unlocker.Passes = false;
        await Assert.That((await _phone.JsonAsync("POST", "vault/unlock", ct)).GetProperty("unlocked").GetBoolean()).IsFalse();
        await Assert.That((await _phone.JsonAsync("GET", "vault", ct)).GetProperty("stage").GetString()).IsEqualTo("locked");
        _phone.Unlocker.Passes = true;
        await Assert.That((await _phone.JsonAsync("POST", "vault/unlock", ct)).GetProperty("unlocked").GetBoolean()).IsTrue();
        await Assert.That(_phone.Unlocker.Asked).IsEquivalentTo(["Unlock Hidden", "Unlock Hidden"], CollectionOrdering.Matching);
        await Assert.That((await _phone.JsonAsync("GET", "vault/key", ct)).GetProperty("recoveryKey").GetString()).IsEqualTo(key);
    }

    [Test]
    public async Task Hiding_Seals_Each_Item_Then_The_Phone_Takes_Them_Out_Together_Not_Counted_And_What_Was_Known_Of_Them_Goes(CancellationToken ct)
    {
        Library(_phone);
        await OnAsync(_phone, ct);
        var ids = await _phone.IdsAsync(ct, "IMG_0001.JPG", "IMG_0002.JPG");
        // A preview made by Library before, which must not outlive the picture's hiding.
        await Assert.That(await _phone.ImageAsync($"organize/files/{ids[1]}/thumbnail", ct)).IsNotNull();
        var previews = _phone.Services.GetRequiredService<IThumbnailStore>();
        var key = (await _phone.Services.GetRequiredService<OrganizeLibrary>().ListAsync(_phone.Root, ct)).Single(f => f.Name == "IMG_0002.JPG").PreviewKey;
        await Assert.That(await previews.GetAsync(key, ct)).IsNotNull();
        var removedBefore = (await _phone.JsonAsync("GET", "plan", ct)).GetProperty("removedToday").GetInt32();

        await Assert.That(await _phone.HiddenAsync(ct, "IMG_0001.JPG", "IMG_0002.JPG")).IsEqualTo(2);
        // Taken out together, in one change the phone asks about once.
        await Assert.That(_phone.Library.RecentlyDeleted.Order()).IsEquivalentTo(["AAAAAAAA-1", "BBBBBBBB-2"], CollectionOrdering.Matching);
        await Assert.That(_phone.Library.Changes.Count).IsEqualTo(1);
        await Assert.That((await _phone.JsonAsync("GET", "plan", ct)).GetProperty("removedToday").GetInt32()).IsEqualTo(removedBefore);
        await Assert.That(await previews.GetAsync(key, ct)).IsNull();
        await Assert.That((await _phone.Services.GetRequiredService<OrganizeLibrary>().ListAsync(_phone.Root, ct)).Select(f => f.Name)).IsEquivalentTo(["IMG_0003.MOV"]);

        var vault = await _phone.JsonAsync("GET", "vault", ct);
        await Assert.That((vault.GetProperty("count").GetInt32(), vault.GetProperty("notBackedUp").GetInt32(), vault.GetProperty("computer").ValueKind)).IsEqualTo((2, 2, JsonValueKind.Null));
        await Assert.That(Names(vault).Order()).IsEquivalentTo(["IMG_0001.JPG", "IMG_0002.JPG"], CollectionOrdering.Matching);
        var live = vault.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("name").GetString() == "IMG_0001.JPG");
        await Assert.That((live.GetProperty("isVideo").GetBoolean(), live.GetProperty("onComputer").GetBoolean(), live.GetProperty("bytes").GetInt64()))
            .IsEqualTo((false, false, MediaSamples.Picture("LIVE-1").LongLength + MediaSamples.Movie("LIVE-1").LongLength));
        await Assert.That(live.GetProperty("taken").GetDateTime()).IsEqualTo(new DateTime(2024, 4, 7, 17, 35, 59));
        var id = live.GetProperty("id").GetString()!;
        // Previews made as it was hidden, shown only while the vault is open.
        await Assert.That(await _phone.ImageAsync($"vault/{id}/thumbnail", ct)).IsNotNull();
        await Assert.That(await _phone.ImageAsync($"vault/{id}/image", ct)).IsNotNull();
        await Assert.That(await _phone.ImageAsync("vault/not-an-item/thumbnail", ct)).IsNull();
        await _phone.AskAsync("POST", "vault/lock", ct);
        var locked = await _phone.JsonAsync("GET", "vault", ct);
        await Assert.That((locked.GetProperty("stage").GetString(), locked.GetProperty("count").GetInt32(), locked.GetProperty("items").GetArrayLength())).IsEqualTo(("locked", 2, 0));
        await Assert.That(await _phone.ImageAsync($"vault/{id}/thumbnail", ct)).IsNull();
        await Assert.That(await _phone.AskAsync("POST", "vault/hide", ct, new { ids = await _phone.IdsAsync(ct, "IMG_0003.MOV") })).IsEqualTo((HttpStatusCode.Conflict, "Unlock Hidden first."));

        // A video has no preview; an id that is no longer in the library is passed over.
        await _phone.JsonAsync("POST", "vault/unlock", ct);
        var video = await _phone.IdsAsync(ct, "IMG_0003.MOV");
        await Assert.That((await _phone.JsonAsync("POST", "vault/hide", ct, new { ids = new[] { video[0], ids[0] } })).GetProperty("changed").GetInt32()).IsEqualTo(1);
        var hiddenVideo = (await _phone.JsonAsync("GET", "vault", ct)).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("isVideo").GetBoolean()).GetProperty("id").GetString();
        await Assert.That(await _phone.ImageAsync($"vault/{hiddenVideo}/thumbnail", ct)).IsNull();
        await Assert.That((await _phone.JsonAsync("POST", "vault/hide", ct, new { ids = new[] { "gone" } })).GetProperty("changed").GetInt32()).IsEqualTo(0);
        foreach (var body in new object[] { new { ids = Array.Empty<string>() }, "not a request" })
            await Assert.That(await _phone.AskAsync("POST", "vault/hide", ct, body)).IsEqualTo((HttpStatusCode.BadRequest, "Choose the photos and videos first."));
    }

    [Test]
    public async Task Nothing_Leaves_The_Library_Nor_Stays_In_The_Vault_When_The_Phone_Says_No_A_File_Cannot_Be_Read_Or_The_Plan_Is_Free(CancellationToken ct)
    {
        Library(_phone);
        await OnAsync(_phone, ct);

        _phone.Library.Refuse = "You chose not to let PhotoSense delete these photos.";
        var (status, text) = await _phone.AskAsync("POST", "vault/hide", ct, new { ids = await _phone.IdsAsync(ct, "IMG_0002.JPG", "IMG_0003.MOV") });
        await Assert.That((status, text)).IsEqualTo((HttpStatusCode.Conflict,
            "Nothing was hidden: the phone did not let PhotoSense take 2 items out of the library. You chose not to let PhotoSense delete these photos."));
        await Assert.That((await _phone.JsonAsync("GET", "vault", ct)).GetProperty("count").GetInt32()).IsEqualTo(0);
        await Assert.That(_phone.Library.RecentlyDeleted).IsEmpty();

        // iCloud cannot give the second file of a Live Photo: nothing is hidden, and nothing half made stays.
        _phone.Library.Fetching = _ => Task.FromException(new IOException("iCloud could not be reached."));
        await Assert.That(await _phone.AskAsync("POST", "vault/hide", ct, new { ids = await _phone.IdsAsync(ct, "IMG_0001.JPG") })).IsEqualTo((HttpStatusCode.Conflict, "iCloud could not be reached."));
        _phone.Library.Fetching = null;
        await Assert.That((await _phone.JsonAsync("GET", "vault", ct)).GetProperty("count").GetInt32()).IsEqualTo(0);

        // Pro ended: what is hidden can still be seen and put back; nothing more is hidden.
        await Assert.That(await _phone.HiddenAsync(ct, "IMG_0002.JPG")).IsEqualTo(1);
        _phone.App.Purchases.Clear();
        await Assert.That(await _phone.AskAsync("POST", "vault/hide", ct, new { ids = await _phone.IdsAsync(ct, "IMG_0003.MOV") })).IsEqualTo((HttpStatusCode.Conflict, HiddenVault.ProOnly));
        var id = (await _phone.JsonAsync("GET", "vault", ct)).GetProperty("items")[0].GetProperty("id").GetString();
        await Assert.That((await _phone.JsonAsync("POST", "vault/unhide", ct, new { ids = new[] { id } })).GetProperty("changed").GetInt32()).IsEqualTo(1);
    }

    [Test]
    public async Task Unhide_Puts_Each_Back_As_It_Was_With_Its_Date_And_Place_And_Delete_Sends_It_On_To_Recently_Deleted_Counted(CancellationToken ct)
    {
        Library(_phone);
        await OnAsync(_phone, ct);
        _phone.Library.Album(["Trips"], "CCCCCCCC-3");
        await _phone.HiddenAsync(ct, "IMG_0001.JPG", "IMG_0003.MOV");
        var items = (await _phone.JsonAsync("GET", "vault", ct)).GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("name").GetString()!, i => i.GetProperty("id").GetString()!);

        await Assert.That((await _phone.JsonAsync("POST", "vault/unhide", ct, new { ids = new[] { items["IMG_0001.JPG"], items["IMG_0003.MOV"] } })).GetProperty("changed").GetInt32()).IsEqualTo(2);
        await Assert.That((await _phone.JsonAsync("GET", "vault", ct)).GetProperty("count").GetInt32()).IsEqualTo(0);
        var (live, liveTaken, livePlace) = _phone.Library.Imported[0];
        await Assert.That(_phone.Library.Bytes(live, LibraryFileKind.Photo)).IsEquivalentTo(MediaSamples.Picture("LIVE-1"), CollectionOrdering.Matching);
        await Assert.That(_phone.Library.Bytes(live, LibraryFileKind.PairedVideo)).IsEquivalentTo(MediaSamples.Movie("LIVE-1"), CollectionOrdering.Matching);
        await Assert.That((liveTaken, livePlace)).IsEqualTo((new DateTime(2024, 4, 7, 17, 35, 59), (PlaceChange?)null));
        var (video, _, place) = _phone.Library.Imported[1];
        await Assert.That(place).IsEqualTo(new PlaceChange(35.2886, -75.5161));
        // Back in the album PhotoSense made, where it was.
        await Assert.That(_phone.Library.InAlbums()["Trips"]).Contains(video);

        // Two more hidden; then the plan is free: deleting from the vault counts toward the day's 50.
        _phone.Library.Add("DDDDDDDD-4", (LibraryFileKind.Photo, "IMG_0004.JPG", MediaSamples.Picture("D")));
        await _phone.HiddenAsync(ct, "IMG_0002.JPG", "IMG_0004.JPG");
        var hidden = (await _phone.JsonAsync("GET", "vault", ct)).GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("name").GetString()!, i => i.GetProperty("id").GetString()!);
        _phone.App.Purchases.Clear();
        var removedBefore = (await _phone.JsonAsync("GET", "plan", ct)).GetProperty("removedToday").GetInt32();
        await Assert.That((await _phone.JsonAsync("POST", "vault/delete", ct, new { ids = new[] { hidden["IMG_0002.JPG"] } })).GetProperty("changed").GetInt32()).IsEqualTo(1);
        var deleted = _phone.Library.Imported[2].Id;
        await Assert.That(_phone.Library.RecentlyDeleted).Contains(deleted);
        await Assert.That((await _phone.JsonAsync("GET", "plan", ct)).GetProperty("removedToday").GetInt32()).IsEqualTo(removedBefore + 1);

        // Every free removal used today: Delete waits for tomorrow, and nothing changes.
        var removals = _phone.Services.GetRequiredService<DailyRemovals>();
        await removals.RecordAsync(49, 0, await removals.TodayAsync(ct), ct);
        var (refused, why) = await _phone.AskAsync("POST", "vault/delete", ct, new { ids = new[] { hidden["IMG_0004.JPG"] } });
        await Assert.That((refused, why.StartsWith("You have used today", StringComparison.Ordinal))).IsEqualTo((HttpStatusCode.Conflict, true)).Because(why);
        await Assert.That((await _phone.JsonAsync("GET", "vault", ct)).GetProperty("count").GetInt32()).IsEqualTo(1);
    }

    [Test]
    public async Task Put_Back_That_The_Phone_Refuses_Or_A_Damaged_Item_Says_So_And_What_Came_Back_Stays(CancellationToken ct)
    {
        Library(_phone);
        await OnAsync(_phone, ct);
        await _phone.HiddenAsync(ct, "IMG_0001.JPG", "IMG_0002.JPG");
        var items = (await _phone.JsonAsync("GET", "vault", ct)).GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("name").GetString()!, i => i.GetProperty("id").GetString()!);

        // Photos does not take it back: it stays hidden.
        _phone.Library.RefuseImport = "You chose not to let PhotoSense put it back in Photos.";
        await Assert.That(await _phone.AskAsync("POST", "vault/unhide", ct, new { ids = new[] { items["IMG_0001.JPG"] } }))
            .IsEqualTo((HttpStatusCode.Conflict, "You chose not to let PhotoSense put it back in Photos."));
        await Assert.That((await _phone.JsonAsync("GET", "vault", ct)).GetProperty("count").GetInt32()).IsEqualTo(2);

        // The phone keeps it back in the library rather than delete it.
        _phone.Library.Refuse = "You chose not to let PhotoSense delete this photo.";
        await Assert.That(await _phone.AskAsync("POST", "vault/delete", ct, new { ids = new[] { items["IMG_0001.JPG"] } }))
            .IsEqualTo((HttpStatusCode.Conflict, "1 item back in the library, as the phone did not let PhotoSense delete them. You chose not to let PhotoSense delete this photo."));
        await Assert.That(_phone.Library.Imported.Count).IsEqualTo(1);

        // A damaged item: it shows no preview, is not listed, and is not put back, though what came before it is.
        var vaultFolder = Path.Combine(In("Jamie's iPhone"), "Documents", "Hidden");
        var damaged = Path.Combine(vaultFolder, items["IMG_0002.JPG"] + VaultBox.Extension);
        var bytes = await File.ReadAllBytesAsync(damaged, ct);
        bytes[VaultBox.HeaderSize + 5] ^= 0xFF;
        await File.WriteAllBytesAsync(damaged, bytes, ct);
        await _phone.AskAsync("POST", "vault/lock", ct);
        await _phone.JsonAsync("POST", "vault/unlock", ct);
        var vault = await _phone.JsonAsync("GET", "vault", ct);
        await Assert.That((vault.GetProperty("count").GetInt32(), vault.GetProperty("items").GetArrayLength())).IsEqualTo((1, 0));
        await Assert.That(await _phone.ImageAsync($"vault/{items["IMG_0002.JPG"]}/thumbnail", ct)).IsNull();
        await Assert.That(await _phone.AskAsync("POST", "vault/unhide", ct, new { ids = new[] { items["IMG_0002.JPG"] } }))
            .IsEqualTo((HttpStatusCode.Conflict, "An item of the hidden vault is damaged, so it could not be put back."));
    }

    [Test]
    public async Task Hidden_Items_Go_To_The_Computer_Sealed_And_A_New_Phone_With_The_Recovery_Key_Brings_Them_Back(CancellationToken ct)
    {
        Library(_phone);
        var key = await OnAsync(_phone, ct);
        await _phone.HiddenAsync(ct, "IMG_0001.JPG");
        await PairedAsync(_phone, "Jamie's iPhone", ct);

        var backedUp = await BackedUpAsync(_phone, ct);
        // Two pictures and a video of the library, and the vault's one file.
        await Assert.That((backedUp.GetProperty("stage").GetString(), backedUp.GetProperty("done").GetInt32(), backedUp.GetProperty("total").GetInt32())).IsEqualTo(("ready", 3, 3));
        var sealedFiles = Directory.GetFiles(Hidden);
        await Assert.That(sealedFiles.Length).IsEqualTo(1);
        await Assert.That(Path.GetExtension(sealedFiles[0])).IsEqualTo(VaultBox.Extension);
        // Sealed: nothing of the picture is to be read there.
        var onComputer = await File.ReadAllBytesAsync(sealedFiles[0], ct);
        await Assert.That(Encoding.ASCII.GetString(onComputer).Contains("IMG_0001", StringComparison.Ordinal)).IsFalse();
        var vault = await _phone.JsonAsync("GET", "vault", ct);
        await Assert.That((vault.GetProperty("notBackedUp").GetInt32(), vault.GetProperty("computer").GetString(), vault.GetProperty("items")[0].GetProperty("onComputer").GetBoolean()))
            .IsEqualTo((0, "Studio iMac", true));
        // The next sends nothing again; Send now never carries the vault.
        await Assert.That((await BackedUpAsync(_phone, ct)).GetProperty("total").GetInt32()).IsEqualTo(0);

        // A new iPhone: paired with the same computer, the vault brought back by the key, typed loosely.
        var phone = NewPhone("New iPhone");
        phone.Pro();
        await Assert.That(await phone.AskAsync("POST", "vault/recover", ct, new { recoveryKey = key })).IsEqualTo((HttpStatusCode.Conflict, "Pair with a computer first: in Settings, choose Computer."));
        await Assert.That(await phone.AskAsync("POST", "vault/recover", ct, new { recoveryKey = "not a key" }))
            .IsEqualTo((HttpStatusCode.BadRequest, "That is not a recovery key. It has 32 letters and digits, in 8 groups of 4."));
        await Assert.That((await phone.AskAsync("POST", "vault/recover", ct, "not a request")).Status).IsEqualTo(HttpStatusCode.BadRequest);
        await PairedAsync(phone, "New iPhone", ct);
        await Assert.That((await phone.AskAsync("POST", "vault/recover", ct, new { recoveryKey = key.Replace("-", " ", StringComparison.Ordinal).ToLowerInvariant() })).Status).IsEqualTo(HttpStatusCode.Accepted);
        await phone.Backup.Running;
        var restored = await phone.JsonAsync("GET", "vault", ct);
        await Assert.That((restored.GetProperty("stage").GetString(), restored.GetProperty("count").GetInt32(), restored.GetProperty("notBackedUp").GetInt32())).IsEqualTo(("open", 1, 0));
        await Assert.That((restored.GetProperty("restore").GetProperty("stage").GetString(), restored.GetProperty("restore").GetProperty("restored").GetInt32())).IsEqualTo(("ready", 1));
        await Assert.That(Names(restored)).IsEquivalentTo(["IMG_0001.JPG"]);
        await Assert.That(phone.Keys.Kept).IsEqualTo(key);

        await Assert.That(await NotedAsync(await phone.Services.GetRequiredService<IPairingStore>().SelfIdAsync(ct), "1 item of the hidden vault given back", ct)).IsTrue();

        // Brought back again: what this phone has already is not sent twice.
        await Assert.That((await phone.AskAsync("POST", "vault/recover", ct, new { recoveryKey = key })).Status).IsEqualTo(HttpStatusCode.Accepted);
        await phone.Backup.Running;
        await Assert.That((await phone.JsonAsync("GET", "vault", ct)).GetProperty("restore").GetProperty("restored").GetInt32()).IsEqualTo(0);

        var id = restored.GetProperty("items")[0].GetProperty("id").GetString();
        await phone.JsonAsync("POST", "vault/unhide", ct, new { ids = new[] { id } });
        await Assert.That(phone.Library.Bytes(phone.Library.Imported[0].Id, LibraryFileKind.Photo)).IsEquivalentTo(MediaSamples.Picture("LIVE-1"), CollectionOrdering.Matching);
    }

    // Whether the computer has noted this for the phone: it notes as the connection closes, which can be just after the phone has finished.
    private async Task<bool> NotedAsync(string phoneId, string text, CancellationToken ct)
    {
        for (var tries = 0; tries < 100; tries++)
        {
            if ((await _kept.EventsAsync(phoneId, 10, ct)).Any(e => e.Text == text)) return true;
            await Task.Delay(50, ct);
        }
        return false;
    }

    [Test]
    public async Task A_Vault_Holding_Items_Takes_No_Other_Recovery_Key_And_A_Restore_Waits_For_A_Backup_Running(CancellationToken ct)
    {
        Library(_phone);
        var key = await OnAsync(_phone, ct);
        await _phone.HiddenAsync(ct, "IMG_0002.JPG");
        await Assert.That(await _phone.AskAsync("POST", "vault/recover", ct, new { recoveryKey = RecoveryKey.New().ToString() }))
            .IsEqualTo((HttpStatusCode.Conflict, "This phone's hidden vault holds items under another recovery key. Unhide them first."));
        await PairedAsync(_phone, "Jamie's iPhone", ct);
        // Its own key again is taken: it brings back what the computer has.
        await Assert.That((await _phone.AskAsync("POST", "vault/recover", ct, new { recoveryKey = key })).Status).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(await _phone.AskAsync("POST", "vault/recover", ct, new { recoveryKey = key })).IsEqualTo((HttpStatusCode.Conflict, "A backup to Studio iMac is running. Restore once it ends."));
        await _phone.Backup.Running;
        await Assert.That((await _phone.JsonAsync("GET", "vault", ct)).GetProperty("restore").GetProperty("restored").GetInt32()).IsEqualTo(0);
    }

    [Test]
    [Arguments("size")]
    [Arguments("more")]
    [Arguments("damaged")]
    [Arguments("end")]
    [Arguments("well")]
    public async Task A_Computer_That_Gives_Back_Wrongly_Stops_The_Restore_And_Keeps_Nothing_Of_It(string wrong, CancellationToken ct)
    {
        _phone.Pro();
        var linked = await ScriptedComputerAsync(_phone, ct);
        var key = RecoveryKey.New();
        await Assert.That((await _phone.AskAsync("POST", "vault/recover", ct, new { recoveryKey = key.ToString() })).Status).IsEqualTo(HttpStatusCode.Accepted);
        var channel = await linked.WaitAsync(ct);
        await channel.ReceiveAsync(ct, "backup");
        await channel.SendAsync(new JsonObject { ["t"] = "held", ["sha256"] = new JsonArray(), ["more"] = false, ["folder"] = "Pictures/PhotoSense" }, ct);
        await channel.ReceiveAsync(ct, "offer");
        await channel.SendAsync(new JsonObject { ["t"] = "done" }, ct);
        var asked = await channel.ReceiveAsync(ct, "vault");
        await Assert.That(Wire.Text(asked, "key")).IsEqualTo(Convert.ToHexStringLower(key.Id));
        var bytes = "sealed"u8.ToArray();
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        await channel.SendAsync(new JsonObject { ["t"] = "vault", ["files"] = new JsonArray(new JsonObject { ["sha256"] = sha, ["size"] = bytes.Length }), ["more"] = false }, ct);
        await Assert.That(Wire.Text(await channel.ReceiveAsync(ct, "fetch"), "sha256")).IsEqualTo(sha);
        if (wrong == "size") await channel.SendAsync(new JsonObject { ["t"] = "file", ["size"] = -1 }, ct);
        else
        {
            await channel.SendAsync(new JsonObject { ["t"] = "file", ["size"] = wrong == "more" ? 2 : bytes.Length }, ct);
            await channel.SendDataAsync(wrong == "damaged" ? "SEALED"u8.ToArray() : bytes, ct);
            await channel.SendAsync(new JsonObject { ["t"] = "end", ["sha256"] = wrong == "end" ? new string('0', 64) : sha }, ct);
        }
        if (wrong == "well")
        {
            await channel.ReceiveAsync(ct, "fetched");
            await channel.ReceiveAsync(ct, "bye");
        }
        await _phone.Backup.Running;

        var restore = (await _phone.JsonAsync("GET", "vault", ct)).GetProperty("restore");
        if (wrong == "well")
        {
            // Given back whole and checked, it is kept, though it opens with no key this phone has: listed as nothing.
            await Assert.That((restore.GetProperty("stage").GetString(), restore.GetProperty("restored").GetInt32())).IsEqualTo(("ready", 1));
            await Assert.That(Directory.GetFiles(Path.Combine(In("Jamie's iPhone"), "Documents", "Hidden")).Length).IsEqualTo(1);
            var vault = await _phone.JsonAsync("GET", "vault", ct);
            await Assert.That((vault.GetProperty("count").GetInt32(), vault.GetProperty("items").GetArrayLength())).IsEqualTo((1, 0));
            return;
        }
        await Assert.That(restore.GetProperty("stage").GetString()).IsEqualTo("failed");
        await Assert.That(restore.GetProperty("problem").GetString()).IsEqualTo(wrong switch
        {
            "size" => "The computer sent something PhotoSense does not understand.",
            "more" => "The computer sent more of a file than it said it would.",
            _ => "A hidden item arrived damaged, so it was not kept. Restore again.",
        });
        var folder = Path.Combine(In("Jamie's iPhone"), "Documents", "Hidden");
        await Assert.That(Directory.Exists(folder) ? Directory.GetFiles(folder).Length : 0).IsEqualTo(0);
    }

    [Test]
    public async Task The_Computer_Gives_Back_Only_Files_Sealed_With_The_Key_Asked_For_And_Refuses_One_Never_Offered(CancellationToken ct)
    {
        Library(_phone);
        await OnAsync(_phone, ct);
        await _phone.HiddenAsync(ct, "IMG_0002.JPG");
        await PairedAsync(_phone, "Jamie's iPhone", ct);
        await BackedUpAsync(_phone, ct);
        // Beside the vault's file: another of the phone's copies, and a file the computer no longer has.
        var other = RecoveryKey.New();
        var phoneId = await _phone.Services.GetRequiredService<IPairingStore>().SelfIdAsync(ct);
        await _kept.HoldAsync(new HeldFile(phoneId, new string('a', 64), Path.Combine(Hidden, "gone.psv"), 1, DateTime.UtcNow, DateTimeOffset.UtcNow), ct);
        var foreign = Path.Combine(Hidden, "foreign.psv");
        await using (var output = File.Create(foreign))
        {
            using var writer = new VaultBox.Writer(output, other.VaultKey, other.Id);
            await writer.CompleteAsync(new VaultMeta("X.JPG", "/", null, null, null, false, DateTime.UtcNow, []), ct);
        }
        await _kept.HoldAsync(new HeldFile(phoneId, new string('b', 64), foreign, new FileInfo(foreign).Length, DateTime.UtcNow, DateTimeOffset.UtcNow), ct);

        // A phone of the test's own asks for the vault, then for a file never offered.
        var (near, far) = await Loopback.PairAsync(ct);
        _open.AddRange([near, far]);
        var secret = RandomNumberGenerator.GetBytes(32);
        await _computerPairs.SaveAsync(new PairedDevice("scripted", "Scripted", secret), ct);
        var pairs = new MemoryPairs("scripted");
        await pairs.SaveAsync(new PairedDevice("computer-1", "Studio iMac", secret), ct);
        var serving = Task.Run(async () =>
        {
            var computer = await Pairing.ComputerAsync(far, new Device("computer-1", "Studio iMac"), _computerPairs, (_, _, _) => Task.FromResult(false), null, true, CancellationToken.None);
            _open.Add(computer.Channel);
            await _backups.ServeAsync(computer.Channel, computer.Other, CancellationToken.None);
        }, CancellationToken.None);
        var linked = await Pairing.PhoneAsync(near, new Device("scripted", "Scripted"), pairs, (_, _, _) => Task.FromResult(false), null, ct);
        var channel = linked.Channel;
        _open.Add(channel);
        await channel.SendAsync(new JsonObject { ["t"] = "backup", ["kind"] = "iPhone" }, ct);
        var held = await channel.ReceiveAsync(ct, "held");
        while (held["more"]?.GetValue<bool>() == true) held = await channel.ReceiveAsync(ct, "held");
        await channel.SendAsync(new JsonObject { ["t"] = "offer", ["files"] = new JsonArray(), ["more"] = false }, ct);
        await channel.ReceiveAsync(ct, "done");
        await channel.SendAsync(new JsonObject { ["t"] = "vault", ["key"] = Convert.ToHexStringLower(other.Id) }, ct);
        var listed = await channel.ReceiveAsync(ct, "vault");
        await Assert.That(((JsonArray)listed["files"]!).Select(f => (string)f!["sha256"]!)).IsEquivalentTo([new string('b', 64)]);
        await channel.SendAsync(new JsonObject { ["t"] = "fetched" }, ct);
        await channel.SendAsync(new JsonObject { ["t"] = "vault", ["key"] = Convert.ToHexStringLower(other.Id) }, ct);
        await channel.ReceiveAsync(ct, "vault");
        await channel.SendAsync(new JsonObject { ["t"] = "fetch", ["sha256"] = new string('c', 64) }, ct);
        var stopped = await Assert.ThrowsAsync<TransferException>(() => serving);
        await Assert.That(stopped!.Message).IsEqualTo("The phone asked for a file that was not offered.");
    }

    [Test]
    public async Task Hiding_On_Android_And_Putting_Back_Goes_Into_The_Folder_It_Was_In_Or_Pictures(CancellationToken ct)
    {
        var store = new FakeMediaStore();
        var app = new FakeAppStore();
        var root = Path.Combine(In("Pixel"), "Phone");
        using var services = new ServiceCollection()
            .AddPhotoSenseAndroid(store, new FakePhotoAccess(), new MagickImageAnalyzer(), In("files"), In("cache"), new FakeAdvertiser(), app, new FakeCodeScanner(), new FakeComputerFinder(),
                new FakeDevice { Kind = "Android phone" }, new FakeNotifier(), new FakeShareSheet(), new FakeVaultKeys(), new FakeUnlocker(), new FakeWidgets(), new MemoryLocationSecrets(), new FakeWebSignIn(), "Pixel 8", root, new TcpTransferListener(0), vaultFolder: In("nobackup"))
            .AddPhotoSenseApi()
            .AddSingleton(new ProgramInfo("android", "1.2.0.57", () => "notices"))
            .AddStandInStores()
            .AddSingleton<TimeProvider>(new TestClock())
            .BuildServiceProvider();
        var router = services.GetRequiredService<PhotoSenseRoutes>().Router;
        async Task<JsonElement> AskAsync(string path, object? body = null)
        {
            var answer = await router.AnswerAsync("POST", new Uri("app://photosense/api/" + path), [new("x-photosense-client", "phone")], body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body), ct);
            await Assert.That(answer.Status).IsEqualTo(200).Because(Encoding.UTF8.GetString(answer.Body));
            return JsonDocument.Parse(answer.Body).RootElement;
        }
        app.Purchases.Add(new StorePurchase(PlanProducts.Lifetime, TestClock.Day, null, Trial: false));
        store.Add("image/1", "DCIM/Camera/", "IMG_1.JPG", MediaSamples.Picture());
        store.Add("image/2", "Pictures/Mine/", "IMG_2.JPG", MediaSamples.Picture("X"));
        // Not where Android keeps photos: put back in Pictures.
        store.Add("image/3", "Download/", "IMG_3.JPG", MediaSamples.Picture("Y"));
        await AskAsync("vault/on");
        var files = await services.GetRequiredService<OrganizeLibrary>().ListAsync(root, ct);
        await Assert.That((await AskAsync("vault/hide", new { ids = files.Select(f => f.Id).ToArray() })).GetProperty("changed").GetInt32()).IsEqualTo(3);
        await Assert.That(store.Trashed.Order()).IsEquivalentTo(["image/1", "image/2", "image/3"], CollectionOrdering.Matching);
        // Kept where Android's own backup of the app does not go.
        await Assert.That(Directory.GetFiles(In("nobackup")).Length).IsEqualTo(3);

        // A picture since taken under the first's name: Android gives the one put back a number.
        store.Add("image/4", "DCIM/Camera/", "IMG_1.JPG", MediaSamples.Picture("Z"));
        var hidden = services.GetRequiredService<HiddenVault>();
        var ids = (await hidden.StateAsync(ct)).Items.Select(i => i.Id).ToArray();
        await Assert.That((await AskAsync("vault/unhide", new { ids })).GetProperty("changed").GetInt32()).IsEqualTo(3);
        await Assert.That(store.Imported.Select(i => i.Folder + i.Name).Order()).IsEquivalentTo(["DCIM/Camera/IMG_1 (1).JPG", "Pictures/IMG_3.JPG", "Pictures/Mine/IMG_2.JPG"], CollectionOrdering.Matching);
        await Assert.That(store.Imported.All(i => i.Taken == new DateTime(2024, 4, 7, 17, 35, 59))).IsTrue();
        await Assert.That(store.Bytes(store.Imported.Single(i => i.Name == "IMG_2.JPG").Id)).IsEquivalentTo(MediaSamples.Picture("X"), CollectionOrdering.Matching);
    }

    [Test]
    public async Task Only_A_Phone_Has_A_Hidden_Vault_And_Only_The_Page_May_Ask(CancellationToken ct)
    {
        var addresses = new[] { ("GET", "vault"), ("POST", "vault/on"), ("POST", "vault/recover"), ("POST", "vault/unlock"), ("POST", "vault/lock"), ("GET", "vault/key"),
            ("POST", "vault/hide"), ("POST", "vault/unhide"), ("POST", "vault/delete") };
        var computer = new TestApi();
        foreach (var (method, path) in addresses)
        {
            var asked = await computer.SendAsync((method == "GET" ? Http.Get(path) : Http.Post(path, "{}")).FromClient(), ct, path);
            await Assert.That((asked.StatusCode, asked.Text())).IsEqualTo((HttpStatusCode.NotFound, "Only a phone has a hidden vault."));
            var stranger = await computer.SendAsync(method == "GET" ? Http.Get(path) : Http.Post(path, "{}"), ct, path);
            await Assert.That(stranger.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }
        await Assert.That((await computer.SendAsync(Http.Get("vault/x/thumbnail"), ct, "vault/x/thumbnail")).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Nothing_Leaves_The_Vault_While_It_Is_Locked(CancellationToken ct)
    {
        Library(_phone);
        await OnAsync(_phone, ct);
        await _phone.HiddenAsync(ct, "IMG_0002.JPG");
        var id = (await _phone.JsonAsync("GET", "vault", ct)).GetProperty("items")[0].GetProperty("id").GetString();
        await _phone.AskAsync("POST", "vault/lock", ct);
        foreach (var path in new[] { "vault/unhide", "vault/delete" })
            await Assert.That(await _phone.AskAsync("POST", path, ct, new { ids = new[] { id } })).IsEqualTo((HttpStatusCode.Conflict, "Unlock Hidden first."));
        await Assert.That((await _phone.JsonAsync("GET", "vault", ct)).GetProperty("count").GetInt32()).IsEqualTo(1);
    }

    [Test]
    public async Task A_Picture_That_Cannot_Be_Decoded_Is_Hidden_Without_A_Preview_And_The_Scans_Record_Of_It_Goes(CancellationToken ct)
    {
        _phone.Library.Add("EEEEEEEE-5", (LibraryFileKind.Photo, "IMG_0005.JPG", "not a picture"u8.ToArray()));
        await OnAsync(_phone, ct);
        var repo = _phone.Services.GetRequiredService<IPhotoRepository>();
        var path = _phone.Services.GetRequiredService<IPhotoFileSystem>().FilesIn(_phone.Root).Single();
        await repo.AddOrUpdateAsync(new PhotoSense.Domain.Entities.Photo { SourcePath = path, FileName = "IMG_0005.JPG" }, ct);

        await Assert.That(await _phone.HiddenAsync(ct, "IMG_0005.JPG")).IsEqualTo(1);
        await Assert.That(await repo.GetByPathAsync(path, ct)).IsNull();
        var id = (await _phone.JsonAsync("GET", "vault", ct)).GetProperty("items")[0].GetProperty("id").GetString();
        await Assert.That(await _phone.ImageAsync($"vault/{id}/thumbnail", ct)).IsNull();
    }

    [Test]
    public async Task Put_Back_Stopped_By_A_Damaged_Item_Says_How_Many_Before_It_Are_Back(CancellationToken ct)
    {
        Library(_phone);
        _phone.Library.Add("DDDDDDDD-4", (LibraryFileKind.Photo, "IMG_0004.JPG", MediaSamples.Picture("D")));
        await OnAsync(_phone, ct);
        await _phone.HiddenAsync(ct, "IMG_0001.JPG", "IMG_0002.JPG", "IMG_0003.MOV", "IMG_0004.JPG");
        var items = (await _phone.JsonAsync("GET", "vault", ct)).GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("name").GetString()!, i => i.GetProperty("id").GetString()!);
        var damaged = Path.Combine(In("Jamie's iPhone"), "Documents", "Hidden", items["IMG_0004.JPG"] + VaultBox.Extension);
        var bytes = await File.ReadAllBytesAsync(damaged, ct);
        bytes[^20] ^= 0xFF;
        await File.WriteAllBytesAsync(damaged, bytes, ct);

        await Assert.That(await _phone.AskAsync("POST", "vault/unhide", ct, new { ids = new[] { items["IMG_0001.JPG"], items["IMG_0002.JPG"], items["IMG_0004.JPG"] } }))
            .IsEqualTo((HttpStatusCode.Conflict, "An item of the hidden vault is damaged, so it could not be put back. The 2 items before it are back in the library."));
        await Assert.That(await _phone.AskAsync("POST", "vault/unhide", ct, new { ids = new[] { items["IMG_0003.MOV"], items["IMG_0004.JPG"] } }))
            .IsEqualTo((HttpStatusCode.Conflict, "An item of the hidden vault is damaged, so it could not be put back. The 1 item before it is back in the library."));
        await Assert.That(_phone.Library.Imported.Count).IsEqualTo(3);
    }

    [Test]
    [Arguments("changed")]
    [Arguments("other")]
    public async Task A_Sealed_Copy_That_Does_Not_Read_Back_As_The_Library_Had_It_Leaves_Nothing_Hidden(string how, CancellationToken ct)
    {
        Library(_phone);
        _phone.Pro();
        _phone.Keys.Kept = RecoveryKey.New().ToString();
        var vaultFiles = new MemoryVaultFiles();
        var s = _phone.Services;
        var files = s.GetRequiredService<PhotoLibraryFileSystem>();
        var vault = new HiddenVault(_phone.Keys, _phone.Unlocker, vaultFiles, files, files, files, s.GetRequiredService<OrganizeLibrary>(), new MagickImageAnalyzer(),
            s.GetRequiredService<IPhotoRepository>(), s.GetRequiredService<IThumbnailStore>(), s.GetRequiredService<Subscription>(), s.GetRequiredService<DailyRemovals>(),
            s.GetRequiredService<PhoneBackup>(), _phone.Root, new TestClock());
        await vault.UnlockAsync(ct);
        // The Live Photo, larger, hidden first; the picture's copy read back as a disk that changed it would give it.
        await vault.HideAsync(await _phone.IdsAsync(ct, "IMG_0001.JPG"), ct);
        var first = vaultFiles.List()[0].Name;
        var firstBytes = Whole(vaultFiles, first);
        vaultFiles.Reading = (name, bytes) => name == first ? bytes : how == "changed" ? [.. bytes[..^1], (byte)(bytes[^1] ^ 1)] : firstBytes;

        var refused = await Assert.ThrowsAsync<IOException>(async () => await vault.HideAsync(await _phone.IdsAsync(ct, "IMG_0002.JPG"), ct));
        await Assert.That(refused!.Message).IsEqualTo("PhotoSense could not check the hidden copy of IMG_0002.JPG, so nothing was hidden.");
        await Assert.That(vaultFiles.List().Select(f => f.Name)).IsEquivalentTo([first]);
        await Assert.That(_phone.Library.RecentlyDeleted).IsEquivalentTo(["AAAAAAAA-1"]);
    }

    // The bytes a stand-in vault holds for a file.
    private static byte[] Whole(MemoryVaultFiles files, string name)
    {
        using var read = files.OpenRead(name);
        using var copy = new MemoryStream();
        read.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>
    /// A computer of the test's own, paired with the phone already and found on the Wi-Fi: the next backup connects to
    /// it, and the test says what it likes on its behalf.
    /// </summary>
    private async Task<Task<SecureChannel>> ScriptedComputerAsync(Phone phone, CancellationToken ct)
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var phonePairs = phone.Services.GetRequiredService<IPairingStore>();
        await _computerPairs.SaveAsync(new PairedDevice(await phonePairs.SelfIdAsync(ct), "Jamie's iPhone", secret), ct);
        await phonePairs.SaveAsync(new PairedDevice("computer-1", "Studio iMac", secret), ct);
        await phone.Services.GetRequiredService<IBackupLedger>().SaveComputerAsync(new BackupComputer("computer-1", "Studio iMac", "Pictures/PhotoSense", null, null), ct);
        var linked = new TaskCompletionSource<SecureChannel>();
        phone.Computers.Computers.Add(new FoundComputer("computer-1", "Studio iMac", async c =>
        {
            var (near, far) = await Loopback.PairAsync(c);
            _open.AddRange([near, far]);
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
}
