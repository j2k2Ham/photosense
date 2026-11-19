using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Api;
using PhotoSense.Api.About;
using PhotoSense.Api.Hosting;
using PhotoSense.Api.Http;
using PhotoSense.Application.Plans;
using PhotoSense.Desktop.Imaging;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Infrastructure.Library;
using PhotoSense.Infrastructure.Transfer;
using PhotoSense.Tests.Endpoints;

namespace PhotoSense.Tests.Application;

/// <summary>
/// A phone's notifications: off until turned on, which asks the phone's own question; the low-storage alert, at most once
/// a week, saying what Free up space would free; and the reminder two days before a free trial ends.
/// </summary>
public sealed class NoticesTests : IDisposable
{
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("photosense-notices-");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "photosense-notices-library-" + Guid.NewGuid().ToString("N"), "Photo Library");
    private readonly FakePhotoLibrary _library = new();
    private readonly FakeAppStore _app = new();
    private readonly FakeNotifier _notifier = new();
    private readonly FakeDevice _device = new() { Storage = new DeviceStorage(128_000_000_000, 6_400_000_000) };
    private readonly TestClock _clock = new();
    private readonly ServiceProvider _phone;
    private readonly ApiRouter _router;

    public NoticesTests()
    {
        _phone = new ServiceCollection()
            .AddPhotoSensePhone(_library, new FakePhotoAccess(), new MagickImageAnalyzer(), In("Documents"), In("Caches"), new FakeAdvertiser(), _app, new FakeCodeScanner(),
                new FakeComputerFinder(), _device, _notifier, new FakeShareSheet(), new FakeVaultKeys(), new FakeUnlocker(), "Jamie's iPhone", _root, new TcpTransferListener(0))
            .AddPhotoSenseApi()
            .AddSingleton(new ProgramInfo("iphone", "1.2.0.57", () => "notices"))
            .AddStandInStores()
            .AddSingleton<TimeProvider>(_clock)
            .BuildServiceProvider();
        _router = _phone.GetRequiredService<PhotoSenseRoutes>().Router;
    }

    public void Dispose()
    {
        _phone.Dispose();
        TestFiles.Remove(_data);
    }

    private string In(string folder) => Path.Combine(_data.FullName, folder);

    private PhoneNotices Notices => _phone.GetRequiredService<PhoneNotices>();

    private async Task<(HttpStatusCode Status, string Text)> AskAsync(string method, string path, CancellationToken ct, object? body = null, bool asPage = true)
    {
        var answer = await _router.AnswerAsync(method, new Uri("app://photosense/api/" + path), asPage ? [new("x-photosense-client", "phone")] : [],
            body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body), ct);
        return ((HttpStatusCode)answer.Status, Encoding.UTF8.GetString(answer.Body));
    }

    private async Task<JsonElement> PhoneAsync(string method, string path, CancellationToken ct, object? body = null)
    {
        var (status, text) = await AskAsync(method, path, ct, body);
        await Assert.That(status).IsEqualTo(HttpStatusCode.OK).Because(text);
        return JsonDocument.Parse(text).RootElement;
    }

    private static (bool, bool, bool) State(JsonElement s)
        => (s.GetProperty("lowStorage").GetBoolean(), s.GetProperty("trialReminder").GetBoolean(), s.GetProperty("allowed").GetBoolean());

    private async Task ScanAsync(CancellationToken ct)
    {
        await Assert.That((await AskAsync("POST", "scan/start", ct, new { primaryLocation = _root, recursive = true })).Status).IsEqualTo(HttpStatusCode.Accepted);
        for (var waited = 0; (await PhoneAsync("GET", "scan/status", ct)).GetProperty("completed").ValueKind == JsonValueKind.Null; waited++)
        {
            if (waited == 600) throw new TimeoutException("The scan did not finish.");
            await Task.Delay(50, ct);
        }
    }

    /// <summary>Two screenshots and a copy of a picture: what Free up space would free.</summary>
    private async Task SomethingToFreeAsync(CancellationToken ct)
    {
        _library.Add("AAAAAAAA-1", (LibraryFileKind.Photo, "Screenshot 2026-11-17.png", MediaSamples.Picture("SHOT-1")));
        _library.Add("BBBBBBBB-2", (LibraryFileKind.Photo, "Screenshot 2026-11-18.png", MediaSamples.Picture("SHOT-2")));
        _library.Add("CCCCCCCC-3", (LibraryFileKind.Photo, "IMG_0003.JPG", MediaSamples.Picture("THREE")));
        _library.Add("DDDDDDDD-4", (LibraryFileKind.Photo, "IMG_0003 (1).JPG", MediaSamples.Picture("THREE")));
        await ScanAsync(ct);
    }

    // What Free up space shows: the duplicates' copies, the screenshots and the blurry shots, each photo once.
    private async Task<long> FreeUpSpaceShowsAsync(CancellationToken ct)
    {
        var seen = new Dictionary<Guid, long>();
        foreach (var kind in new[] { "duplicates", "screenshots", "blurry" })
            foreach (var photo in (await PhoneAsync("GET", $"cleanup/categories/{kind}", ct)).EnumerateArray())
                seen[photo.GetProperty("id").GetGuid()] = photo.GetProperty("fileSizeBytes").GetInt64();
        return seen.Values.Sum();
    }

    [Test]
    public async Task Notifications_Are_Off_Until_Turned_On_And_Turning_One_On_Asks_The_Phone_Once(CancellationToken ct)
    {
        await Assert.That(State(await PhoneAsync("GET", "notices", ct))).IsEqualTo((false, false, false));
        await Assert.That(State(await PhoneAsync("POST", "notices", ct, new { lowStorage = true }))).IsEqualTo((true, false, true));
        await Assert.That(State(await PhoneAsync("POST", "notices", ct, new { trialReminder = true }))).IsEqualTo((true, true, true));
        await Assert.That(State(await PhoneAsync("POST", "notices", ct, new { lowStorage = false }))).IsEqualTo((false, true, true));
        await Assert.That(_notifier.Asked).IsEqualTo(1);
        await Assert.That(State(await PhoneAsync("GET", "notices", ct))).IsEqualTo((false, true, true));
    }

    [Test]
    public async Task Refused_By_The_Person_A_Notification_Stays_Off_And_Turning_One_Off_Asks_Nothing(CancellationToken ct)
    {
        _notifier.Answer = false;
        await Assert.That(State(await PhoneAsync("POST", "notices", ct, new { trialReminder = false }))).IsEqualTo((false, false, false));
        await Assert.That(_notifier.Asked).IsEqualTo(0);
        await Assert.That(State(await PhoneAsync("POST", "notices", ct, new { lowStorage = true, trialReminder = true }))).IsEqualTo((false, false, false));
        await Assert.That(_notifier.Asked).IsEqualTo(1);
        await Assert.That(await AskAsync("POST", "notices", ct, new { })).IsEqualTo((HttpStatusCode.BadRequest, "Say which notifications to turn on or off."));
        await Assert.That(await AskAsync("POST", "notices", ct)).IsEqualTo((HttpStatusCode.BadRequest, "Say which notifications to turn on or off."));
    }

    [Test]
    public async Task Low_Storage_Is_Alerted_Once_A_Week_With_What_Free_Up_Space_Shows(CancellationToken ct)
    {
        await SomethingToFreeAsync(ct);
        // Off, nothing is sent.
        await Assert.That(await Notices.CheckStorageAsync(ct)).IsFalse();
        await PhoneAsync("POST", "notices", ct, new { lowStorage = true });

        await Assert.That(await Notices.CheckStorageAsync(ct)).IsTrue();
        var bytes = await FreeUpSpaceShowsAsync(ct);
        await Assert.That(bytes).IsGreaterThan(0);
        await Assert.That(_notifier.Shown.Single())
            .IsEqualTo(new Notice(PhotoSense.Domain.Services.Notices.LowStorage, "iPhone storage is 95% full", $"PhotoSense found {Sizes.Of(bytes)} you can free.", PhotoSense.Domain.Services.Notices.FreeUpSpace));

        // Not again the same week; again a week on.
        _clock.Now += TimeSpan.FromDays(6);
        await Assert.That(await Notices.CheckStorageAsync(ct)).IsFalse();
        _clock.Now += TimeSpan.FromDays(1);
        await Assert.That(await Notices.CheckStorageAsync(ct)).IsTrue();
        await Assert.That(_notifier.Shown.Count).IsEqualTo(2);
    }

    [Test]
    public async Task No_Alert_With_Room_To_Spare_Nothing_To_Free_Or_Notifications_Turned_Off_In_The_Phones_Settings(CancellationToken ct)
    {
        await PhoneAsync("POST", "notices", ct, new { lowStorage = true });
        // Nothing to free yet.
        await Assert.That(await Notices.CheckStorageAsync(ct)).IsFalse();
        await SomethingToFreeAsync(ct);

        _device.Storage = new DeviceStorage(128_000_000_000, 12_800_000_000);
        await Assert.That(await Notices.CheckStorageAsync(ct)).IsFalse();
        _device.Storage = new DeviceStorage(0, 0);
        await Assert.That(await Notices.CheckStorageAsync(ct)).IsFalse();
        _device.Storage = new DeviceStorage(128_000_000_000, 1_000_000_000);
        _notifier.Allowed = false;
        await Assert.That(await Notices.CheckStorageAsync(ct)).IsFalse();
        await Assert.That(_notifier.Shown).IsEmpty();
    }

    [Test]
    public async Task An_Android_Phone_Or_Tablet_Starts_The_Alert_With_Its_Own_Word(CancellationToken ct)
    {
        await SomethingToFreeAsync(ct);
        await PhoneAsync("POST", "notices", ct, new { lowStorage = true });
        _device.Kind = "Android tablet";
        await Notices.CheckStorageAsync(ct);
        await Assert.That(_notifier.Shown.Single().Title).IsEqualTo("Tablet storage is 95% full");
    }

    [Test]
    public async Task The_Trial_Is_Reminded_Of_Two_Days_Before_It_Ends_With_The_Price_After(CancellationToken ct)
    {
        var end = TestClock.Day.AddDays(7);
        _app.Purchases.Add(new StorePurchase(PlanProducts.Yearly, TestClock.Day, end, Trial: true));
        await PhoneAsync("POST", "notices", ct, new { trialReminder = true });
        var (notice, at) = _notifier.Scheduled[PhotoSense.Domain.Services.Notices.TrialReminder];
        await Assert.That((notice.Title, notice.Body, notice.Opens, at))
            .IsEqualTo(("Your free trial ends in 2 days", "Then $19.99 a year, unless you cancel. Cancel any time in Settings.", PhotoSense.Domain.Services.Notices.Subscription, end.AddDays(-2)));

        // The store out of reach, the price is not said.
        _app.Unreachable = true;
        await Notices.RemindAsync(ct);
        await Assert.That(_notifier.Scheduled[PhotoSense.Domain.Services.Notices.TrialReminder].Notice.Body)
            .IsEqualTo("Then it renews as the yearly plan, unless you cancel. Cancel any time in Settings.");
        _app.Unreachable = false;

        // Within two days of its end, or turned off, it is taken back.
        _clock.Now = end.AddDays(-2);
        await Notices.RemindAsync(ct);
        await Assert.That(_notifier.Scheduled).IsEmpty();
        _clock.Now = TestClock.Day;
        await Notices.RemindAsync(ct);
        await PhoneAsync("POST", "notices", ct, new { trialReminder = false });
        await Assert.That(_notifier.Scheduled).IsEmpty();
    }

    [Test]
    public async Task No_Reminder_Is_Set_Without_A_Trial(CancellationToken ct)
    {
        await PhoneAsync("POST", "notices", ct, new { trialReminder = true });
        await Assert.That(_notifier.Scheduled).IsEmpty();
        _app.Purchases.Add(new StorePurchase(PlanProducts.Lifetime, TestClock.Day, null, Trial: false));
        await Notices.RemindAsync(ct);
        await Assert.That(_notifier.Scheduled).IsEmpty();
    }

    [Test]
    public async Task A_Notification_Tapped_Opens_What_It_Is_About_Once(CancellationToken ct)
    {
        _notifier.Opened = PhotoSense.Domain.Services.Notices.FreeUpSpace;
        await Assert.That((await PhoneAsync("GET", "notices/opened", ct)).GetProperty("opens").GetString()).IsEqualTo("free-up-space");
        await Assert.That((await PhoneAsync("GET", "notices/opened", ct)).GetProperty("opens").ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    [Test]
    public async Task Only_A_Phone_Sends_Notifications_And_Only_The_Page_May_Ask(CancellationToken ct)
    {
        await Assert.That((await AskAsync("GET", "notices", ct, asPage: false)).Status).IsEqualTo(HttpStatusCode.Unauthorized);
        var computer = new TestApi();
        foreach (var (method, path) in new[] { ("GET", "notices"), ("POST", "notices"), ("GET", "notices/opened") })
        {
            var refused = await computer.SendAsync((method == "GET" ? Http.Get(path) : Http.Post(path)).FromClient(), ct, path);
            await Assert.That((refused.StatusCode, refused.Text())).IsEqualTo((HttpStatusCode.NotFound, "Only a phone sends notifications."));
        }
    }

    [Test]
    public async Task The_Settings_Are_Kept_In_The_Database(CancellationToken ct)
    {
        using var db = new LiteDB.LiteDatabase(new MemoryStream());
        var store = db.Notices();
        await Assert.That(await store.GetAsync(ct)).IsEqualTo(NoticeSettings.None);
        var sent = new DateTimeOffset(2026, 11, 17, 9, 41, 0, TimeSpan.Zero);
        await store.SaveAsync(new NoticeSettings(true, false, sent), ct);
        await Assert.That(await db.Notices().GetAsync(ct)).IsEqualTo(new NoticeSettings(true, false, sent));
        await store.SaveAsync(new NoticeSettings(false, true, null), ct);
        await Assert.That(await store.GetAsync(ct)).IsEqualTo(new NoticeSettings(false, true, null));
    }
}
