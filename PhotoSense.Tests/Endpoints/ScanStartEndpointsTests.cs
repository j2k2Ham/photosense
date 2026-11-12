using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using PhotoSense.Application.Scanning;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Services;

namespace PhotoSense.Tests.Endpoints;

/// <summary>Starting a scan: which folders, one at a time, and what stops one.</summary>
public sealed class ScanStartEndpointsTests : IDisposable
{
    private static readonly PhotoStorageOptions Defaults = new() { PrimaryPath = "configured-primary", SecondaryPath = "configured-secondary" };
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("photosense-scanstart-");
    private readonly InMemoryScanProgressStore _progress = new(new TestClock());
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
        /// <summary>The scans started, in order.</summary>
        public List<(ScanRequest Request, string InstanceId)> Started { get; } = [];
        /// <summary>Lets the scans running finish.</summary>
        public TaskCompletionSource Finish { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>What a scan throws when it finishes, if anything.</summary>
        public Exception? Failure { get; set; }

        /// <inheritdoc />
        public async Task<ScanSummary> RunAsync(ScanRequest request, string instanceId, CancellationToken ct)
        {
            lock (Started) Started.Add((request, instanceId));
            try
            {
                if (Failure is not null) throw Failure;
                await Finish.Task.WaitAsync(ct);
                return new ScanSummary(0, 0, 0, 0, 0);
            }
            finally { progress.ScanCompleted(instanceId); }
        }
    }

    // The scanning addresses, starting scans through the test's runner, with the folders configured as given.
    private ScanApi Starter(PhotoStorageOptions? defaults = null, IPhotoFileSystem? files = null)
        => new(new TestApi(s => s.AddSingleton(Options.Create(defaults ?? new PhotoStorageOptions())).AddSingleton(_runner).AddSingleton(files ?? TestFiles.Disk)));

    // What a scan asked for with this body scans, on a computer where every folder named is there.
    private async Task<ScanRequest> AskedForAsync(string body, PhotoStorageOptions defaults, CancellationToken ct)
    {
        var everywhere = new Mock<IPhotoFileSystem>();
        everywhere.Setup(f => f.FolderExists(It.IsAny<string>())).Returns(true);
        await Starter(defaults, everywhere.Object).StartAsync(Http.Post("scan/start", body).FromClient(), ct);
        _scans.Finish.SetResult();
        await _runner.Running;
        return _scans.Started.Single().Request;
    }

    private static string Body(string? primary, string? secondary = null)
        => System.Text.Json.JsonSerializer.Serialize(new { primaryLocation = primary, secondaryLocation = secondary, recursive = false });

    // ---- what is asked for

    [Test]
    public async Task Reads_The_Fields_The_Web_Client_Sends(CancellationToken ct)
    {
        var request = await AskedForAsync("""{"primaryLocation":" C:\\Photos ","secondaryLocation":"D:\\Backup","recursive":false}""", Defaults, ct);
        await Assert.That(request).IsEqualTo(new ScanRequest("C:\\Photos", "D:\\Backup", false));
    }

    [Test]
    public async Task Still_Reads_The_Shorter_Field_Names(CancellationToken ct)
    {
        var request = await AskedForAsync("""{"primary":"C:\\Photos","secondary":"D:\\Backup"}""", Defaults, ct);
        await Assert.That(request).IsEqualTo(new ScanRequest("C:\\Photos", "D:\\Backup", true));
    }

    [Test]
    [Arguments("")]
    [Arguments("not json")]
    [Arguments("[]")]
    [Arguments("""{"primaryLocation":"  ","secondaryLocation":null,"recursive":"yes"}""")]
    public async Task Falls_Back_To_The_Configured_Folders(string body, CancellationToken ct)
        => await Assert.That(await AskedForAsync(body, Defaults, ct)).IsEqualTo(new ScanRequest("configured-primary", "configured-secondary", true));

    [Test]
    [Arguments("""{"primaryLocation":"C:\\Photos","startOver":true}""", true)]
    [Arguments("""{"primaryLocation":"C:\\Photos","startOver":false}""", false)]
    [Arguments("""{"primaryLocation":"C:\\Photos"}""", false)]                      // not asked for: earlier results are built on
    [Arguments("""{"primaryLocation":"C:\\Photos","startOver":"yes"}""", false)]     // only a plain true forgets anything
    [Arguments("not json", false)]
    public async Task A_Scan_Starts_Over_Only_When_Plainly_Asked_To(string body, bool startOver, CancellationToken ct)
        => await Assert.That((await AskedForAsync(body, Defaults, ct)).StartOver).IsEqualTo(startOver);

    [Test]
    public async Task No_Secondary_Folder_Anywhere_Means_None(CancellationToken ct)
    {
        var request = await AskedForAsync("""{"primaryLocation":"C:\\Photos"}""", new PhotoStorageOptions(), ct);
        await Assert.That(request).IsEqualTo(new ScanRequest("C:\\Photos", null, true));
    }

    [Test]
    public async Task A_Scan_Is_Running_From_Its_Start_Until_It_Completes(CancellationToken ct)
    {
        var progress = new InMemoryScanProgressStore(new TestClock());
        await Assert.That(ScanRunner.IsRunning(progress.GetLatest())).IsFalse();
        progress.ScanStarted("a");
        await Assert.That(ScanRunner.IsRunning(progress.GetLatest())).IsTrue();
        progress.ScanCompleted("a");
        await Assert.That(ScanRunner.IsRunning(progress.GetLatest())).IsFalse();
    }

    // ---- starting one

    [Test]
    public async Task Starting_A_Scan_Runs_It_In_The_Background_And_Marks_It_Running_Until_It_Is_Done(CancellationToken ct)
    {
        var backup = _root.CreateSubdirectory("backup").FullName;

        var response = await Starter().StartAsync(Http.Post("scan/start", Body(_root.FullName, backup)).FromClient(), ct);

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
    public async Task A_Second_Scan_Is_Refused_While_One_Runs(CancellationToken ct)
    {
        var starter = Starter();
        await starter.StartAsync(Http.Post("scan/start", Body(_root.FullName)).FromClient(), ct);

        var second = await starter.StartAsync(Http.Post("scan/start", Body(_root.FullName)).FromClient(), ct);

        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(second.Json().GetProperty("error").GetString()).Contains("already running");
        _scans.Finish.SetResult();
        await _runner.Running;
        await Assert.That(_scans.Started.Count).IsEqualTo(1);

        _scans.Finish = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await Assert.That((await starter.StartAsync(Http.Post("scan/start", Body(_root.FullName)).FromClient(), ct)).StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        _scans.Finish.SetResult();
        await _runner.Running;
    }

    [Test]
    public async Task A_Scan_That_Fails_Says_Why_In_Its_Log_And_A_New_One_Can_Start(CancellationToken ct)
    {
        _scans.Failure = new IOException("The disk went away");
        var starter = Starter();

        var instanceId = (await starter.StartAsync(Http.Post("scan/start", Body(_root.FullName)).FromClient(), ct)).Json().GetProperty("instanceId").GetString()!;
        await _runner.Running;

        _log.Verify(l => l.Log(instanceId, "Error", "The scan stopped: The disk went away"), Times.Once);
        await Assert.That(ScanRunner.IsRunning(_progress.GetLatest())).IsFalse();
        _scans.Failure = null;
        _scans.Finish.SetResult();
        await Assert.That((await starter.StartAsync(Http.Post("scan/start", Body(_root.FullName)).FromClient(), ct)).StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await _runner.Running;
    }

    // ---- stopping one

    [Test]
    public async Task Stop_Ends_The_Scan_Running_Keeping_What_It_Read_And_Another_Can_Start(CancellationToken ct)
    {
        var scans = Starter();
        var instanceId = (await scans.StartAsync(Http.Post("scan/start", Body(_root.FullName)).FromClient(), ct)).Json().GetProperty("instanceId").GetString()!;

        var stopped = await scans.StopAsync(Http.Post($"scan/stop/{instanceId}").FromClient(), instanceId, ct);
        await _runner.Running;

        await Assert.That(stopped.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(ScanRunner.IsRunning(_progress.GetLatest())).IsFalse();
        _log.Verify(l => l.Log(instanceId, "Info", "Scan stopped. What was read so far is kept."), Times.Once);
        _log.Verify(l => l.Log(instanceId, "Error", It.IsAny<string>()), Times.Never);
        await Assert.That((await scans.StartAsync(Http.Post("scan/start", Body(_root.FullName)).FromClient(), ct)).StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        _scans.Finish.SetResult();
        await _runner.Running;
    }

    [Test]
    public async Task Stop_Answers_Not_Found_For_A_Scan_That_Is_Not_Running(CancellationToken ct)
    {
        var scans = Starter();
        var instanceId = (await scans.StartAsync(Http.Post("scan/start", Body(_root.FullName)).FromClient(), ct)).Json().GetProperty("instanceId").GetString()!;

        var other = await scans.StopAsync(Http.Post("scan/stop/other").FromClient(), "other", ct);
        _scans.Finish.SetResult();
        await _runner.Running;
        var finished = await scans.StopAsync(Http.Post($"scan/stop/{instanceId}").FromClient(), instanceId, ct);

        foreach (var answer in new[] { other, finished })
            await Assert.That((answer.StatusCode, answer.Json().GetProperty("error").GetString())).IsEqualTo((HttpStatusCode.NotFound, "That scan is not running."));
        _log.Verify(l => l.Log(It.IsAny<string>(), "Info", It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task Only_The_Page_Can_Stop_A_Scan(CancellationToken ct)
    {
        var scans = Starter();
        var instanceId = (await scans.StartAsync(Http.Post("scan/start", Body(_root.FullName)).FromClient(), ct)).Json().GetProperty("instanceId").GetString()!;

        var refused = await scans.StopAsync(Http.Post($"scan/stop/{instanceId}"), instanceId, ct);

        await Assert.That(refused.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(ScanRunner.IsRunning(_progress.GetLatest())).IsTrue();
        _scans.Finish.SetResult();
        await _runner.Running;
    }

    [Test]
    public async Task A_Folder_The_Server_Cannot_Find_Is_Reported_Instead_Of_Scanned(CancellationToken ct)
    {
        var starter = Starter();
        var missing = Path.Combine(_root.FullName, "no-such-folder");

        var noPrimary = await starter.StartAsync(Http.Post("scan/start", "{}").FromClient(), ct);
        var badPrimary = await starter.StartAsync(Http.Post("scan/start", Body("Phone Pictures")).FromClient(), ct);
        var badSecondary = await starter.StartAsync(Http.Post("scan/start", Body(_root.FullName, missing)).FromClient(), ct);

        await Assert.That((noPrimary.StatusCode, noPrimary.Json().GetProperty("error").GetString())).IsEqualTo((HttpStatusCode.BadRequest, "A folder to scan is required."));
        await Assert.That((badPrimary.StatusCode, badPrimary.Json().GetProperty("error").GetString())).IsEqualTo((HttpStatusCode.BadRequest, "Folder not found on the server: Phone Pictures"));
        await Assert.That((badSecondary.StatusCode, badSecondary.Json().GetProperty("error").GetString())).IsEqualTo((HttpStatusCode.BadRequest, $"Folder not found on the server: {missing}"));
        await Assert.That(_scans.Started).IsEmpty();
        await Assert.That(ScanRunner.IsRunning(_progress.GetLatest())).IsFalse();
    }

    [Test]
    public async Task Scans_And_Their_Groups_Answer_Only_The_UI(CancellationToken ct)
    {
        var groups = Starter();

        await Assert.That((await Starter().StartAsync(Http.Post("scan/start", Body(_root.FullName)), ct)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await groups.GetDuplicateGroupsAsync(Http.Get("scan/groups"), ct)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

        await Assert.That(_scans.Started).IsEmpty();
        await Assert.That(ScanRunner.IsRunning(_progress.GetLatest())).IsFalse();
    }

    [Test]
    public async Task With_No_Folder_In_The_Request_The_Configured_One_Is_Scanned(CancellationToken ct)
    {
        var response = await Starter(new PhotoStorageOptions { PrimaryPath = _root.FullName }).StartAsync(Http.Post("scan/start", "").FromClient(), ct);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        _scans.Finish.SetResult();
        await _runner.Running;
        await Assert.That(_scans.Started.Single().Request).IsEqualTo(new ScanRequest(_root.FullName, null, true, false));
    }
}
