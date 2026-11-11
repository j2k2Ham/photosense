using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using PhotoSense.Application.Scanning;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Matching;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;

namespace PhotoSense.Tests.Endpoints;

/// <summary>A scan's status, progress and groups, and forgetting what scans recorded.</summary>
public sealed class ScanAndStatusEndpointsTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("photosense-scanapi-");
    private readonly InMemoryScanProgressStore _progress = new(new TestClock());

    public void Dispose() => TestFiles.Remove(_root);

    // ---- forgetting earlier results

    // The scanning addresses, with the test's progress and whatever else it gives.
    private ScanApi Scans(Action<IServiceCollection>? with = null)
        => new(new TestApi(s => { s.AddSingleton<IScanProgressStore>(_progress); with?.Invoke(s); }));

    private (ScanApi Reset, InMemoryPhotoRepository Repo, InMemoryThumbnailStore Thumbnails) Resetter()
    {
        var repo = new InMemoryPhotoRepository();
        var thumbnails = new InMemoryThumbnailStore();
        return (Scans(s => s.AddSingleton<IPhotoRepository>(repo).AddSingleton<IThumbnailStore>(thumbnails)), repo, thumbnails);
    }

    [Test]
    public async Task Clearing_Forgets_Every_Record_And_Preview_And_Says_How_Many(CancellationToken ct)
    {
        var (reset, repo, thumbnails) = Resetter();
        await repo.AddOrUpdateAsync(TestPhotos.Make("a.jpg"), ct);
        await repo.AddOrUpdateAsync(TestPhotos.Make("b.jpg"), ct);
        await thumbnails.SaveAsync("AB12", [1], ct);
        // A scan that has finished does not stand in the way.
        _progress.ScanStarted("earlier");
        _progress.ScanCompleted("earlier");

        var response = await reset.ResetAsync(Http.Post("scan/reset").FromClient(), ct);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Json().GetProperty("forgotten").GetInt32()).IsEqualTo(2);
        await Assert.That(await repo.GetAllAsync(ct)).IsEmpty();
        await Assert.That(thumbnails.Saved).IsEmpty();

        // With nothing left there is nothing to forget, and that is no error.
        await Assert.That((await reset.ResetAsync(Http.Post("scan/reset").FromClient(), ct)).Json().GetProperty("forgotten").GetInt32()).IsEqualTo(0);
    }

    [Test]
    public async Task Results_Cannot_Be_Cleared_From_Under_A_Running_Scan(CancellationToken ct)
    {
        var (reset, repo, thumbnails) = Resetter();
        await repo.AddOrUpdateAsync(TestPhotos.Make("a.jpg"), ct);
        await thumbnails.SaveAsync("AB12", [1], ct);
        _progress.ScanStarted("running");

        var response = await reset.ResetAsync(Http.Post("scan/reset").FromClient(), ct);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(response.Json().GetProperty("error").GetString()).Contains("A scan is running");
        await Assert.That(await repo.GetAllAsync(ct)).HasSingleItem();
        await Assert.That(thumbnails.Saved).HasSingleItem();
    }

    [Test]
    public async Task Only_The_UI_May_Clear_The_Results(CancellationToken ct)
    {
        var (reset, repo, _) = Resetter();
        await repo.AddOrUpdateAsync(TestPhotos.Make("a.jpg"), ct);

        var response = await reset.ResetAsync(Http.Post("scan/reset"), ct);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(await repo.GetAllAsync(ct)).HasSingleItem();
    }

    // ---- progress and status

    [Test]
    public async Task Progress_Is_Reported_For_A_Scan_That_Was_Started_And_Not_For_An_Unknown_One(CancellationToken ct)
    {
        _progress.ScanStarted("scan-1");
        _progress.SetTotals("scan-1", 4, 2);
        _progress.IncrementProcessed("scan-1", primary: true);
        _progress.IncrementProcessed("scan-1", primary: false);
        _progress.Read("scan-1", "6f1c1c1e-0000-4000-8000-000000000001");
        var function = Scans();

        var json = (await function.GetAsync(Http.Get("scan/progress/scan-1"), "scan-1", ct)).Json();
        await Assert.That((json.GetProperty("instanceId").GetString(), json.GetProperty("primaryTotal").GetInt32(),
            json.GetProperty("primaryProcessed").GetInt32(), json.GetProperty("secondaryTotal").GetInt32(), json.GetProperty("secondaryProcessed").GetInt32())).IsEqualTo(("scan-1", 4, 1, 2, 1));
        await Assert.That(json.GetProperty("primaryPercent").GetDouble()).IsEqualTo(25);
        await Assert.That(json.GetProperty("recent").EnumerateArray().Single().GetString()).IsEqualTo("6f1c1c1e-0000-4000-8000-000000000001");
        await Assert.That(json.GetProperty("completedUtc").ValueKind).IsEqualTo(System.Text.Json.JsonValueKind.Null);
        // Nothing to go by yet: no earlier scan, and too little of this one.
        await Assert.That(json.GetProperty("secondsLeft").ValueKind).IsEqualTo(System.Text.Json.JsonValueKind.Null);

        // Going by earlier scans, less the moment that has passed since it started.
        _progress.Expect("scan-1", 780);
        var left = (await function.GetAsync(Http.Get("scan/progress/scan-1"), "scan-1", ct)).Json().GetProperty("secondsLeft").GetDouble();
        await Assert.That(left).IsGreaterThanOrEqualTo(720).And.IsLessThanOrEqualTo(780);
        _progress.ScanCompleted("scan-1");
        await Assert.That((await function.GetAsync(Http.Get("scan/progress/scan-1"), "scan-1", ct)).Json().GetProperty("secondsLeft").GetDouble()).IsEqualTo(0);

        await Assert.That((await function.GetAsync(Http.Get("scan/progress/other"), "other", ct)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task The_Time_Left_Counts_Down_From_What_Earlier_Scans_Took(CancellationToken ct)
    {
        var clock = new TestClock();
        var progress = new InMemoryScanProgressStore(clock);
        progress.ScanStarted("scan-1");
        progress.Expect("scan-1", 780);
        clock.Now += TimeSpan.FromMinutes(1);

        var json = (await new ScanApi(new TestApi(s => s.AddSingleton<IScanProgressStore>(progress).AddSingleton<TimeProvider>(clock))).GetAsync(Http.Get("scan/progress/scan-1"), "scan-1", ct)).Json();

        await Assert.That(json.GetProperty("secondsLeft").GetDouble()).IsEqualTo(720);
    }

    [Test]
    public async Task Status_Describes_The_Latest_Scan_And_Counts_The_Photos(CancellationToken ct)
    {
        var repo = new InMemoryPhotoRepository();
        await repo.AddOrUpdateAsync(TestPhotos.Make("a.jpg"), ct);
        _progress.ScanStarted("scan-1");
        _progress.SetTotals("scan-1", 2, 0);
        _progress.ScanCompleted("scan-1");

        var json = (await Scans(s => s.AddSingleton<IPhotoRepository>(repo)).GetStatusAsync(Http.Get("scan/status"), ct)).Json();

        await Assert.That((json.GetProperty("instanceId").GetString(), json.GetProperty("primaryTotal").GetInt32(), json.GetProperty("totalPhotos").GetInt32())).IsEqualTo(("scan-1", 2, 1));
        await Assert.That(json.GetProperty("completed").ValueKind).IsNotEqualTo(System.Text.Json.JsonValueKind.Null);
    }

    [Test]
    [Arguments(null, null, false, false)]
    [Arguments("C:/photos", "D:/backup", true, true)]
    public async Task Health_Says_Which_Folders_Are_Configured(string? primary, string? secondary, bool hasPrimary, bool hasSecondary, CancellationToken ct)
    {
        var options = Options.Create(new PhotoStorageOptions { PrimaryPath = primary ?? "", SecondaryPath = secondary ?? "" });
        var json = (await new AboutApi(new TestApi(s => s.AddSingleton(options))).HealthAsync(Http.Get("health"), ct)).Json();
        await Assert.That((json.GetProperty("status").GetString(), json.GetProperty("storagePrimaryConfigured").GetBoolean(), json.GetProperty("storageSecondaryConfigured").GetBoolean())).IsEqualTo(("ok", hasPrimary, hasSecondary));
    }

    // ---- groups

    [Test]
    [Arguments("scan/groups", false, null, false, 1, 50)]
    [Arguments("scan/groups?mode=SIMILAR&q=img&hideKept=true&page=3&pageSize=10", true, "img", true, 3, 10)]
    [Arguments("scan/groups?mode=other&hideKept=false&page=0&pageSize=0", false, null, false, 1, 1)]
    [Arguments("scan/groups?page=-4&pageSize=5000", false, null, false, 1, 200)]
    [Arguments("scan/groups?page=x&pageSize=y", false, null, false, 1, 50)]
    public async Task The_Groups_Query_Is_Read_With_Sensible_Limits(string url, bool similar, string? text, bool hideKept, int page, int pageSize, CancellationToken ct)
    {
        var analysis = new Mock<IDuplicateAnalysisService>();
        analysis.Setup(a => a.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new DuplicateAnalysis([], []));
        var function = Scans(s => s.AddSingleton(analysis.Object));

        var json = (await function.GetDuplicateGroupsAsync(Http.Get(url).FromClient(), ct)).Json();

        await Assert.That((json.GetProperty("mode").GetString(), json.GetProperty("page").GetInt32(), json.GetProperty("pageSize").GetInt32())).IsEqualTo((similar ? "similar" : "duplicates", page, pageSize));
        _ = (text, hideKept); // both only filter, and there is nothing here to filter
    }

    [Test]
    public async Task Groups_Nobody_Is_Waiting_For_Any_More_Are_Given_Up(CancellationToken ct)
    {
        var analysis = new Mock<IDuplicateAnalysisService>();
        analysis.Setup(a => a.GetAsync(It.Is<CancellationToken>(t => t.IsCancellationRequested))).ThrowsAsync(new OperationCanceledException());
        var function = Scans(s => s.AddSingleton(analysis.Object));

        await Assert.ThrowsAsync<OperationCanceledException>(() => function.GetDuplicateGroupsAsync(Http.Get("scan/groups").FromClient(), new CancellationToken(canceled: true)));
    }

    [Test]
    public async Task A_Photo_Is_Described_With_A_Place_Only_When_It_Has_A_Position(CancellationToken ct)
    {
        var places = new Mock<IPlaceNameResolver>();
        places.Setup(p => p.Describe(35.2, -80.8)).Returns("Charlotte, North Carolina, US");
        var located = TestPhotos.Make("a.jpg"); located.Latitude = 35.2; located.Longitude = -80.8;
        var half = TestPhotos.Make("b.jpg"); half.Latitude = 35.2;
        var other = TestPhotos.Make("c.jpg"); other.Longitude = -80.8;
        var video = TestPhotos.Video("clip.MOV"); video.DurationSeconds = 12.5;
        var members = new[] { half, other, TestPhotos.Make("d.jpg"), video, new PhotoSense.Domain.Entities.Photo { SourcePath = "e.jpg", FileName = "e.jpg" },
            new PhotoSense.Domain.Entities.Photo { SourcePath = "", FileName = "f.jpg" } };
        var group = new DuplicateGroup(located, members.Select(m => new DuplicateMember(m, MatchKind.Identical, 0, 0)).ToList());

        // As the page is sent each photo: the keeper and its members of the one group there is.
        var shown = (await new GroupPages([group], [], places.Object).BuildAsync(false, null, false, 1, 10, ct)).Items.Single();
        var sent = shown.Members.Select(m => m.Photo).Append(shown.Keeper).ToDictionary(p => p.FileName);
        await Assert.That(sent["a.jpg"].PlaceName).IsEqualTo("Charlotte, North Carolina, US");
        await Assert.That(sent["b.jpg"].PlaceName).IsNull();
        await Assert.That(sent["c.jpg"].PlaceName).IsNull();
        await Assert.That(sent["d.jpg"].PlaceName).IsNull();
        await Assert.That((sent["clip.MOV"].IsVideo, sent["clip.MOV"].DurationSeconds, sent["clip.MOV"].Format)).IsEqualTo((true, 12.5, "MOV"));
        await Assert.That(sent["e.jpg"].Folder).IsEqualTo(string.Empty);
        await Assert.That(sent["f.jpg"].Folder).IsEqualTo(string.Empty);
    }
}
