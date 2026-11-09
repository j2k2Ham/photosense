using System.Net;
using PhotoSense.Api.Scanning;
using PhotoSense.Application.Scanning;

namespace PhotoSense.Tests.Endpoints;

// These classes log through one process-wide queue, so they must not run side by side.
/// <summary>A scan's log lines, handed over once each, at a measured pace.</summary>
[NotInParallel("ScanLogQueue")]
public class ScanLogsEndpointsTests
{
    private readonly InMemoryScanLogSink _sink = new();

    public ScanLogsEndpointsTests() => DrainQueue();

    private static void DrainQueue()
    {
        while (InMemoryScanLogSink.TryDequeuePending(out _)) { }
    }

    private static ScanLogsEndpoints Logs(TimeProvider? time = null) => new(time);

    // ---- what the page polls for

    [Test]
    [Arguments("scan/logs", 30)]              // default limit is 200
    [Arguments("scan/logs?limit=5", 5)]
    [Arguments("scan/logs?limit=0", 30)]      // not a usable limit: default
    [Arguments("scan/logs?limit=-3", 30)]
    [Arguments("scan/logs?limit=abc", 30)]
    [Arguments("scan/logs?limit=999999", 30)] // capped at 1000, which is still more than there is
    public async Task Polling_Hands_Out_Queued_Lines_Up_To_The_Limit(string url, int expected, CancellationToken ct)
    {
        for (int i = 0; i < 30; i++) _sink.Log("scan-1", "Info", $"line {i}");

        var json = (await Logs().GetPendingAsync(Http.Get(url).FromClient(), ct)).Json();

        await Assert.That(json.GetProperty("count").GetInt32()).IsEqualTo(expected);
        var first = json.GetProperty("items")[0];
        await Assert.That((first.GetProperty("instanceId").GetString(), first.GetProperty("level").GetString(), first.GetProperty("message").GetString())).IsEqualTo(("scan-1", "Info", "line 0"));
        DrainQueue();
    }

    [Test]
    public async Task Polling_More_Than_A_Thousand_Lines_At_Once_Is_Capped(CancellationToken ct)
    {
        for (int i = 0; i < 1005; i++) _sink.Log("scan-1", "Info", "x");
        var logs = Logs();
        await Assert.That((await logs.GetPendingAsync(Http.Get("scan/logs?limit=5000").FromClient(), ct)).Json().GetProperty("count").GetInt32()).IsEqualTo(1000);
        await Assert.That((await logs.GetPendingAsync(Http.Get("scan/logs").FromClient(), ct)).Json().GetProperty("count").GetInt32()).IsEqualTo(5);
    }

    [Test]
    public async Task Polling_Too_Often_Is_Turned_Away_Until_The_Allowance_Refills_By_The_Services_Clock(CancellationToken ct)
    {
        var clock = new TestClock();
        var logs = Logs(time: clock);
        for (var i = 0; i < 20; i++)
            await Assert.That((await logs.GetPendingAsync(Http.Get("scan/logs").FromClient(), ct)).StatusCode).IsEqualTo(HttpStatusCode.OK);

        var refused = await logs.GetPendingAsync(Http.Get("scan/logs").FromClient(), ct);
        await Assert.That(refused.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(refused.Text()).Contains("Rate limit");

        // A second later there is allowance for one more, and only one.
        clock.Now += TimeSpan.FromSeconds(1);
        await Assert.That((await logs.GetPendingAsync(Http.Get("scan/logs").FromClient(), ct)).StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await logs.GetPendingAsync(Http.Get("scan/logs").FromClient(), ct)).StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
    }

    [Test]
    public async Task The_Log_Is_Shown_Only_To_The_UI_And_Nothing_Is_Taken_Off_The_Queue_For_Anyone_Else(CancellationToken ct)
    {
        _sink.Log("scan-1", "Info", @"C:\Photos\IMG_1.JPG");
        var logs = Logs();

        await Assert.That((await logs.GetPendingAsync(Http.Get("scan/logs"), ct)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

        await Assert.That((await logs.GetPendingAsync(Http.Get("scan/logs").FromClient(), ct)).Json().GetProperty("count").GetInt32()).IsEqualTo(1);
    }
}
