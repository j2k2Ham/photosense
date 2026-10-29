using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PhotoSense.Api;
using PhotoSense.Api.About;
using PhotoSense.Api.Endpoints;
using PhotoSense.Api.Hosting;
using PhotoSense.Api.Http;
using PhotoSense.Application.Changes;
using PhotoSense.Application.Transfer;
using PhotoSense.Desktop.Imaging;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Files;
using PhotoSense.Infrastructure.Library;
using PhotoSense.Infrastructure.Persistence;
using PhotoSense.Infrastructure.Transfer;
using PhotoSense.Tests.Endpoints;

namespace PhotoSense.Tests.Application;

/// <summary>
/// Photos sent from a phone to a computer: the phone's whole stack on a photo library in memory, the computer's
/// receiving on a folder of its own, the two talking over this computer's own network.
/// </summary>
public sealed class TransferTests : IDisposable
{
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("photosense-transfer-");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "photosense-transfer-library-" + Guid.NewGuid().ToString("N"), "Photo Library");
    private readonly FakePhotoLibrary _library = new();
    private readonly FakeAdvertiser _advertiser = new();
    private readonly ServiceProvider _phone;
    private readonly ApiRouter _router;
    private readonly LiteDB.LiteDatabase _computerDb = new(new MemoryStream());
    private readonly LiteDbPairingStore _computerPairs;
    private readonly PhoneReceiving _computer;
    private readonly string _folder;
    private readonly List<IDisposable> _open = [];

    public TransferTests()
    {
        _phone = new ServiceCollection()
            .AddPhotoSensePhone(_library, new FakePhotoAccess(), new MagickImageAnalyzer(), In("Documents"), In("Caches"), _advertiser, "Jamie's iPhone", _root, new TcpTransferListener(0))
            .AddPhotoSenseApi()
            .AddSingleton(new ProgramInfo("iphone", "1.2.0.57", () => "notices"))
            .BuildServiceProvider();
        _router = _phone.GetRequiredService<PhotoSenseRoutes>().Router;
        _folder = Directory.CreateDirectory(In("Computer")).FullName;
        _computerPairs = new LiteDbPairingStore(_computerDb);
        _computer = Receiving(new OnThisComputer(_advertiser));
    }

    private PhoneReceiving Receiving(IPhoneFinder finder, DiskFileSystem? disk = null)
    {
        disk ??= new DiskFileSystem();
        return new PhoneReceiving(finder, _computerPairs, disk, new FileChanges(new LiteDbOrganizeBatchStore(_computerDb), new InMemoryPhotoRepository(), new Mock<IAuditRepository>().Object, disk), "DESKTOP-1");
    }

    public void Dispose()
    {
        foreach (var open in _open) open.Dispose();
        _computer.Stop();
        _phone.GetRequiredService<PhoneSending>().Stop();
        _phone.Dispose();
        _computerDb.Dispose();
        TestFiles.Remove(_data);
    }

    /// <summary>Finds the phone the way the network would: where it said it waits.</summary>
    private sealed class OnThisComputer(FakeAdvertiser advertiser) : IPhoneFinder
    {
        public Task<IReadOnlyList<FoundPhone>> FindAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<FoundPhone>>(advertiser.Telling is { } telling
                ? [new FoundPhone($"wifi:{telling.Id}", telling.Name, "wifi", c => Loopback.ConnectAsync(telling.Port, c))]
                : []);
    }

    private sealed class Found(params FoundPhone[] phones) : IPhoneFinder
    {
        public Task<IReadOnlyList<FoundPhone>> FindAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<FoundPhone>>(phones);
    }

    private string In(string folder) => Path.Combine(_data.FullName, folder);

    private async Task<(int Status, string Text)> AskAsync(string method, string path, object? body = null)
    {
        var answer = await _router.AnswerAsync(method, new Uri("app://photosense/api/" + path), [new("x-photosense-client", "phone")], body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body));
        return (answer.Status, Encoding.UTF8.GetString(answer.Body));
    }

    private async Task<JsonElement> PhoneAsync(string method, string path, object? body = null)
    {
        var (status, text) = await AskAsync(method, path, body);
        await Assert.That(status / 100).IsEqualTo(2).Because(text);
        return JsonDocument.Parse(text).RootElement;
    }

    private async Task<string> Stage() => (await PhoneAsync("GET", "send/state")).GetProperty("stage").GetString()!;

    private static async Task Until(Func<Task<bool>> done, string what)
    {
        for (var waited = 0; !await done(); waited++)
        {
            if (waited == 400) throw new TimeoutException($"Not {what} within 20 seconds.");
            await Task.Delay(50);
        }
    }

    // A Live Photo and a picture, offered by the ids Organize knows them by.
    private async Task<List<string>> OfferedAsync()
    {
        _library.Add("AAAAAAAA-1", (LibraryFileKind.Photo, "IMG_0001.JPG", MediaSamples.Picture("SENT-1")), (LibraryFileKind.PairedVideo, "IMG_0001.MOV", MediaSamples.Movie("SENT-1")));
        _library.Add("BBBBBBBB-2", (LibraryFileKind.Photo, "IMG_0002.JPG", MediaSamples.Picture("SENT-2")));
        var listed = await PhoneAsync("GET", $"organize/files?root={Uri.EscapeDataString(_root)}");
        return [.. listed.GetProperty("files").EnumerateArray().Where(f => f.GetProperty("name").GetString()!.EndsWith(".JPG", StringComparison.Ordinal)).Select(f => f.GetProperty("id").GetString()!)];
    }

    private async Task ReceiveAsync()
    {
        await Assert.That((await _computer.FindAsync()).Single().Name).IsEqualTo("Jamie's iPhone");
        _computer.Start($"wifi:{_advertiser.Telling!.Value.Id}", _folder);
    }

    [Test]
    public async Task The_First_Time_Both_Show_One_Number_Then_The_Files_Arrive_Checked_And_Can_Be_Deleted_From_The_Phone()
    {
        var offered = await PhoneAsync("POST", "send/start", new { files = await OfferedAsync() });
        await Assert.That((offered.GetProperty("stage").GetString(), offered.GetProperty("total").GetInt32(), offered.GetProperty("phone").GetString(), _advertiser.Telling!.Value.Name))
            .IsEqualTo(("waiting", 3, "Jamie's iPhone", "Jamie's iPhone"));
        await ReceiveAsync();

        await Until(async () => await Stage() == "comparing" && _computer.State.Stage == ReceiveStage.Comparing, "comparing");
        var asked = await PhoneAsync("GET", "send/state");
        await Assert.That((asked.GetProperty("code").GetString(), asked.GetProperty("computer").GetString(), _computer.State.Phone)).IsEqualTo((_computer.State.Code, "DESKTOP-1", "Jamie's iPhone"));
        await Assert.That(_computer.State.Code!).Matches(@"^\d{3} \d{3}$");
        _computer.Confirm(true);
        await PhoneAsync("POST", "send/confirm", new { same = true });

        await Until(async () => _computer.State.Stage == ReceiveStage.Done && await Stage() == "sent", "done");
        await _computer.Running;
        var received = _computer.State;
        await Assert.That((received.Done, received.Total, received.AlreadyThere, received.Folder)).IsEqualTo((3, 3, 0, _folder));
        await Assert.That(Directory.GetFiles(_folder).Select(f => new FileInfo(f).Name).Order()).IsEquivalentTo(["IMG_0001.JPG", "IMG_0001.MOV", "IMG_0002.JPG"], CollectionOrdering.Matching);
        await Assert.That(await File.ReadAllBytesAsync(Path.Combine(_folder, "IMG_0001.MOV"))).IsEquivalentTo(MediaSamples.Movie("SENT-1"), CollectionOrdering.Matching);
        var sent = await PhoneAsync("GET", "send/state");
        await Assert.That((sent.GetProperty("done").GetInt32(), sent.GetProperty("deletable").GetArrayLength(), sent.GetProperty("where").GetString(), sent.GetProperty("bytes").GetInt64() == received.TotalBytes))
            .IsEqualTo((3, 2, _folder, true));
        await Assert.That(sent.GetProperty("deletable").EnumerateArray().Select(n => n.GetString()!)).IsEquivalentTo(["IMG_0001.JPG", "IMG_0002.JPG"], CollectionOrdering.Matching);

        // Paired now: sent again, they are found to be there already, and nothing is asked.
        await PhoneAsync("POST", "send/start", new { files = (await PhoneAsync("GET", $"organize/files?root={Uri.EscapeDataString(_root)}")).GetProperty("files").EnumerateArray().Select(f => f.GetProperty("id").GetString()).ToArray() });
        await ReceiveAsync();
        await Until(() => Task.FromResult(_computer.State.Stage is ReceiveStage.Done or ReceiveStage.Failed), "done again");
        await Assert.That((_computer.State.Stage, _computer.State.AlreadyThere, _computer.State.Code)).IsEqualTo((ReceiveStage.Done, 3, (string?)null));
        await Assert.That(Directory.GetFiles(_folder).Length).IsEqualTo(3);

        var deleted = await PhoneAsync("POST", "send/delete-sent");
        await Assert.That((deleted.GetProperty("deleted").GetInt32(), _library.Changes.Single().Delete.Count)).IsEqualTo((2, 2));
        await Assert.That(_library.RecentlyDeleted).IsEquivalentTo(["AAAAAAAA-1", "BBBBBBBB-2"], CollectionOrdering.Matching);
        // Nothing left to delete: nothing is asked.
        await Assert.That((await PhoneAsync("POST", "send/delete-sent")).GetProperty("deleted").GetInt32()).IsEqualTo(0);
        await Assert.That(_library.Changes.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(false, true, "The numbers did not match, so nothing was sent. Try again, and look that both show the same number.", "On the computer the numbers were said not to match, so nothing was sent. Try again.")]
    [Arguments(true, false, "On the phone the numbers were said not to match, so nothing was sent. Try again.", "The numbers did not match, so nothing was sent. Try again, and look that both show the same number.")]
    public async Task Numbers_Said_Not_To_Match_On_Either_Stop_Both_And_Nothing_Is_Sent(bool computerSays, bool phoneSays, string onComputer, string onPhone)
    {
        await PhoneAsync("POST", "send/start", new { files = await OfferedAsync() });
        await ReceiveAsync();
        await Until(async () => await Stage() == "comparing" && _computer.State.Stage == ReceiveStage.Comparing, "comparing");
        _computer.Confirm(computerSays);
        await PhoneAsync("POST", "send/confirm", new { same = phoneSays });
        await Until(async () => _computer.State.Stage == ReceiveStage.Failed && await Stage() == "failed", "failed");
        await Assert.That((_computer.State.Problem, (await PhoneAsync("GET", "send/state")).GetProperty("problem").GetString())).IsEqualTo((onComputer, onPhone));
        await Assert.That(Directory.GetFiles(_folder)).IsEmpty();
    }

    [Test]
    public async Task Stopping_Either_Side_Ends_It_Cleanly_And_A_Second_Computer_Is_Told_The_Phone_Is_Busy()
    {
        await PhoneAsync("POST", "send/start", new { files = await OfferedAsync() });
        await ReceiveAsync();
        await Until(() => Task.FromResult(_computer.State.Stage == ReceiveStage.Comparing), "comparing");
        await Assert.That(Assert.Throws<InvalidOperationException>(() => _computer.Start($"wifi:{_advertiser.Telling!.Value.Id}", _folder))!.Message).IsEqualTo("Photos are already being received.");

        // Another computer, while the phone is busy with the first.
        await using (var other = await Loopback.ConnectAsync(_advertiser.Telling!.Value.Port, CancellationToken.None))
        {
            var busy = await Assert.ThrowsAsync<TransferException>(() => Wire.ReceiveAsync(other, "phone", CancellationToken.None));
            await Assert.That(busy!.Message).IsEqualTo("The phone is already sending to another computer.");
        }

        _computer.Stop();
        await _computer.Running;
        await Assert.That((_computer.State.Stage, _computer.State.Problem)).IsEqualTo((ReceiveStage.Failed, "Receiving was stopped. What had arrived is kept."));
        var stopped = await PhoneAsync("POST", "send/stop");
        await Assert.That((stopped.GetProperty("stage").GetString(), _advertiser.Telling)).IsEqualTo(("idle", ((string, string, int)?)null));
        await Assert.That((await _computer.FindAsync()).Count).IsEqualTo(0);
        await Assert.That(Assert.Throws<ArgumentException>(() => _computer.Start("wifi:gone", _folder))!.Message).IsEqualTo("That phone is no longer offering photos. Look for it again.");
    }

    [Test]
    public async Task Only_Files_Still_There_Are_Offered_Each_Once_With_Its_Live_Photo_Video()
    {
        var offered = await OfferedAsync();
        var once = await PhoneAsync("POST", "send/start", new { files = new[] { offered[0], offered[0], "not an id" } });
        await Assert.That(once.GetProperty("total").GetInt32()).IsEqualTo(2);
        // An answer that is not a yes, with nothing asked: nothing happens.
        await Assert.That((await PhoneAsync("POST", "send/confirm", "not json")).GetProperty("stage").GetString()).IsEqualTo("waiting");
        await PhoneAsync("POST", "send/stop");

        _library.Remove("AAAAAAAA-1");
        _library.Remove("BBBBBBBB-2");
        await Assert.That(await AskAsync("POST", "send/start", new { files = offered })).IsEqualTo((404, "None of those files is there to send any more."));
        await Assert.That(await AskAsync("POST", "send/start", new { files = Array.Empty<string>() })).IsEqualTo((400, "Choose the photos to send first."));
        await Assert.That(await AskAsync("POST", "send/start", "not json")).IsEqualTo((400, "Choose the photos to send first."));
    }

    [Test]
    public async Task Only_A_Phone_Deletes_What_It_Sent()
    {
        var onAComputer = new PhoneSending(new TcpTransferListener(0), new FakeAdvertiser(), new MemoryPairs("computer"), new DiskFileSystem(),
            _phone.GetRequiredService<ICompanionFileFinder>(), _phone.GetRequiredService<PhotoSense.Application.Organizing.OrganizeLibrary>(), "DESKTOP-1");
        await Assert.That(Assert.Throws<InvalidOperationException>(() => onAComputer.DeleteSent())!.Message).IsEqualTo("Only a phone deletes what it has sent.");
    }

    [Test]
    public async Task Deleting_From_The_Phone_That_The_Person_Declines_Says_So_And_Keeps_Them_Deletable()
    {
        var (channel, _) = await SentToComputerAsync();
        await Get(channel, "f3");
        await Until(async () => (await PhoneAsync("GET", "send/state")).GetProperty("deletable").GetArrayLength() == 1, "one there");
        _library.Refuse = "You chose not to delete these photos.";
        await Assert.That(await AskAsync("POST", "send/delete-sent")).IsEqualTo((409, "You chose not to delete these photos."));
        await Assert.That((await PhoneAsync("GET", "send/state")).GetProperty("deletable").GetArrayLength()).IsEqualTo(1);
    }

    // ---- a computer of the test's own, paired with the phone already, asking what it likes

    private async Task<SecureChannel> PlayComputerAsync()
    {
        var (phoneId, _, port) = _advertiser.Telling!.Value;
        var secret = RandomNumberGenerator.GetBytes(32);
        var pairs = new MemoryPairs("test-computer");
        await pairs.SaveAsync(new PairedDevice(phoneId, "Jamie's iPhone", secret, DateTime.UtcNow));
        await _phone.GetRequiredService<IPairingStore>().SaveAsync(new PairedDevice("test-computer", "Test computer", secret, DateTime.UtcNow));
        var stream = await Loopback.ConnectAsync(port, CancellationToken.None);
        _open.Add(stream);
        var linked = await Pairing.ComputerAsync(stream, new Device("test-computer", "Test computer"), pairs, (_, _, _) => Task.FromResult(false), CancellationToken.None);
        _open.Add(linked.Channel);
        return linked.Channel;
    }

    private static async Task<string> Get(SecureChannel channel, string id, string? where = "C:\\Photos")
    {
        await channel.SendAsync(new JsonObject { ["t"] = "get", ["id"] = id }, CancellationToken.None);
        var size = (long)(await channel.ReceiveAsync(CancellationToken.None, "file"))["size"]!;
        for (long got = 0; got < size;) got += (await channel.ReceiveDataAsync(CancellationToken.None)).Length;
        var check = Wire.Text(await channel.ReceiveAsync(CancellationToken.None, "end"), "sha256");
        if (where is not null) await channel.SendAsync(new JsonObject { ["t"] = "got", ["id"] = id, ["sha256"] = check, ["where"] = where }, CancellationToken.None);
        return check;
    }

    private async Task<(SecureChannel Channel, JsonObject Offer)> SentToComputerAsync()
    {
        await PhoneAsync("POST", "send/start", new { files = await OfferedAsync() });
        var channel = await PlayComputerAsync();
        await channel.SendAsync(new JsonObject { ["t"] = "offer?" }, CancellationToken.None);
        return (channel, await channel.ReceiveAsync(CancellationToken.None, "offer"));
    }

    private async Task<JsonElement> SendingProblemAsync()
    {
        await Until(async () => await Stage() == "failed", "failed");
        return await PhoneAsync("GET", "send/state");
    }

    [Test]
    public async Task A_File_Counts_As_On_The_Computer_Only_With_The_Phones_Own_Check_And_Folders_Not_One_Are_Not_Named()
    {
        var (channel, offer) = await SentToComputerAsync();
        await Assert.That(((JsonArray)offer["files"]!).Select(f => (string)f!["name"]!)).IsEquivalentTo(["IMG_0001.JPG", "IMG_0001.MOV", "IMG_0002.JPG"], CollectionOrdering.Matching);
        await Get(channel, "f1", "C:\\Photos\\A");
        await Get(channel, "f2", "C:\\Photos\\B");
        await Until(async () => (await PhoneAsync("GET", "send/state")).GetProperty("done").GetInt32() == 2, "two there");
        var two = await PhoneAsync("GET", "send/state");
        await Assert.That((two.GetProperty("deletable")[0].GetString(), two.GetProperty("where").ValueKind)).IsEqualTo(("IMG_0001.JPG", JsonValueKind.Null));

        var check = await Get(channel, "f3", where: null);
        var other = (check[0] == '0' ? "1" : "0") + check[1..];
        await channel.SendAsync(new JsonObject { ["t"] = "got", ["id"] = "f3", ["sha256"] = other, ["where"] = "C:\\Photos" }, CancellationToken.None);
        var stopped = await SendingProblemAsync();
        await Assert.That((stopped.GetProperty("problem").GetString(), stopped.GetProperty("deletable").GetArrayLength()))
            .IsEqualTo(("The computer's check of a file did not match what was sent. Send it again.", 1));
    }

    [Test]
    public async Task A_Computer_Asking_For_A_File_Not_Offered_Is_Stopped()
    {
        var (channel, _) = await SentToComputerAsync();
        await channel.SendAsync(new JsonObject { ["t"] = "get", ["id"] = "f9" }, CancellationToken.None);
        await Assert.That((await SendingProblemAsync()).GetProperty("problem").GetString()).IsEqualTo("The computer asked for a file that was not offered.");
    }

    // ---- a phone of the test's own, paired with the computer already, sending what it likes

    private async Task<(PhoneReceiving Computer, SecureChannel Phone)> FromPlayedPhoneAsync(DiskFileSystem? disk = null)
    {
        var (near, far) = await Loopback.PairAsync();
        _open.Add(near);
        _open.Add(far);
        var secret = RandomNumberGenerator.GetBytes(32);
        var pairs = new MemoryPairs("test-phone");
        await pairs.SaveAsync(new PairedDevice(await _computerPairs.SelfIdAsync(), "DESKTOP-1", secret, DateTime.UtcNow));
        await _computerPairs.SaveAsync(new PairedDevice("test-phone", "Test phone", secret, DateTime.UtcNow));
        var computer = Receiving(new Found(new FoundPhone("wifi:test-phone", "Test phone", "wifi", _ => Task.FromResult(near))), disk);
        await computer.FindAsync();
        computer.Start("wifi:test-phone", _folder);
        var linked = await Pairing.PhoneAsync(far, new Device("test-phone", "Test phone"), pairs, (_, _, _) => Task.FromResult(false), CancellationToken.None);
        _open.Add(linked.Channel);
        await linked.Channel.ReceiveAsync(CancellationToken.None, "offer?");
        return (computer, linked.Channel);
    }

    private static Task Offer(SecureChannel phone, params JsonObject[] files)
        => phone.SendAsync(new JsonObject { ["t"] = "offer", ["files"] = new JsonArray([.. files]) }, CancellationToken.None);

    private static JsonObject Offered(string id, string name, JsonNode? size) => new() { ["id"] = id, ["name"] = name, ["size"] = size };

    // The computer stops at what it does not take, so what would come after is not sent.
    private static async Task SendFile(SecureChannel phone, byte[] bytes, string? check = null, long? said = null, bool whole = true)
    {
        var id = Wire.Text(await phone.ReceiveAsync(CancellationToken.None, "get"), "id");
        await phone.SendAsync(new JsonObject { ["t"] = "file", ["id"] = id, ["size"] = said ?? bytes.Length }, CancellationToken.None);
        if (!whole) return;
        await phone.SendDataAsync(bytes, CancellationToken.None);
        if (said < bytes.Length) return;
        await phone.SendAsync(new JsonObject { ["t"] = "end", ["id"] = id, ["sha256"] = check ?? Convert.ToHexStringLower(SHA256.HashData(bytes)) }, CancellationToken.None);
    }

    private static async Task<ReceiveState> Ended(PhoneReceiving computer)
    {
        await computer.Running.WaitAsync(TimeSpan.FromSeconds(20));
        return computer.State;
    }

    [Test]
    public async Task A_Phone_Offering_Nothing_Is_Done_At_Once()
    {
        var (computer, phone) = await FromPlayedPhoneAsync();
        await phone.SendAsync(new JsonObject { ["t"] = "offer" }, CancellationToken.None);
        await phone.ReceiveAsync(CancellationToken.None, "done");
        await phone.SendAsync(new JsonObject { ["t"] = "bye" }, CancellationToken.None);
        var ended = await Ended(computer);
        await Assert.That((ended.Stage, ended.Total, ended.Phone)).IsEqualTo((ReceiveStage.Done, 0, "Test phone"));
    }

    [Test]
    public async Task A_File_That_Arrives_Damaged_Or_Longer_Than_Said_Is_Not_Kept()
    {
        var (computer, phone) = await FromPlayedPhoneAsync();
        await Offer(phone, Offered("f1", "IMG_0001.JPG", 3));
        await SendFile(phone, [1, 2, 3], check: "not its check");
        await Assert.That((await Ended(computer)).Problem).IsEqualTo("A file arrived damaged, so it was not kept. Try again.");

        var (longer, phone2) = await FromPlayedPhoneAsync();
        await Offer(phone2, Offered("f1", "IMG_0001.JPG", 2));
        await SendFile(phone2, [1, 2, 3], said: 2);
        await Assert.That((await Ended(longer)).Problem).IsEqualTo("The phone sent more of a file than it said it would.");
        await Assert.That(Directory.GetFiles(_folder)).IsEmpty();
    }

    [Test]
    [Arguments("\"large\"")]
    [Arguments("-1")]
    [Arguments(null)]
    public async Task An_Offer_Without_Sizes_Is_Not_Understood(string? size)
    {
        var (computer, phone) = await FromPlayedPhoneAsync();
        await Offer(phone, Offered("f1", "IMG_0001.JPG", size is null ? null : JsonNode.Parse(size)));
        await Assert.That((await Ended(computer)).Problem).IsEqualTo("The phone sent something PhotoSense does not understand.");
    }

    [Test]
    public async Task A_Phone_That_Answers_With_Something_Else_Is_Not_Understood_And_Nothing_Is_Left_Behind()
    {
        var (computer, phone) = await FromPlayedPhoneAsync();
        await Offer(phone, Offered("f1", "IMG_0001.JPG", 3));
        await phone.ReceiveAsync(CancellationToken.None, "get");
        await phone.SendAsync(new JsonObject { ["t"] = "end" }, CancellationToken.None);
        await Assert.That((await Ended(computer)).Problem).IsEqualTo("The other device sent something PhotoSense does not understand.");
        await Assert.That(Directory.GetFiles(_folder)).IsEmpty();
    }

    [Test]
    public async Task A_File_Of_The_Same_Name_But_Not_The_Same_Is_Kept_And_The_New_One_Arrives_Beside_It()
    {
        await File.WriteAllBytesAsync(Path.Combine(_folder, "IMG_0001.JPG"), [9, 9, 9]);
        await File.WriteAllBytesAsync(Path.Combine(_folder, "IMG_0002.JPG"), [9]);
        var (computer, phone) = await FromPlayedPhoneAsync();
        await Offer(phone, Offered("f1", "..\\..\\IMG_0001.JPG", 3), Offered("f2", "IMG_0002.JPG", 3));
        await SendFile(phone, [1, 2, 3]);
        await phone.ReceiveAsync(CancellationToken.None, "got");
        await SendFile(phone, [4, 5, 6]);
        await phone.ReceiveAsync(CancellationToken.None, "got");
        await phone.ReceiveAsync(CancellationToken.None, "done");
        await phone.SendAsync(new JsonObject { ["t"] = "bye" }, CancellationToken.None);
        var ended = await Ended(computer);
        await Assert.That((ended.Stage, ended.Done, ended.AlreadyThere)).IsEqualTo((ReceiveStage.Done, 2, 0));
        await Assert.That(Directory.GetFiles(_folder).Length).IsEqualTo(4);
        await Assert.That(await File.ReadAllBytesAsync(Path.Combine(_folder, "IMG_0001.JPG"))).IsEquivalentTo(new byte[] { 9, 9, 9 }, CollectionOrdering.Matching);
    }

    private sealed class Forbidding : DiskFileSystem
    {
        public override Stream CreateNew(string path) => throw new UnauthorizedAccessException("Access to the folder is denied.");
    }

    [Test]
    public async Task A_Folder_That_Will_Not_Take_Files_Says_What_The_Computer_Said()
    {
        var (computer, phone) = await FromPlayedPhoneAsync(new FailingDisk().Fail("create", _ => true, "The disk is full."));
        await Offer(phone, Offered("f1", "IMG_0001.JPG", 3));
        await SendFile(phone, [1, 2, 3], whole: false);
        await Assert.That((await Ended(computer)).Problem).IsEqualTo("Receiving stopped: The disk is full.");

        var (forbidden, phone2) = await FromPlayedPhoneAsync(new Forbidding());
        await Offer(phone2, Offered("f1", "IMG_0001.JPG", 3));
        await SendFile(phone2, [1, 2, 3], whole: false);
        await Assert.That((await Ended(forbidden)).Problem).IsEqualTo("Receiving stopped: Access to the folder is denied.");
    }

    [Test]
    [Arguments("IMG_0001.JPG", "IMG_0001.JPG")]
    [Arguments("DCIM/100APPLE/IMG_0001.JPG", "IMG_0001.JPG")]
    [Arguments("..\\..\\Windows\\win.ini", "win.ini")]
    [Arguments("what?.jpg", "what_.jpg")]
    [Arguments("tab\there.jpg", "tab_here.jpg")]
    [Arguments(" ..hidden. ", "hidden")]
    [Arguments("...", "photo")]
    [Arguments("", "photo")]
    public async Task A_Name_From_The_Phone_Becomes_A_Plain_Name_In_The_Folder(string said, string kept)
        => await Assert.That(PhoneReceiving.Plain(said)).IsEqualTo(kept);

    // ---- what the computer's page asks

    [Test]
    public async Task The_Computer_Says_Plainly_What_Is_Wrong_With_What_It_Is_Asked()
    {
        var endpoints = new TransferEndpoints(receiving: _computer);
        await Assert.That((await endpoints.PhonesAsync(Http.Get("phones").FromClient())).Json().GetProperty("phones").GetArrayLength()).IsEqualTo(0);
        await Assert.That((await endpoints.ReceivingAsync(Http.Get("receive/state"))).Json().GetProperty("stage").GetString()).IsEqualTo("idle");
        // An answer when nothing is asked: nothing happens.
        await endpoints.ConfirmReceivingAsync(Http.Post("receive/confirm", "{\"same\": true}").FromClient());
        await endpoints.ConfirmReceivingAsync(Http.Post("receive/confirm", "not json").FromClient());

        async Task<(int, string)> Start(string body)
        {
            var answer = await endpoints.StartReceivingAsync(Http.Post("receive/start", body).FromClient());
            return ((int)answer.StatusCode, answer.Text());
        }
        const string choose = "Choose the phone, and the folder the photos go to.";
        var folder = JsonSerializer.Serialize(_folder);
        await Assert.That(await Start("not json")).IsEqualTo((400, choose));
        await Assert.That(await Start($"{{\"folder\": {folder}}}")).IsEqualTo((400, choose));
        await Assert.That(await Start("{\"phone\": \"wifi:x\"}")).IsEqualTo((400, choose));
        await Assert.That(await Start("{\"phone\": \"wifi:x\", \"folder\": \"Photos\"}")).IsEqualTo((400, choose));
        await Assert.That(await Start($"{{\"phone\": \"wifi:x\", \"folder\": {folder}}}")).IsEqualTo((404, "That phone is no longer offering photos. Look for it again."));

        await PhoneAsync("POST", "send/start", new { files = await OfferedAsync() });
        var phones = (await endpoints.PhonesAsync(Http.Get("phones").FromClient())).Json().GetProperty("phones");
        var key = phones[0].GetProperty("key").GetString()!;
        await Assert.That((phones[0].GetProperty("name").GetString(), phones[0].GetProperty("via").GetString())).IsEqualTo(("Jamie's iPhone", "wifi"));
        var missing = Path.Combine(_folder, "Not there");
        await Assert.That(await Start($"{{\"phone\": \"{key}\", \"folder\": {JsonSerializer.Serialize(missing)}}}")).IsEqualTo((404, $"Folder not found: {missing}"));

        var started = await endpoints.StartReceivingAsync(Http.Post("receive/start", $"{{\"phone\": \"{key}\", \"folder\": {folder}}}").FromClient());
        await Assert.That(started.Json().GetProperty("stage").GetString()).IsEqualTo("connecting");
        await Assert.That(await Start($"{{\"phone\": \"{key}\", \"folder\": {folder}}}")).IsEqualTo((409, "Photos are already being received."));
        await Until(() => Task.FromResult(_computer.State.Stage == ReceiveStage.Comparing), "comparing");
        await endpoints.ConfirmReceivingAsync(Http.Post("receive/confirm", "{\"same\": false}").FromClient());
        await _computer.Running;
        var stopped = (await endpoints.StopReceivingAsync(Http.Post("receive/stop").FromClient())).Json();
        await Assert.That((stopped.GetProperty("stage").GetString(), stopped.GetProperty("problem").GetString()))
            .IsEqualTo(("failed", "The numbers did not match, so nothing was sent. Try again, and look that both show the same number."));
    }
}
