using System.Net;
using PhotoSense.Api.Scanning;
using PhotoSense.Application.Scanning;

namespace PhotoSense.Tests.Endpoints;

// These classes log through one process-wide queue, so they must not run side by side.
[NotInParallel("ScanLogQueue")]
public class ScanLogsEndpointsTests
{
    private readonly InMemoryScanLogSink _sink = new();
    private readonly InMemoryScanProgressStore _progress = new();

    public ScanLogsEndpointsTests() => DrainQueue();

    private static void DrainQueue()
    {
        while (InMemoryScanLogSink.TryDequeuePending(out _)) { }
    }

    private ScanLogsEndpoints Logs(TimeSpan? followFor = null, TimeProvider? time = null)
        => new(_sink, _progress, time) { FollowFor = followFor ?? TimeSpan.FromMilliseconds(250), PollEvery = TimeSpan.FromMilliseconds(40) };

    // ---- what the page polls for

    [Test]
    [Arguments("scan/logs", 30)]              // default limit is 200
    [Arguments("scan/logs?limit=5", 5)]
    [Arguments("scan/logs?limit=0", 30)]      // not a usable limit: default
    [Arguments("scan/logs?limit=-3", 30)]
    [Arguments("scan/logs?limit=abc", 30)]
    [Arguments("scan/logs?limit=999999", 30)] // capped at 1000, which is still more than there is
    public async Task Polling_Hands_Out_Queued_Lines_Up_To_The_Limit(string url, int expected)
    {
        for (int i = 0; i < 30; i++) _sink.Log("scan-1", "Info", $"line {i}");

        var json = (await Logs().GetPendingAsync(Http.Get(url).FromClient())).Json();

        await Assert.That(json.GetProperty("count").GetInt32()).IsEqualTo(expected);
        var first = json.GetProperty("items")[0];
        await Assert.That((first.GetProperty("instanceId").GetString(), first.GetProperty("level").GetString(), first.GetProperty("message").GetString())).IsEqualTo(("scan-1", "Info", "line 0"));
        DrainQueue();
    }

    [Test]
    public async Task Polling_More_Than_A_Thousand_Lines_At_Once_Is_Capped()
    {
        for (int i = 0; i < 1005; i++) _sink.Log("scan-1", "Info", "x");
        var logs = Logs();
        await Assert.That((await logs.GetPendingAsync(Http.Get("scan/logs?limit=5000").FromClient())).Json().GetProperty("count").GetInt32()).IsEqualTo(1000);
        await Assert.That((await logs.GetPendingAsync(Http.Get("scan/logs").FromClient())).Json().GetProperty("count").GetInt32()).IsEqualTo(5);
    }

    [Test]
    public async Task Polling_Too_Often_Is_Turned_Away_Until_The_Allowance_Refills_By_The_Services_Clock()
    {
        var clock = new TestClock();
        var logs = Logs(time: clock);
        for (var i = 0; i < 20; i++)
            await Assert.That((await logs.GetPendingAsync(Http.Get("scan/logs").FromClient())).StatusCode).IsEqualTo(HttpStatusCode.OK);

        var refused = await logs.GetPendingAsync(Http.Get("scan/logs").FromClient());
        await Assert.That(refused.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(refused.Text()).Contains("Rate limit");

        // A second later there is allowance for one more, and only one.
        clock.Now += TimeSpan.FromSeconds(1);
        await Assert.That((await logs.GetPendingAsync(Http.Get("scan/logs").FromClient())).StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await logs.GetPendingAsync(Http.Get("scan/logs").FromClient())).StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
    }

    [Test]
    public async Task The_Log_Is_Shown_Only_To_The_UI_And_Nothing_Is_Taken_Off_The_Queue_For_Anyone_Else()
    {
        _progress.ScanStarted("scan-1");
        _sink.Log("scan-1", "Info", @"C:\Photos\IMG_1.JPG");
        var logs = Logs();

        await Assert.That((await logs.GetPendingAsync(Http.Get("scan/logs"))).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await logs.GetLogs(Http.Get("scan/logs/scan-1"), "scan-1")).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await logs.GetLogsStream(Http.Get("scan/logs/stream/scan-1"), "scan-1")).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

        await Assert.That((await logs.GetPendingAsync(Http.Get("scan/logs").FromClient())).Json().GetProperty("count").GetInt32()).IsEqualTo(1);
    }

    // ---- by scan

    [Test]
    public async Task Lines_Of_One_Scan_Can_Be_Listed_And_Filtered_By_Time()
    {
        _sink.Log("scan-1", "Info", "first");
        await Task.Delay(20);
        var between = DateTime.UtcNow;
        await Task.Delay(20);
        _sink.Log("scan-1", "Info", "second");
        _sink.Log("scan-2", "Info", "another scan");

        var all = (await Logs().GetLogs(Http.Get("scan/logs/scan-1").FromClient(), "scan-1")).Json();
        await Assert.That(all.EnumerateArray().Select(l => l.GetProperty("message").GetString()!)).IsEquivalentTo(new[] { "first", "second" }, CollectionOrdering.Matching);

        var recent = (await Logs().GetLogs(Http.Get($"scan/logs/scan-1?since={Uri.EscapeDataString(between.ToString("o"))}").FromClient(), "scan-1")).Json();
        await Assert.That(recent.EnumerateArray().Single().GetProperty("message").GetString()).IsEqualTo("second");

        var badDate = (await Logs().GetLogs(Http.Get("scan/logs/scan-1?since=yesterday-ish").FromClient(), "scan-1")).Json();
        await Assert.That(badDate.GetArrayLength()).IsEqualTo(2);
    }

    [Test]
    public async Task Without_An_Id_The_Latest_Scan_Is_Meant_And_With_No_Scan_There_Is_Nothing()
    {
        await Assert.That((await Logs().GetLogs(Http.Get("scan/logs/").FromClient(), null)).Json().GetArrayLength()).IsEqualTo(0);
        await Assert.That((await Logs().GetLogsStream(Http.Get("scan/logs/stream/").FromClient(), " ")).Text()).Contains("No active scan");

        _progress.ScanStarted("scan-7");
        _sink.Log("scan-7", "Info", "latest");
        await Assert.That((await Logs().GetLogs(Http.Get("scan/logs/").FromClient(), "")).Json().EnumerateArray().Single().GetProperty("message").GetString()).IsEqualTo("latest");
        await Assert.That((await Logs().GetLogsStream(Http.Get("scan/logs/stream/").FromClient(), null)).Text()).Contains("Info latest");
    }

    [Test]
    [Arguments("scan/logs/stream/scan-1")]
    [Arguments("scan/logs/stream/scan-1?follow=false")]
    [Arguments("scan/logs/stream/scan-1?follow=maybe")]
    public async Task A_Stream_Sends_What_There_Is_And_Ends(string url)
    {
        _sink.Log("scan-1", "Info", "first");
        var response = await Logs().GetLogsStream(Http.Get(url).FromClient(), "scan-1");
        await Assert.That(response.Header("Content-Type")).IsEqualTo("text/event-stream");
        await Assert.That(response.Text()).Contains("event: log");
        await Assert.That(response.Text()).Contains("Info first");
        await Assert.That(response.Text()).DoesNotContain("event: end");
    }

    [Test]
    public async Task A_Followed_Stream_Closes_When_Its_Time_Is_Up_By_The_Services_Clock()
    {
        _sink.Log("scan-1", "Info", "before");
        // Each look at the clock moves it on ten seconds: after thirty, the stream closes.
        var clock = new TestClock(TestClock.Day, TimeSpan.FromSeconds(10));

        var text = (await Logs(TimeSpan.FromSeconds(30), clock).GetLogsStream(Http.Get("scan/logs/stream/scan-1?follow=true").FromClient(), "scan-1")).Text();

        await Assert.That(text).EndsWith("event: end\ndata: stream closed\n\n");
        await Assert.That(clock.Now).IsEqualTo(TestClock.Day.AddSeconds(40));
    }

    [Test]
    public async Task A_Followed_Stream_Also_Sends_Lines_That_Arrive_While_It_Is_Open()
    {
        _sink.Log("scan-1", "Info", "before");
        var stream = Logs(TimeSpan.FromMilliseconds(600)).GetLogsStream(Http.Get("scan/logs/stream/scan-1?follow=true").FromClient(), "scan-1");
        await Task.Delay(120);
        _sink.Log("scan-1", "Info", "during");

        var text = (await stream).Text();

        await Assert.That(text).Contains("Info before");
        await Assert.That(text).Contains("Info during");
        await Assert.That(text).EndsWith("event: end\ndata: stream closed\n\n");
    }
}
