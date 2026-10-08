using System.Net;
using System.Reflection;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.SignalRService;
using PhotoSense.Application.Scanning;
using PhotoSense.Functions.Realtime;
using PhotoSense.Functions.Scanning;
using PhotoSense.Infrastructure.Scanning;

namespace PhotoSense.Tests.Functions;

// These classes log through one process-wide queue, so they must not run side by side.
[NotInParallel("ScanLogQueue")]
public class ScanLogFunctionsTests
{
    private readonly InMemoryScanLogSink _sink = new();
    private readonly InMemoryScanProgressStore _progress = new();

    public ScanLogFunctionsTests()
    {
        DrainQueue();
        SetRateLimiter(tokens: 20, lastRefill: DateTime.UtcNow);
    }

    // The polling endpoint keeps its rate limiter and its queue in static fields.
    private static void SetRateLimiter(double tokens, DateTime lastRefill)
    {
        var type = typeof(SignalRLogFunctions);
        type.GetField("_tokens", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, tokens);
        type.GetField("_lastRefill", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, lastRefill);
    }

    private static void DrainQueue()
    {
        while (InMemoryScanLogSink.TryDequeuePending(out _)) { }
    }

    private ScanLogsStubFunction ByInstance(TimeSpan? followFor = null)
        => new(_sink, _progress) { FollowFor = followFor ?? TimeSpan.FromMilliseconds(250), PollEvery = TimeSpan.FromMilliseconds(40) };

    // ---- polling endpoint

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

        var json = (await SignalRLogFunctions.GetScanLogs(Http.Get(url))).Json();

        await Assert.That(json.GetProperty("count").GetInt32()).IsEqualTo(expected);
        var first = json.GetProperty("items")[0];
        await Assert.That((first.GetProperty("instanceId").GetString(), first.GetProperty("level").GetString(), first.GetProperty("message").GetString())).IsEqualTo(("scan-1", "Info", "line 0"));
        DrainQueue();
    }

    [Test]
    public async Task Polling_More_Than_A_Thousand_Lines_At_Once_Is_Capped()
    {
        for (int i = 0; i < 1005; i++) _sink.Log("scan-1", "Info", "x");
        await Assert.That((await SignalRLogFunctions.GetScanLogs(Http.Get("scan/logs?limit=5000"))).Json().GetProperty("count").GetInt32()).IsEqualTo(1000);
        await Assert.That((await SignalRLogFunctions.GetScanLogs(Http.Get("scan/logs"))).Json().GetProperty("count").GetInt32()).IsEqualTo(5);
    }

    [Test]
    public async Task Polling_Too_Often_Is_Turned_Away_Until_The_Allowance_Refills()
    {
        SetRateLimiter(tokens: 1, lastRefill: DateTime.UtcNow.AddMinutes(1)); // a clock that has not advanced: nothing refills
        await Assert.That((await SignalRLogFunctions.GetScanLogs(Http.Get("scan/logs"))).StatusCode).IsEqualTo(HttpStatusCode.OK);

        var refused = await SignalRLogFunctions.GetScanLogs(Http.Get("scan/logs"));
        await Assert.That(refused.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(refused.Text()).Contains("Rate limit");

        SetRateLimiter(tokens: 0, lastRefill: DateTime.UtcNow.AddSeconds(-5)); // five seconds later there is allowance again
        await Assert.That((await SignalRLogFunctions.GetScanLogs(Http.Get("scan/logs"))).StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    // ---- SignalR

    [Test]
    public async Task Negotiation_Returns_The_Connection_It_Was_Given()
    {
        var info = new SignalRConnectionInfo { Url = "https://example.service.signalr.net/client/?hub=scanlogs", AccessToken = "token" };
        var response = await SignalRLogFunctions.Negotiate(Http.Post("scan/logs/negotiate"), info);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Text()).Contains("example.service.signalr.net");
    }

    [Test]
    public async Task The_Broadcast_Sends_Each_Queued_Line_As_A_Log_Message()
    {
        _sink.Log("scan-1", "Warn", "Could not decode a.heic");
        _sink.Log("scan-1", "Info", "Scan complete");

        var messages = SignalRLogFunctions.BroadcastScanLogs(new TimerInfo());

        await Assert.That(messages.Length).IsEqualTo(2);
        var first = System.Text.Json.JsonSerializer.SerializeToElement(messages[0]);
        await Assert.That(first.GetProperty("target").GetString()).IsEqualTo("log");
        var arguments = first.GetProperty("arguments");
        await Assert.That((arguments[0].GetString(), arguments[2].GetString(), arguments[3].GetString())).IsEqualTo(("scan-1", "Warn", "Could not decode a.heic"));
        await Assert.That(SignalRLogFunctions.BroadcastScanLogs(new TimerInfo())).IsEmpty(); // the queue was emptied
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

        var all = (await ByInstance().GetLogs(Http.Get("scan/logs/scan-1"), "scan-1")).Json();
        await Assert.That(all.EnumerateArray().Select(l => l.GetProperty("message").GetString()!)).IsEquivalentTo(new[] { "first", "second" }, CollectionOrdering.Matching);

        var recent = (await ByInstance().GetLogs(Http.Get($"scan/logs/scan-1?since={Uri.EscapeDataString(between.ToString("o"))}"), "scan-1")).Json();
        await Assert.That(recent.EnumerateArray().Single().GetProperty("message").GetString()).IsEqualTo("second");

        var badDate = (await ByInstance().GetLogs(Http.Get("scan/logs/scan-1?since=yesterday-ish"), "scan-1")).Json();
        await Assert.That(badDate.GetArrayLength()).IsEqualTo(2);
    }

    [Test]
    public async Task Without_An_Id_The_Latest_Scan_Is_Meant_And_With_No_Scan_There_Is_Nothing()
    {
        await Assert.That((await ByInstance().GetLogs(Http.Get("scan/logs/"), null)).Json().GetArrayLength()).IsEqualTo(0);
        await Assert.That((await ByInstance().GetLogsStream(Http.Get("scan/logs/stream/"), " ")).Text()).Contains("No active scan");

        _progress.ScanStarted("scan-7");
        _sink.Log("scan-7", "Info", "latest");
        await Assert.That((await ByInstance().GetLogs(Http.Get("scan/logs/"), "")).Json().EnumerateArray().Single().GetProperty("message").GetString()).IsEqualTo("latest");
        await Assert.That((await ByInstance().GetLogsStream(Http.Get("scan/logs/stream/"), null)).Text()).Contains("Info latest");
    }

    [Test]
    [Arguments("scan/logs/stream/scan-1")]
    [Arguments("scan/logs/stream/scan-1?follow=false")]
    [Arguments("scan/logs/stream/scan-1?follow=maybe")]
    public async Task A_Stream_Sends_What_There_Is_And_Ends(string url)
    {
        _sink.Log("scan-1", "Info", "first");
        var response = await ByInstance().GetLogsStream(Http.Get(url), "scan-1");
        await Assert.That(response.Header("Content-Type")).IsEqualTo("text/event-stream");
        await Assert.That(response.Text()).Contains("event: log");
        await Assert.That(response.Text()).Contains("Info first");
        await Assert.That(response.Text()).DoesNotContain("event: end");
    }

    [Test]
    public async Task A_Followed_Stream_Also_Sends_Lines_That_Arrive_While_It_Is_Open()
    {
        _sink.Log("scan-1", "Info", "before");
        var stream = ByInstance(TimeSpan.FromMilliseconds(600)).GetLogsStream(Http.Get("scan/logs/stream/scan-1?follow=true"), "scan-1");
        await Task.Delay(120);
        _sink.Log("scan-1", "Info", "during");

        var text = (await stream).Text();

        await Assert.That(text).Contains("Info before");
        await Assert.That(text).Contains("Info during");
        await Assert.That(text).EndsWith("event: end\ndata: stream closed\n\n");
    }
}
