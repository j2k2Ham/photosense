using System.Net;
using Microsoft.Extensions.Options;
using Moq;
using PhotoSense.Api.Endpoints;
using PhotoSense.Api.Scanning;
using PhotoSense.Application.Scanning;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Services;

namespace PhotoSense.Tests.Endpoints;

public sealed class ScanStartEndpointsTests : IDisposable
{
    private static readonly PhotoStorageOptions Defaults = new() { PrimaryPath = "configured-primary", SecondaryPath = "configured-secondary" };
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("photosense-scanstart-");
    private readonly InMemoryScanProgressStore _progress = new();
    private readonly Mock<IScanLogSink> _log = new();
    private readonly HeldScans _scans;
    private readonly ScanRunner _runner;

    public ScanStartEndpointsTests()
    {
        _scans = new HeldScans(_progress);
        _runner = new ScanRunner(_scans, _progress, _log.Object);
    }

    public void Dispose() => TestFiles.Remove(_root);

    /// <summary>A scan that runs until the test lets it finish, and marks itself finished then, as a real one does.</summary>
    private sealed class HeldScans(IScanProgressStore progress) : IScanExecutionService
    {
        public List<(ScanRequest Request, string InstanceId)> Started { get; } = [];
        public TaskCompletionSource Finish { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception? Failure { get; set; }

        public async Task<ScanSummary> RunAsync(ScanRequest request, string instanceId, CancellationToken ct = default)
        {
            lock (Started) Started.Add((request, instanceId));
            try
            {
                if (Failure is not null) throw Failure;
                await Finish.Task;
                return new ScanSummary(0, 0, 0, 0, 0);
            }
            finally { progress.ScanCompleted(instanceId); }
        }
    }

    private ScanStartEndpoints Starter(PhotoStorageOptions? defaults = null) => new(Options.Create(defaults ?? new PhotoStorageOptions()), _runner, TestFiles.Disk);

    private static string Body(string? primary, string? secondary = null)
        => System.Text.Json.JsonSerializer.Serialize(new { primaryLocation = primary, secondaryLocation = secondary, recursive = false });

    // ---- what is asked for

    [Test]
    public async Task Reads_The_Fields_The_Web_Client_Sends()
    {
        var request = ScanStartEndpoints.ParseBody("""{"primaryLocation":" C:\\Photos ","secondaryLocation":"D:\\Backup","recursive":false}""", Defaults);
        await Assert.That(request).IsEqualTo(new ScanRequest("C:\\Photos", "D:\\Backup", false));
    }

    [Test]
    public async Task Still_Reads_The_Shorter_Field_Names()
    {
        var request = ScanStartEndpoints.ParseBody("""{"primary":"C:\\Photos","secondary":"D:\\Backup"}""", Defaults);
        await Assert.That(request).IsEqualTo(new ScanRequest("C:\\Photos", "D:\\Backup", true));
    }

    [Test]
    [Arguments("")]
    [Arguments("not json")]
    [Arguments("[]")]
    [Arguments("""{"primaryLocation":"  ","secondaryLocation":null,"recursive":"yes"}""")]
    public async Task Falls_Back_To_The_Configured_Folders(string body)
        => await Assert.That(ScanStartEndpoints.ParseBody(body, Defaults)).IsEqualTo(new ScanRequest("configured-primary", "configured-secondary", true));

    [Test]
    [Arguments("""{"primaryLocation":"C:\\Photos","startOver":true}""", true)]
    [Arguments("""{"primaryLocation":"C:\\Photos","startOver":false}""", false)]
    [Arguments("""{"primaryLocation":"C:\\Photos"}""", false)]                      // not asked for: earlier results are built on
    [Arguments("""{"primaryLocation":"C:\\Photos","startOver":"yes"}""", false)]     // only a plain true forgets anything
    [Arguments("not json", false)]
    public async Task A_Scan_Starts_Over_Only_When_Plainly_Asked_To(string body, bool startOver)
        => await Assert.That(ScanStartEndpoints.ParseBody(body, Defaults).StartOver).IsEqualTo(startOver);

    [Test]
    public async Task No_Secondary_Folder_Anywhere_Means_None()
    {
        var request = ScanStartEndpoints.ParseBody("""{"primaryLocation":"C:\\Photos"}""", new PhotoStorageOptions());
        await Assert.That(request).IsEqualTo(new ScanRequest("C:\\Photos", null, true));
    }

    [Test]
    public async Task A_Scan_Is_Running_From_Its_Start_Until_It_Completes()
    {
        var progress = new InMemoryScanProgressStore();
        await Assert.That(ScanRunner.IsRunning(progress.GetLatest())).IsFalse();
        progress.ScanStarted("a");
        await Assert.That(ScanRunner.IsRunning(progress.GetLatest())).IsTrue();
        progress.ScanCompleted("a");
        await Assert.That(ScanRunner.IsRunning(progress.GetLatest())).IsFalse();
    }

    // ---- starting one

    [Test]
    public async Task Starting_A_Scan_Runs_It_In_The_Background_And_Marks_It_Running_Until_It_Is_Done()
    {
        var backup = _root.CreateSubdirectory("backup").FullName;

        var response = await Starter().StartAsync(Http.Post("scan/start", Body(_root.FullName, backup)).FromClient());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        var instanceId = response.Json().GetProperty("instanceId").GetString()!;
        await Assert.That(instanceId).Matches("^[0-9a-f]{32}$");
        await Assert.That(ScanRunner.IsRunning(_progress.GetLatest())).IsTrue();
        await Assert.That(_progress.GetLatest().InstanceId).IsEqualTo(instanceId);

        _scans.Finish.SetResult();
        await _runner.Running;

        await Assert.That(_scans.Started.Single()).IsEqualTo((new ScanRequest(_root.FullName, backup, false, false), instanceId));
        await Assert.That(ScanRunner.IsRunning(_progress.GetLatest())).IsFalse();
    }

    [Test]
    public async Task A_Second_Scan_Is_Refused_While_One_Runs()
    {
        var starter = Starter();
        await starter.StartAsync(Http.Post("scan/start", Body(_root.FullName)).FromClient());

        var second = await starter.StartAsync(Http.Post("scan/start", Body(_root.FullName)).FromClient());

        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(second.Json().GetProperty("error").GetString()).Contains("already running");
        _scans.Finish.SetResult();
        await _runner.Running;
        await Assert.That(_scans.Started.Count).IsEqualTo(1);

        _scans.Finish = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await Assert.That((await starter.StartAsync(Http.Post("scan/start", Body(_root.FullName)).FromClient())).StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        _scans.Finish.SetResult();
        await _runner.Running;
    }

    [Test]
    public async Task A_Scan_That_Fails_Says_Why_In_Its_Log_And_A_New_One_Can_Start()
    {
        _scans.Failure = new IOException("The disk went away");
        var starter = Starter();

        var instanceId = (await starter.StartAsync(Http.Post("scan/start", Body(_root.FullName)).FromClient())).Json().GetProperty("instanceId").GetString()!;
        await _runner.Running;

        _log.Verify(l => l.Log(instanceId, "Error", "The scan stopped: The disk went away"), Times.Once);
        await Assert.That(ScanRunner.IsRunning(_progress.GetLatest())).IsFalse();
        _scans.Failure = null;
        _scans.Finish.SetResult();
        await Assert.That((await starter.StartAsync(Http.Post("scan/start", Body(_root.FullName)).FromClient())).StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await _runner.Running;
    }

    [Test]
    public async Task A_Folder_The_Server_Cannot_Find_Is_Reported_Instead_Of_Scanned()
    {
        var starter = Starter();
        var missing = Path.Combine(_root.FullName, "no-such-folder");

        var noPrimary = await starter.StartAsync(Http.Post("scan/start", "{}").FromClient());
        var badPrimary = await starter.StartAsync(Http.Post("scan/start", Body("Phone Pictures")).FromClient());
        var badSecondary = await starter.StartAsync(Http.Post("scan/start", Body(_root.FullName, missing)).FromClient());

        await Assert.That((noPrimary.StatusCode, noPrimary.Json().GetProperty("error").GetString())).IsEqualTo((HttpStatusCode.BadRequest, "A folder to scan is required."));
        await Assert.That((badPrimary.StatusCode, badPrimary.Json().GetProperty("error").GetString())).IsEqualTo((HttpStatusCode.BadRequest, "Folder not found on the server: Phone Pictures"));
        await Assert.That((badSecondary.StatusCode, badSecondary.Json().GetProperty("error").GetString())).IsEqualTo((HttpStatusCode.BadRequest, $"Folder not found on the server: {missing}"));
        await Assert.That(_scans.Started).IsEmpty();
        await Assert.That(ScanRunner.IsRunning(_progress.GetLatest())).IsFalse();
    }

    [Test]
    public async Task Scans_And_Their_Groups_Answer_Only_The_UI()
    {
        var groups = new DuplicateGroupsEndpoints(new ScanGroupingFacade(Mock.Of<IDuplicateAnalysisService>(), new PhotoDtoMapper(Mock.Of<IPlaceNameResolver>()), new PhotoRanking()));

        await Assert.That((await Starter().StartAsync(Http.Post("scan/start", Body(_root.FullName)))).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await groups.GetDuplicateGroups(Http.Get("scan/groups"))).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

        await Assert.That(_scans.Started).IsEmpty();
        await Assert.That(ScanRunner.IsRunning(_progress.GetLatest())).IsFalse();
    }

    [Test]
    public async Task With_No_Folder_In_The_Request_The_Configured_One_Is_Scanned()
    {
        var response = await Starter(new PhotoStorageOptions { PrimaryPath = _root.FullName }).StartAsync(Http.Post("scan/start", "").FromClient());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        _scans.Finish.SetResult();
        await _runner.Running;
        await Assert.That(_scans.Started.Single().Request).IsEqualTo(new ScanRequest(_root.FullName, null, true, false));
    }
}
