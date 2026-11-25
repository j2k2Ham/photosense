using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Trovolve.Api;
using Trovolve.Api.About;
using Trovolve.Api.Hosting;
using Trovolve.Api.Http;
using Trovolve.Application.Organizing;
using Trovolve.Application.Transfer;
using Trovolve.Desktop.Imaging;
using Trovolve.Domain.Entities;
using Trovolve.Domain.Repositories;
using Trovolve.Domain.Services;
using Trovolve.Infrastructure.Library;
using Trovolve.Infrastructure.Transfer;
using Trovolve.Tests.Endpoints;

namespace Trovolve.Tests.Application;

/// <summary>
/// Swipe to clean on a phone: the phone's whole stack on a photo library in memory, asked as the page asks. Nothing is
/// deleted while swiping; what is marked goes at the end, to the phone's Recently Deleted, within the free removals.
/// </summary>
public sealed class SwipeTests : IDisposable
{
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("trovolve-swipe-");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "trovolve-swipe-library-" + Guid.NewGuid().ToString("N"), "Photo Library");
    private readonly FakePhotoLibrary _library = new();
    private readonly TestClock _clock = new();
    private readonly ServiceProvider _phone;
    private readonly ApiRouter _router;

    public SwipeTests()
    {
        _phone = new ServiceCollection()
            .AddTrovolvePhone(_library, new FakePhotoAccess(), new MagickImageAnalyzer(), In("Documents"), In("Caches"), new FakeAdvertiser(), new FakeAppStore(), new FakeCodeScanner(),
                new FakeComputerFinder(), new FakeDevice(), new FakeNotifier(), new FakeShareSheet(), new FakeVaultKeys(), new FakeUnlocker(), new FakeWidgets(), new MemoryLocationSecrets(), new FakeWebSignIn(), "Jamie's iPhone", _root, new TcpTransferListener(0))
            .AddTrovolveApi()
            .AddSingleton(new ProgramInfo("iphone", "1.2.0.57", () => "notices"))
            .AddStandInStores()
            .AddSingleton<TimeProvider>(_clock)
            .BuildServiceProvider();
        _router = _phone.GetRequiredService<TrovolveRoutes>().Router;
    }

    public void Dispose()
    {
        _phone.Dispose();
        TestFiles.Remove(_data);
    }

    private string In(string folder) => Path.Combine(_data.FullName, folder);

    private DateOnly Today => DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);

    /// <summary>A Live Photo, two pictures of different sizes and a video.</summary>
    private void Library()
    {
        _library.Add("AAAAAAAA-1", (LibraryFileKind.Photo, "IMG_0001.JPG", MediaSamples.Picture("LIVE-1")), (LibraryFileKind.PairedVideo, "IMG_0001.MOV", MediaSamples.Movie("LIVE-1")));
        _library.Add("BBBBBBBB-2", (LibraryFileKind.Photo, "IMG_0002.JPG", MediaSamples.Picture()));
        _library.Add("CCCCCCCC-3", (LibraryFileKind.Video, "IMG_0003.MOV", MediaSamples.Movie(creationDate: "2026-10-10T10:00:00+0000")));
        _library.Add("DDDDDDDD-4", (LibraryFileKind.Photo, "IMG_0004.JPG", MediaSamples.Picture("FOUR")));
    }

    private async Task<(HttpStatusCode Status, string Text)> AskAsync(string method, string path, CancellationToken ct, object? body = null, bool asPage = true)
    {
        var answer = await _router.AnswerAsync(method, new Uri("app://trovolve/api/" + path), asPage ? [new("x-trovolve-client", "phone")] : [],
            body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body), ct);
        return ((HttpStatusCode)answer.Status, Encoding.UTF8.GetString(answer.Body));
    }

    private async Task<JsonElement> PhoneAsync(string method, string path, CancellationToken ct, object? body = null)
    {
        var (status, text) = await AskAsync(method, path, ct, body);
        await Assert.That(status).IsEqualTo(HttpStatusCode.OK).Because(text);
        return JsonDocument.Parse(text).RootElement;
    }

    // The library's names by Library's ids, which the swipe answers with.
    private async Task<Dictionary<string, string>> NamesAsync(CancellationToken ct)
        => (await PhoneAsync("GET", $"organize/files?root={Uri.EscapeDataString(_root)}", ct)).GetProperty("files").EnumerateArray()
            .ToDictionary(f => f.GetProperty("id").GetString()!, f => f.GetProperty("name").GetString()!);

    private static string[] Ids(JsonElement state, string list = "set") => [.. state.GetProperty(list).EnumerateArray().Select(c => c.GetProperty("id").GetString()!)];

    private static JsonElement Card(JsonElement state, string id, string list = "set") => state.GetProperty(list).EnumerateArray().Single(c => c.GetProperty("id").GetString() == id);

    private static bool? Decided(JsonElement card) => card.GetProperty("delete").ValueKind == JsonValueKind.Null ? null : card.GetProperty("delete").GetBoolean();

    private Task<JsonElement> DecideAsync(string id, bool delete, CancellationToken ct) => PhoneAsync("POST", "swipe/decide", ct, new { id, delete });

    [Test]
    public async Task A_Days_Set_Is_Drawn_At_Random_From_What_Was_Never_Swiped_A_Live_Photo_As_One_And_Stays_The_Same_All_Day(CancellationToken ct)
    {
        Library();
        var names = await NamesAsync(ct);
        var first = await PhoneAsync("GET", "swipe", ct);
        await Assert.That(first.GetProperty("date").GetString()).IsEqualTo(Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        // Four pictures and videos: the Live Photo's video goes with its picture.
        await Assert.That(Ids(first).Select(id => names[id]).Order()).IsEquivalentTo(["IMG_0001.JPG", "IMG_0002.JPG", "IMG_0003.MOV", "IMG_0004.JPG"], CollectionOrdering.Matching);
        await Assert.That(Ids(first).All(id => Decided(Card(first, id)) is null)).IsTrue();
        await Assert.That(Card(first, Ids(first).Single(id => names[id] == "IMG_0001.JPG")).GetProperty("bytes").GetInt64())
            .IsEqualTo(MediaSamples.Picture("LIVE-1").LongLength + MediaSamples.Movie("LIVE-1").LongLength);
        await Assert.That((first.GetProperty("unswiped").GetInt32(), first.GetProperty("streak").GetInt32(), first.GetProperty("freed").ValueKind)).IsEqualTo((0, 0, JsonValueKind.Null));
        await Assert.That(first.GetProperty("week").EnumerateArray().Select(d => d.GetProperty("swiped").GetBoolean())).IsEquivalentTo(Enumerable.Repeat(false, 7));
        await Assert.That(first.GetProperty("week")[6].GetProperty("date").GetString()).IsEqualTo(first.GetProperty("date").GetString());

        // Asked again, the same set in the same order.
        await Assert.That(Ids(await PhoneAsync("GET", "swipe", ct))).IsEquivalentTo(Ids(first), CollectionOrdering.Matching);

        // What was swiped is never drawn again: the next day's set holds only what was not.
        var kept = Ids(first)[0];
        await DecideAsync(kept, false, ct);
        _clock.Now = _clock.Now.AddDays(1);
        var next = await PhoneAsync("GET", "swipe", ct);
        await Assert.That(Ids(next)).DoesNotContain(kept);
        await Assert.That(Ids(next)).Count().IsEqualTo(3);
    }

    [Test]
    public async Task Each_Photo_Is_Kept_Or_Marked_Undo_Takes_Back_The_Last_And_Nothing_Is_Deleted_While_Swiping(CancellationToken ct)
    {
        Library();
        var set = Ids(await PhoneAsync("GET", "swipe", ct));
        var marked = await DecideAsync(set[0], true, ct);
        var swiped = await DecideAsync(set[1], false, ct);
        await Assert.That((Decided(Card(swiped, set[0])), Decided(Card(swiped, set[1])), Decided(Card(swiped, set[2])))).IsEqualTo(((bool?)true, (bool?)false, (bool?)null));
        await Assert.That(Decided(Card(marked, set[1]))).IsNull();
        await Assert.That((swiped.GetProperty("streak").GetInt32(), swiped.GetProperty("week")[6].GetProperty("swiped").GetBoolean())).IsEqualTo((1, true));
        await Assert.That(_library.RecentlyDeleted).IsEmpty();

        // Swiped already, not in the set, or not said: refused, and nothing changes.
        await Assert.That(await AskAsync("POST", "swipe/decide", ct, new { id = set[0], delete = false })).IsEqualTo((HttpStatusCode.Conflict, "That photo was swiped already. Undo takes back the last one."));
        await Assert.That(await AskAsync("POST", "swipe/decide", ct, new { id = "nothing", delete = true })).IsEqualTo((HttpStatusCode.Conflict, "That photo is not in today's set. Swipe to clean shows another."));
        await Assert.That((await AskAsync("POST", "swipe/decide", ct, new { delete = true })).Status).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await AskAsync("POST", "swipe/decide", ct, "not a request")).Status).IsEqualTo(HttpStatusCode.BadRequest);

        // Undo takes back the last decision, then the one before; with nothing swiped today, the day is no longer swiped.
        var undone = await PhoneAsync("POST", "swipe/undo", ct);
        await Assert.That((Decided(Card(undone, set[0])), Decided(Card(undone, set[1])))).IsEqualTo(((bool?)true, (bool?)null));
        undone = await PhoneAsync("POST", "swipe/undo", ct);
        await Assert.That(Decided(Card(undone, set[0]))).IsNull();
        await Assert.That((undone.GetProperty("streak").GetInt32(), undone.GetProperty("week")[6].GetProperty("swiped").GetBoolean())).IsEqualTo((0, false));
        await Assert.That(await AskAsync("POST", "swipe/undo", ct)).IsEqualTo((HttpStatusCode.Conflict, "Nothing swiped today to undo."));
        // What was taken back can be swiped again, and may be drawn another day.
        await Assert.That(Decided(Card(await DecideAsync(set[0], false, ct), set[0]))).IsFalse();
        await PhoneAsync("POST", "swipe/undo", ct);
        await Assert.That((await _phone.GetRequiredService<ISwipeStore>().SwipedAsync(ct)).Count).IsEqualTo(0);
    }

    [Test]
    public async Task Deleting_Sends_The_Largest_Marked_To_Recently_Deleted_After_The_Phones_Question_Within_The_Free_Removals_And_The_Rest_Stay_Marked(CancellationToken ct)
    {
        Library();
        var names = await NamesAsync(ct);
        var set = Ids(await PhoneAsync("GET", "swipe", ct));
        foreach (var id in set) await DecideAsync(id, names[id] != "IMG_0004.JPG", ct);
        var sized = (await PhoneAsync("GET", "swipe", ct)).GetProperty("set").EnumerateArray().Where(c => c.GetProperty("delete").GetBoolean())
            .OrderByDescending(c => c.GetProperty("bytes").GetInt64()).Select(c => c.GetProperty("id").GetString()!).ToList();
        await Assert.That(sized).Count().IsEqualTo(3);

        // On the summary, one is kept after all, then marked again, then kept; it is not deleted.
        var spare = sized[2];
        await Assert.That(Card(await PhoneAsync("POST", "swipe/spare", ct, new { id = spare, spared = true }), spare).GetProperty("spared").GetBoolean()).IsTrue();
        await Assert.That(Card(await PhoneAsync("POST", "swipe/spare", ct, new { id = spare, spared = false }), spare).GetProperty("spared").GetBoolean()).IsFalse();
        await PhoneAsync("POST", "swipe/spare", ct, new { id = spare, spared = true });
        await Assert.That(await AskAsync("POST", "swipe/spare", ct, new { id = spare, spared = true })).IsEqualTo((HttpStatusCode.Conflict, "That photo is not marked to delete."));
        await Assert.That(await AskAsync("POST", "swipe/spare", ct, new { id = sized[0], spared = false })).IsEqualTo((HttpStatusCode.Conflict, "That photo was not kept on the summary."));
        await Assert.That(await AskAsync("POST", "swipe/spare", ct, new { id = "nothing", spared = true })).IsEqualTo((HttpStatusCode.Conflict, "That photo is not marked to delete."));
        await Assert.That((await AskAsync("POST", "swipe/spare", ct, new { spared = true })).Status).IsEqualTo(HttpStatusCode.BadRequest);

        // The person says no to the phone's question: nothing goes, and nothing is counted.
        _library.Refuse = "You chose not to let Trovolve delete these photos.";
        await Assert.That(await AskAsync("POST", "swipe/delete", ct, new { atMost = 1 })).IsEqualTo((HttpStatusCode.Conflict, "You chose not to let Trovolve delete these photos."));
        await Assert.That(_library.RecentlyDeleted).IsEmpty();

        // The largest goes, with every file of it; the other stays marked.
        var freed = await PhoneAsync("POST", "swipe/delete", ct, new { atMost = 1 });
        await Assert.That((freed.GetProperty("deleted").GetInt32(), freed.GetProperty("left").GetInt32())).IsEqualTo((1, 1));
        await Assert.That(freed.GetProperty("bytes").GetInt64()).IsEqualTo(MediaSamples.Picture("LIVE-1").LongLength + MediaSamples.Movie("LIVE-1").LongLength);
        await Assert.That(_library.RecentlyDeleted).IsEquivalentTo(["AAAAAAAA-1"]);
        await Assert.That((await PhoneAsync("GET", "plan", ct)).GetProperty("leftToday").GetInt32()).IsEqualTo(49);
        var after = await PhoneAsync("GET", "swipe", ct);
        await Assert.That(after.GetProperty("freed").GetProperty("deleted").GetInt32()).IsEqualTo(1);
        await Assert.That(Ids(after)).DoesNotContain(sized[0]);

        // The next day, what stayed marked is waiting from before, beside the new set; over what is left today, nothing goes.
        _clock.Now = _clock.Now.AddDays(1);
        var tomorrow = await PhoneAsync("GET", "swipe", ct);
        await Assert.That(Ids(tomorrow, "earlier")).IsEquivalentTo([sized[1]]);
        await Assert.That(Decided(Card(tomorrow, sized[1], "earlier"))).IsTrue();
        await Assert.That((tomorrow.GetProperty("freed").ValueKind, tomorrow.GetProperty("set").GetArrayLength())).IsEqualTo((JsonValueKind.Null, 0));
        await _phone.GetRequiredService<IPlanStore>().AddRemovedAsync(Today, 50, ct);
        var over = await AskAsync("POST", "swipe/delete", ct);
        await Assert.That((over.Status, over.Text.Contains("50", StringComparison.Ordinal))).IsEqualTo((HttpStatusCode.Conflict, true));
        await _phone.GetRequiredService<IPlanStore>().AddRemovedAsync(Today, -50, ct);
        var rest = await PhoneAsync("POST", "swipe/delete", ct, "not a request");
        await Assert.That((rest.GetProperty("deleted").GetInt32(), rest.GetProperty("left").GetInt32())).IsEqualTo((1, 0));
        // Nothing left marked: nothing is asked.
        await Assert.That((await PhoneAsync("POST", "swipe/delete", ct)).GetProperty("deleted").GetInt32()).IsEqualTo(0);
        await Assert.That(_library.RecentlyDeleted).Count().IsEqualTo(2);
    }

    [Test]
    public async Task The_Streak_Counts_The_Days_In_A_Row_Something_Was_Swiped_Up_To_Yesterday_Until_Todays_First(CancellationToken ct)
    {
        for (var i = 0; i < 8; i++) _library.Add($"EEEEEEEE-{i}", (LibraryFileKind.Photo, $"IMG_10{i:00}.JPG", MediaSamples.Picture($"SEED-{i}")));
        async Task SwipeOneAsync() => await DecideAsync(Ids(await PhoneAsync("GET", "swipe", ct)).First(), false, ct);

        await SwipeOneAsync();
        _clock.Now = _clock.Now.AddDays(1);
        await SwipeOneAsync();
        _clock.Now = _clock.Now.AddDays(1);
        // Before today's first swipe, the streak runs to yesterday.
        var before = await PhoneAsync("GET", "swipe", ct);
        await Assert.That(before.GetProperty("streak").GetInt32()).IsEqualTo(2);
        await Assert.That(before.GetProperty("week").EnumerateArray().Select(d => d.GetProperty("swiped").GetBoolean())).IsEquivalentTo([false, false, false, false, true, true, false], CollectionOrdering.Matching);
        await SwipeOneAsync();
        await Assert.That((await PhoneAsync("GET", "swipe", ct)).GetProperty("streak").GetInt32()).IsEqualTo(3);

        // A day missed ends it.
        _clock.Now = _clock.Now.AddDays(2);
        await Assert.That((await PhoneAsync("GET", "swipe", ct)).GetProperty("streak").GetInt32()).IsEqualTo(0);
        await SwipeOneAsync();
        await Assert.That((await PhoneAsync("GET", "swipe", ct)).GetProperty("streak").GetInt32()).IsEqualTo(1);
    }

    [Test]
    public async Task A_Photo_Is_Known_By_Its_Scans_Checksum_And_One_Deleted_Some_Other_Way_Is_No_Longer_Shown_Or_Deleted(CancellationToken ct)
    {
        Library();
        // The phone's scan worked out one picture's checksum: it is known by it, from one run of Trovolve to the next.
        var files = _phone.GetRequiredService<IPhotoFileSystem>();
        var picture = files.FilesIn(_root).Single(p => p.EndsWith("IMG_0002.JPG", StringComparison.Ordinal));
        var stored = files.Describe(picture)!;
        var sha = Convert.ToHexString(SHA256.HashData(MediaSamples.Picture()));
        await _phone.GetRequiredService<IPhotoRepository>().AddOrUpdateAsync(new Photo
        {
            SourcePath = picture, FileName = "IMG_0002.JPG", FileSizeBytes = stored.Length, FileModifiedUtc = stored.ModifiedUtc, ContentHash = sha,
        }, ct);
        var names = await NamesAsync(ct);
        var set = Ids(await PhoneAsync("GET", "swipe", ct));
        foreach (var id in set) await DecideAsync(id, true, ct);
        await Assert.That((await _phone.GetRequiredService<ISwipeStore>().SwipedAsync(ct))).Contains(sha.ToLowerInvariant());

        // Two are deleted in Photos meanwhile: neither is shown, nor asked about again.
        _library.Remove("BBBBBBBB-2");
        _library.Remove("CCCCCCCC-3");
        var state = await PhoneAsync("GET", "swipe", ct);
        await Assert.That(Ids(state).Select(id => names[id]).Order()).IsEquivalentTo(["IMG_0001.JPG", "IMG_0004.JPG"], CollectionOrdering.Matching);
        var freed = await PhoneAsync("POST", "swipe/delete", ct);
        await Assert.That((freed.GetProperty("deleted").GetInt32(), freed.GetProperty("left").GetInt32())).IsEqualTo((2, 0));
        await Assert.That(_library.RecentlyDeleted.Order()).IsEquivalentTo(["AAAAAAAA-1", "DDDDDDDD-4"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_Photo_On_The_Computer_Says_So_On_Its_Card(CancellationToken ct)
    {
        Library();
        // Its checksum is known from the scan, and the computer holds a file of that checksum.
        var files = _phone.GetRequiredService<IPhotoFileSystem>();
        var picture = files.FilesIn(_root).Single(p => p.EndsWith("IMG_0004.JPG", StringComparison.Ordinal));
        var stored = files.Describe(picture)!;
        await _phone.GetRequiredService<IPhotoRepository>().AddOrUpdateAsync(new Photo
        {
            SourcePath = picture, FileName = "IMG_0004.JPG", FileSizeBytes = stored.Length, FileModifiedUtc = stored.ModifiedUtc,
            ContentHash = Convert.ToHexString(SHA256.HashData(MediaSamples.Picture("FOUR"))),
        }, ct);
        var names = await NamesAsync(ct);
        var ledger = _phone.GetRequiredService<IBackupLedger>();
        await ledger.SaveComputerAsync(new BackupComputer("computer-1", "Studio iMac", "Pictures/Trovolve", null, null), ct);
        await ledger.KeepOnComputerAsync([Convert.ToHexStringLower(SHA256.HashData(MediaSamples.Picture("FOUR")))], true, ct);
        var state = await PhoneAsync("GET", "swipe", ct);
        await Assert.That(Ids(state).Where(id => Card(state, id).GetProperty("onComputer").GetBoolean()).Select(id => names[id])).IsEquivalentTo(["IMG_0004.JPG"]);
    }

    [Test]
    public async Task Only_A_Phone_Has_Swipe_To_Clean_And_Only_The_Page_May_Ask(CancellationToken ct)
    {
        await Assert.That((await AskAsync("GET", "swipe", ct, asPage: false)).Status).IsEqualTo(HttpStatusCode.Unauthorized);

        var computer = new TestApi();
        foreach (var (method, path) in new[] { ("GET", "swipe"), ("POST", "swipe/decide"), ("POST", "swipe/undo"), ("POST", "swipe/spare"), ("POST", "swipe/delete") })
        {
            var refused = await computer.SendAsync((method == "GET" ? Http.Get(path) : Http.Post(path)).FromClient(), ct, path);
            await Assert.That((refused.StatusCode, refused.Text())).IsEqualTo((HttpStatusCode.NotFound, "Only a phone has Swipe to clean."));
        }

        // Swipe to clean over a computer's own disk could not delete what it marked.
        var services = computer.Services;
        var onDisk = new SwipeToClean(new MemorySwipeStore(), services.GetRequiredService<OrganizeLibrary>(), TestFiles.Disk,
            new PhoneBackup(new MemoryBackupLedger(), new FakeComputerFinder(), new MemoryPairs("pc"), services.GetRequiredService<OrganizeLibrary>(), TestFiles.Disk,
                TestPlans.Subscription(), TestPlans.Removals(), new FakeDevice(), new FakeFavorites(), "PC", In("Pictures"), new TestClock(), new MemoryVaultFiles(), new MemoryLocationStore(), TestPlans.Terms(), NullLogger<PhoneBackup>.Instance),
            TestPlans.Removals(), In("Pictures"), new TestClock());
        var asked = await new TestApi(s => s.AddSingleton(onDisk)).SendAsync(Http.Post("swipe/delete").FromClient(), ct, "swipe/delete");
        await Assert.That((asked.StatusCode, asked.Text())).IsEqualTo((HttpStatusCode.Conflict, "Only a phone deletes what is swiped."));
    }
}
