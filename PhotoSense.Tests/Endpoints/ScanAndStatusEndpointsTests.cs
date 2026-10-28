using System.Net;
using Microsoft.Extensions.Options;
using Moq;
using PhotoSense.Application.Scanning;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.DTOs;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Api.Endpoints;
using PhotoSense.Api.Scanning;
using PhotoSense.Infrastructure.Persistence;

namespace PhotoSense.Tests.Endpoints;

public sealed class ScanAndStatusEndpointsTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("photosense-scanapi-");
    private readonly InMemoryScanProgressStore _progress = new();

    public void Dispose() => TestFiles.Remove(_root);

    // ---- forgetting earlier results

    private (ScanResetEndpoints Reset, InMemoryPhotoRepository Repo, InMemoryThumbnailStore Thumbnails) Resetter()
    {
        var repo = new InMemoryPhotoRepository();
        var thumbnails = new InMemoryThumbnailStore();
        return (new ScanResetEndpoints(repo, thumbnails, _progress), repo, thumbnails);
    }

    [Test]
    public async Task Clearing_Forgets_Every_Record_And_Preview_And_Says_How_Many()
    {
        var (reset, repo, thumbnails) = Resetter();
        await repo.AddOrUpdateAsync(TestPhotos.Make("a.jpg"));
        await repo.AddOrUpdateAsync(TestPhotos.Make("b.jpg"));
        await thumbnails.SaveAsync("AB12", [1]);
        // A scan that has finished does not stand in the way.
        _progress.ScanStarted("earlier");
        _progress.ScanCompleted("earlier");

        var response = await reset.ResetAsync(Http.Post("scan/reset").FromClient());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Json().GetProperty("forgotten").GetInt32()).IsEqualTo(2);
        await Assert.That(await repo.GetAllAsync()).IsEmpty();
        await Assert.That(thumbnails.Saved).IsEmpty();

        // With nothing left there is nothing to forget, and that is no error.
        await Assert.That((await reset.ResetAsync(Http.Post("scan/reset").FromClient())).Json().GetProperty("forgotten").GetInt32()).IsEqualTo(0);
    }

    [Test]
    public async Task Results_Cannot_Be_Cleared_From_Under_A_Running_Scan()
    {
        var (reset, repo, thumbnails) = Resetter();
        await repo.AddOrUpdateAsync(TestPhotos.Make("a.jpg"));
        await thumbnails.SaveAsync("AB12", [1]);
        _progress.ScanStarted("running");

        var response = await reset.ResetAsync(Http.Post("scan/reset").FromClient());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(response.Json().GetProperty("error").GetString()).Contains("A scan is running");
        await Assert.That(await repo.GetAllAsync()).HasSingleItem();
        await Assert.That(thumbnails.Saved).HasSingleItem();
    }

    [Test]
    public async Task Only_The_UI_May_Clear_The_Results()
    {
        var (reset, repo, _) = Resetter();
        await repo.AddOrUpdateAsync(TestPhotos.Make("a.jpg"));

        var response = await reset.ResetAsync(Http.Post("scan/reset"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(await repo.GetAllAsync()).HasSingleItem();
    }

    // ---- progress and status

    [Test]
    public async Task Progress_Is_Reported_For_A_Scan_That_Was_Started_And_Not_For_An_Unknown_One()
    {
        _progress.ScanStarted("scan-1");
        _progress.SetTotals("scan-1", 4, 2);
        _progress.IncrementProcessed("scan-1", primary: true);
        _progress.IncrementProcessed("scan-1", primary: false);
        var function = new ScanProgressEndpoints(_progress);

        var json = (await function.Get(Http.Get("scan/progress/scan-1"), "scan-1")).Json();
        await Assert.That((json.GetProperty("instanceId").GetString(), json.GetProperty("primaryTotal").GetInt32(),
            json.GetProperty("primaryProcessed").GetInt32(), json.GetProperty("secondaryTotal").GetInt32(), json.GetProperty("secondaryProcessed").GetInt32())).IsEqualTo(("scan-1", 4, 1, 2, 1));
        await Assert.That(json.GetProperty("primaryPercent").GetDouble()).IsEqualTo(25);
        await Assert.That(json.GetProperty("completedUtc").ValueKind).IsEqualTo(System.Text.Json.JsonValueKind.Null);
        // Nothing to go by yet: no earlier scan, and too little of this one.
        await Assert.That(json.GetProperty("secondsLeft").ValueKind).IsEqualTo(System.Text.Json.JsonValueKind.Null);

        // Going by earlier scans, less the moment that has passed since it started.
        _progress.Expect("scan-1", 780);
        var left = (await function.Get(Http.Get("scan/progress/scan-1"), "scan-1")).Json().GetProperty("secondsLeft").GetDouble();
        await Assert.That(left).IsGreaterThanOrEqualTo(720).And.IsLessThanOrEqualTo(780);
        _progress.ScanCompleted("scan-1");
        await Assert.That((await function.Get(Http.Get("scan/progress/scan-1"), "scan-1")).Json().GetProperty("secondsLeft").GetDouble()).IsEqualTo(0);

        await Assert.That((await function.Get(Http.Get("scan/progress/other"), "other")).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task The_Time_Left_Counts_Down_From_What_Earlier_Scans_Took()
    {
        var clock = new TestClock();
        var progress = new InMemoryScanProgressStore(clock);
        progress.ScanStarted("scan-1");
        progress.Expect("scan-1", 780);
        clock.Now += TimeSpan.FromMinutes(1);

        var json = (await new ScanProgressEndpoints(progress, clock).Get(Http.Get("scan/progress/scan-1"), "scan-1")).Json();

        await Assert.That(json.GetProperty("secondsLeft").GetDouble()).IsEqualTo(720);
    }

    [Test]
    public async Task Status_Describes_The_Latest_Scan_And_Counts_The_Photos()
    {
        var repo = new InMemoryPhotoRepository();
        await repo.AddOrUpdateAsync(TestPhotos.Make("a.jpg"));
        _progress.ScanStarted("scan-1");
        _progress.SetTotals("scan-1", 2, 0);
        _progress.ScanCompleted("scan-1");

        var json = (await new ScanStatusEndpoints(repo, _progress).GetStatus(Http.Get("scan/status"))).Json();

        await Assert.That((json.GetProperty("instanceId").GetString(), json.GetProperty("primaryTotal").GetInt32(), json.GetProperty("totalPhotos").GetInt32())).IsEqualTo(("scan-1", 2, 1));
        await Assert.That(json.GetProperty("completed").ValueKind).IsNotEqualTo(System.Text.Json.JsonValueKind.Null);
    }

    [Test]
    [Arguments(null, null, false, false)]
    [Arguments("C:/photos", "D:/backup", true, true)]
    public async Task Health_Says_Which_Folders_Are_Configured(string? primary, string? secondary, bool hasPrimary, bool hasSecondary)
    {
        var options = Options.Create(new PhotoStorageOptions { PrimaryPath = primary ?? "", SecondaryPath = secondary ?? "" });
        var json = (await new HealthEndpoints(options).GetAsync(Http.Get("health"))).Json();
        await Assert.That((json.GetProperty("status").GetString(), json.GetProperty("storagePrimaryConfigured").GetBoolean(), json.GetProperty("storageSecondaryConfigured").GetBoolean())).IsEqualTo(("ok", hasPrimary, hasSecondary));
    }

    // ---- groups

    [Test]
    [Arguments("scan/groups", false, null, false, 1, 50)]
    [Arguments("scan/groups?mode=SIMILAR&q=img&hideKept=true&page=3&pageSize=10", true, "img", true, 3, 10)]
    [Arguments("scan/groups?mode=other&hideKept=false&page=0&pageSize=0", false, null, false, 1, 1)]
    [Arguments("scan/groups?page=-4&pageSize=5000", false, null, false, 1, 200)]
    [Arguments("scan/groups?page=x&pageSize=y", false, null, false, 1, 50)]
    public async Task The_Groups_Query_Is_Read_With_Sensible_Limits(string url, bool similar, string? text, bool hideKept, int page, int pageSize)
    {
        var analysis = new Mock<IDuplicateAnalysisService>();
        analysis.Setup(a => a.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new DuplicateAnalysis([], []));
        var function = new DuplicateGroupsEndpoints(new ScanGroupingFacade(analysis.Object, new PhotoDtoMapper(Mock.Of<IPlaceNameResolver>()), new PhotoRanking()));

        var json = (await function.GetDuplicateGroups(Http.Get(url).FromClient())).Json();

        await Assert.That((json.GetProperty("mode").GetString(), json.GetProperty("page").GetInt32(), json.GetProperty("pageSize").GetInt32())).IsEqualTo((similar ? "similar" : "duplicates", page, pageSize));
        _ = (text, hideKept); // both only filter, and there is nothing here to filter
    }

    [Test]
    public async Task Groups_Nobody_Is_Waiting_For_Any_More_Are_Given_Up()
    {
        var analysis = new Mock<IDuplicateAnalysisService>();
        analysis.Setup(a => a.GetAsync(It.Is<CancellationToken>(t => t.IsCancellationRequested))).ThrowsAsync(new OperationCanceledException());
        var function = new DuplicateGroupsEndpoints(new ScanGroupingFacade(analysis.Object, new PhotoDtoMapper(Mock.Of<IPlaceNameResolver>()), new PhotoRanking()));

        await Assert.ThrowsAsync<OperationCanceledException>(() => function.GetDuplicateGroups(Http.Abandoned("scan/groups").FromClient()));
    }

    [Test]
    public async Task A_Photo_Is_Described_With_A_Place_Only_When_It_Has_A_Position()
    {
        var places = new Mock<IPlaceNameResolver>();
        places.Setup(p => p.Describe(35.2, -80.8)).Returns("Charlotte, North Carolina, US");
        var mapper = new PhotoDtoMapper(places.Object);
        var located = TestPhotos.Make("a.jpg"); located.Latitude = 35.2; located.Longitude = -80.8;
        var half = TestPhotos.Make("b.jpg"); half.Latitude = 35.2;
        var other = TestPhotos.Make("c.jpg"); other.Longitude = -80.8;
        var video = TestPhotos.Video("clip.MOV"); video.DurationSeconds = 12.5;

        await Assert.That(mapper.Map(located).PlaceName).IsEqualTo("Charlotte, North Carolina, US");
        await Assert.That(mapper.Map(half).PlaceName).IsNull();
        await Assert.That(mapper.Map(other).PlaceName).IsNull();
        await Assert.That(mapper.Map(TestPhotos.Make("d.jpg")).PlaceName).IsNull();
        await Assert.That((mapper.Map(video).IsVideo, mapper.Map(video).DurationSeconds, mapper.Map(video).Format)).IsEqualTo((true, 12.5, "MOV"));
        await Assert.That(mapper.Map(new PhotoSense.Domain.Entities.Photo { SourcePath = "a.jpg", FileName = "a.jpg" }).Folder).IsEqualTo(string.Empty);
        await Assert.That(mapper.Map(new PhotoSense.Domain.Entities.Photo { SourcePath = "", FileName = "a.jpg" }).Folder).IsEqualTo(string.Empty);
    }
}
