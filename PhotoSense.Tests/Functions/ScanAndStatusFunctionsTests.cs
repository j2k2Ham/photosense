using System.Net;
using LiteDB;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using PhotoSense.Application.Photos.Interfaces;
using PhotoSense.Application.Scanning;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.DTOs;
using PhotoSense.Domain.Events;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Functions;
using PhotoSense.Functions.Api;
using PhotoSense.Functions.Scanning;
using PhotoSense.Infrastructure.Persistence;

namespace PhotoSense.Tests.Functions;

public sealed class ScanAndStatusFunctionsTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("photosense-scanapi-");
    private readonly InMemoryScanProgressStore _progress = new();

    public void Dispose() => TestFiles.Remove(_root);

    // ---- forgetting earlier results

    private (ScanResetFunction Reset, InMemoryPhotoRepository Repo, InMemoryThumbnailStore Thumbnails) Resetter()
    {
        var repo = new InMemoryPhotoRepository();
        var thumbnails = new InMemoryThumbnailStore();
        return (new ScanResetFunction(repo, thumbnails, _progress), repo, thumbnails);
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

    // ---- starting a scan

    private (ScanHttpStarter Starter, Mock<DurableTaskClient> Client) Starter(PhotoStorageOptions? defaults = null)
    {
        var client = new Mock<DurableTaskClient>("test") { CallBase = true };
        client.Setup(c => c.ScheduleNewOrchestrationInstanceAsync(It.IsAny<TaskName>(), It.IsAny<object?>(), It.IsAny<StartOrchestrationOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("instance-1");
        return (new ScanHttpStarter(Options.Create(defaults ?? new PhotoStorageOptions()), _progress), client);
    }

    private string Body(string? primary, string? secondary = null)
        => System.Text.Json.JsonSerializer.Serialize(new { primaryLocation = primary, secondaryLocation = secondary, recursive = false });

    [Test]
    public async Task Starting_A_Scan_Schedules_It_And_Marks_It_Running()
    {
        var (starter, client) = Starter();
        var backup = _root.CreateSubdirectory("backup").FullName;

        var response = await starter.StartAsync(Http.Post("scan/start", Body(_root.FullName, backup)), client.Object);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(response.Json().GetProperty("instanceId").GetString()).IsEqualTo("instance-1");
        client.Verify(c => c.ScheduleNewOrchestrationInstanceAsync(nameof(ScanOrchestrator.RunScanAsync), new ScanRequest(_root.FullName, backup, false, false),
            It.IsAny<StartOrchestrationOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        await Assert.That(ScanHttpStarter.IsRunning(_progress.GetLatest())).IsTrue();
    }

    [Test]
    public async Task A_Second_Scan_Is_Refused_While_One_Runs()
    {
        var (starter, client) = Starter();
        await starter.StartAsync(Http.Post("scan/start", Body(_root.FullName)), client.Object);

        var second = await starter.StartAsync(Http.Post("scan/start", Body(_root.FullName)), client.Object);

        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(second.Json().GetProperty("error").GetString()).Contains("already running");
        client.Verify(c => c.ScheduleNewOrchestrationInstanceAsync(It.IsAny<TaskName>(), It.IsAny<object?>(), It.IsAny<StartOrchestrationOptions?>(), It.IsAny<CancellationToken>()), Times.Once);

        _progress.ScanCompleted("instance-1");
        await Assert.That((await starter.StartAsync(Http.Post("scan/start", Body(_root.FullName)), client.Object)).StatusCode).IsEqualTo(HttpStatusCode.Accepted);
    }

    [Test]
    public async Task A_Folder_The_Server_Cannot_Find_Is_Reported_Instead_Of_Scanned()
    {
        var (starter, client) = Starter();
        var missing = Path.Combine(_root.FullName, "no-such-folder");

        var noPrimary = await starter.StartAsync(Http.Post("scan/start", "{}"), client.Object);
        var badPrimary = await starter.StartAsync(Http.Post("scan/start", Body("Phone Pictures")), client.Object);
        var badSecondary = await starter.StartAsync(Http.Post("scan/start", Body(_root.FullName, missing)), client.Object);

        await Assert.That((noPrimary.StatusCode, noPrimary.Json().GetProperty("error").GetString())).IsEqualTo((HttpStatusCode.BadRequest, "A folder to scan is required."));
        await Assert.That((badPrimary.StatusCode, badPrimary.Json().GetProperty("error").GetString())).IsEqualTo((HttpStatusCode.BadRequest, "Folder not found on the server: Phone Pictures"));
        await Assert.That((badSecondary.StatusCode, badSecondary.Json().GetProperty("error").GetString())).IsEqualTo((HttpStatusCode.BadRequest, $"Folder not found on the server: {missing}"));
        client.Verify(c => c.ScheduleNewOrchestrationInstanceAsync(It.IsAny<TaskName>(), It.IsAny<object?>(), It.IsAny<StartOrchestrationOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        await Assert.That(ScanHttpStarter.IsRunning(_progress.GetLatest())).IsFalse();
    }

    [Test]
    public async Task With_No_Folder_In_The_Request_The_Configured_One_Is_Scanned()
    {
        var (starter, client) = Starter(new PhotoStorageOptions { PrimaryPath = _root.FullName });
        await Assert.That((await starter.StartAsync(Http.Post("scan/start", ""), client.Object)).StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        client.Verify(c => c.ScheduleNewOrchestrationInstanceAsync(It.IsAny<TaskName>(), new ScanRequest(_root.FullName, null, true, false),
            It.IsAny<StartOrchestrationOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- the orchestration

    [Test]
    public async Task The_Orchestration_Runs_The_Whole_Scan_As_One_Activity()
    {
        var request = new ScanRequest(_root.FullName, null, true);
        var summary = new ScanSummary(3, 3, 0, 0, 0);
        var exec = new Mock<IScanExecutionService>();
        exec.Setup(e => e.RunAsync(request, "instance-9", It.IsAny<CancellationToken>())).ReturnsAsync(summary);
        var orchestrator = new ScanOrchestrator(exec.Object);

        var context = new Mock<TaskOrchestrationContext>();
        context.Setup(c => c.GetInput<ScanRequest>()).Returns(request);
        context.SetupGet(c => c.InstanceId).Returns("instance-9");
        context.Setup(c => c.CallActivityAsync<ScanSummary>(It.IsAny<TaskName>(), It.IsAny<object?>(), It.IsAny<TaskOptions?>())).ReturnsAsync(summary);

        await orchestrator.RunScanAsync(context.Object);
        context.Verify(c => c.CallActivityAsync<ScanSummary>(nameof(ScanOrchestrator.RunScanActivity), new RunScanInput(request, "instance-9"), It.IsAny<TaskOptions?>()), Times.Once);

        await Assert.That(await orchestrator.RunScanActivity(new RunScanInput(request, "instance-9"))).IsSameReferenceAs(summary);

        // An orchestration started with nothing to scan does nothing.
        var empty = new Mock<TaskOrchestrationContext>();
        empty.Setup(c => c.GetInput<ScanRequest>()).Returns((ScanRequest?)null);
        await orchestrator.RunScanAsync(empty.Object);
        empty.Verify(c => c.CallActivityAsync<ScanSummary>(It.IsAny<TaskName>(), It.IsAny<object?>(), It.IsAny<TaskOptions?>()), Times.Never);
    }

    // ---- progress and status

    [Test]
    public async Task Progress_Is_Reported_For_A_Scan_That_Was_Started_And_Not_For_An_Unknown_One()
    {
        _progress.ScanStarted("scan-1");
        _progress.SetTotals("scan-1", 4, 2);
        _progress.IncrementProcessed("scan-1", primary: true);
        _progress.IncrementProcessed("scan-1", primary: false);
        var function = new ScanProgressByInstanceFunction(_progress);

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
    public async Task Status_Describes_The_Latest_Scan_And_Counts_The_Photos()
    {
        var repo = new InMemoryPhotoRepository();
        await repo.AddOrUpdateAsync(TestPhotos.Make("a.jpg"));
        _progress.ScanStarted("scan-1");
        _progress.SetTotals("scan-1", 2, 0);
        _progress.ScanCompleted("scan-1");

        var json = (await new ScanStatusFunctions(repo, _progress).GetStatus(Http.Get("scan/status"))).Json();

        await Assert.That((json.GetProperty("instanceId").GetString(), json.GetProperty("primaryTotal").GetInt32(), json.GetProperty("totalPhotos").GetInt32())).IsEqualTo(("scan-1", 2, 1));
        await Assert.That(json.GetProperty("completed").ValueKind).IsNotEqualTo(System.Text.Json.JsonValueKind.Null);
    }

    [Test]
    [Arguments(null, null, false, false)]
    [Arguments("C:/photos", "D:/backup", true, true)]
    public async Task Health_Says_Which_Folders_Are_Configured(string? primary, string? secondary, bool hasPrimary, bool hasSecondary)
    {
        var options = Options.Create(new PhotoStorageOptions { PrimaryPath = primary ?? "", SecondaryPath = secondary ?? "" });
        var json = (await new HealthFunctions(options).GetAsync(Http.Get("health"))).Json();
        await Assert.That((json.GetProperty("status").GetString(), json.GetProperty("storagePrimaryConfigured").GetBoolean(), json.GetProperty("storageSecondaryConfigured").GetBoolean())).IsEqualTo(("ok", hasPrimary, hasSecondary));
    }

    [Test]
    public async Task Dispatching_The_Outbox_Marks_Every_Pending_Message_Processed()
    {
        var first = new OutboxMessage { Type = "a", Payload = "{}" };
        var second = new OutboxMessage { Type = "b", Payload = "{}" };
        var store = new Mock<IOutboxStore>();
        store.Setup(s => s.GetUnprocessedAsync(100, It.IsAny<CancellationToken>())).ReturnsAsync([first, second]);

        var response = await new OutboxDispatchFunctions(store.Object).RunManualAsync(Http.Post("outbox/dispatch"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        store.Verify(s => s.MarkProcessedAsync(first.Id, It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.MarkProcessedAsync(second.Id, It.IsAny<CancellationToken>()), Times.Once);
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
        var function = new DuplicateGroupsFunctions(new ScanGroupingFacade(analysis.Object, new PhotoDtoMapper(Mock.Of<IPlaceNameResolver>()), new PhotoRanking()));

        var json = (await function.GetDuplicateGroups(Http.Get(url))).Json();

        await Assert.That((json.GetProperty("mode").GetString(), json.GetProperty("page").GetInt32(), json.GetProperty("pageSize").GetInt32())).IsEqualTo((similar ? "similar" : "duplicates", page, pageSize));
        _ = (text, hideKept); // both only filter, and there is nothing here to filter
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

    // ---- the service registrations

    [Test]
    public async Task Every_Service_The_Functions_Need_Can_Be_Built()
    {
        var settings = new Dictionary<string, string?>
        {
            ["PhotoStorage:DatabasePath"] = Path.Combine(_root.FullName, "not-there-yet", "photosense.db"),
            ["PhotoStorage:KeepFormat"] = "WidelyCompatible"
        };
        using var host = new HostBuilder()
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(settings))
            .ConfigureServices((ctx, s) => s.AddPhotoSenseCore(ctx.Configuration))
            .Build();
        var services = host.Services;

        foreach (var type in new[]
        {
            typeof(LiteDatabase), typeof(IPhotoRepository), typeof(IAuditRepository), typeof(IScanHistory), typeof(IImageHashingService), typeof(IImageAnalyzer), typeof(IThumbnailStore),
            typeof(IPhotoMetadataExtractor), typeof(PhotoRanking), typeof(IDuplicateAnalysisService), typeof(IDuplicateRemovalService), typeof(IPlaceNameResolver),
            typeof(PhotoDtoMapper), typeof(ScanGroupingFacade), typeof(IScanRequestPublisher), typeof(IOutboxStore), typeof(IIntegrationEventPublisher),
            typeof(ICompanionFileFinder), typeof(ISystemViewer), typeof(IFolderBrowser), typeof(IPhotoDeletionService), typeof(IPhotoSearchService),
            typeof(IScanProgressStore), typeof(IScanLogSink), typeof(IScanExecutionService), typeof(IValidateOptions<PhotoStorageOptions>)
        })
            await Assert.That(services.GetRequiredService(type)).IsNotNull();

        // The configured preference reaches the ranking: the JPEG is the keeper.
        var ranking = services.GetRequiredService<PhotoRanking>();
        var heic = TestPhotos.Make("a.HEIC");
        var jpeg = TestPhotos.Make("a.JPG", format: "JPEG");
        await Assert.That(new[] { heic, jpeg }.OrderBy(p => p, ranking.BestFirst).First()).IsSameReferenceAs(jpeg);
        await Assert.That(services.GetRequiredService<IOptions<PhotoStorageOptions>>().Value.ResolveThumbnailPath()).StartsWith(_root.FullName);
        // The folder for the database is made on first use.
        await Assert.That(File.Exists(Path.Combine(_root.FullName, "not-there-yet", "photosense.db"))).IsTrue();
        await host.StopAsync();
    }
}
