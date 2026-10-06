using System.Net;
using System.Reflection;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.SignalRService;
using PhotoSense.Application.Scanning;
using PhotoSense.Functions.Realtime;
using PhotoSense.Functions.Scanning;
using PhotoSense.Infrastructure.Scanning;
using Xunit;

namespace PhotoSense.Tests.Functions;

// These classes log through one process-wide queue, so they must not run side by side.
[Collection("ScanLogQueue")]
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

    [Theory]
    [InlineData("scan/logs", 30)]              // default limit is 200
    [InlineData("scan/logs?limit=5", 5)]
    [InlineData("scan/logs?limit=0", 30)]      // not a usable limit: default
    [InlineData("scan/logs?limit=-3", 30)]
    [InlineData("scan/logs?limit=abc", 30)]
    [InlineData("scan/logs?limit=999999", 30)] // capped at 1000, which is still more than there is
    public async Task Polling_Hands_Out_Queued_Lines_Up_To_The_Limit(string url, int expected)
    {
        for (int i = 0; i < 30; i++) _sink.Log("scan-1", "Info", $"line {i}");

        var json = (await SignalRLogFunctions.GetScanLogs(Http.Get(url))).Json();

        Assert.Equal(expected, json.GetProperty("count").GetInt32());
        var first = json.GetProperty("items")[0];
        Assert.Equal(("scan-1", "Info", "line 0"), (first.GetProperty("instanceId").GetString(), first.GetProperty("level").GetString(), first.GetProperty("message").GetString()));
        DrainQueue();
    }

    [Fact]
    public async Task Polling_More_Than_A_Thousand_Lines_At_Once_Is_Capped()
    {
        for (int i = 0; i < 1005; i++) _sink.Log("scan-1", "Info", "x");
        Assert.Equal(1000, (await SignalRLogFunctions.GetScanLogs(Http.Get("scan/logs?limit=5000"))).Json().GetProperty("count").GetInt32());
        Assert.Equal(5, (await SignalRLogFunctions.GetScanLogs(Http.Get("scan/logs"))).Json().GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Polling_Too_Often_Is_Turned_Away_Until_The_Allowance_Refills()
    {
        SetRateLimiter(tokens: 1, lastRefill: DateTime.UtcNow.AddMinutes(1)); // a clock that has not advanced: nothing refills
        Assert.Equal(HttpStatusCode.OK, (await SignalRLogFunctions.GetScanLogs(Http.Get("scan/logs"))).StatusCode);

        var refused = await SignalRLogFunctions.GetScanLogs(Http.Get("scan/logs"));
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Contains("Rate limit", refused.Text());

        SetRateLimiter(tokens: 0, lastRefill: DateTime.UtcNow.AddSeconds(-5)); // five seconds later there is allowance again
        Assert.Equal(HttpStatusCode.OK, (await SignalRLogFunctions.GetScanLogs(Http.Get("scan/logs"))).StatusCode);
    }

    // ---- SignalR

    [Fact]
    public async Task Negotiation_Returns_The_Connection_It_Was_Given()
    {
        var info = new SignalRConnectionInfo { Url = "https://example.service.signalr.net/client/?hub=scanlogs", AccessToken = "token" };
        var response = await SignalRLogFunctions.Negotiate(Http.Post("scan/logs/negotiate"), info);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("example.service.signalr.net", response.Text());
    }

    [Fact]
    public void The_Broadcast_Sends_Each_Queued_Line_As_A_Log_Message()
    {
        _sink.Log("scan-1", "Warn", "Could not decode a.heic");
        _sink.Log("scan-1", "Info", "Scan complete");

        var messages = SignalRLogFunctions.BroadcastScanLogs(new TimerInfo());

        Assert.Equal(2, messages.Length);
        var first = System.Text.Json.JsonSerializer.SerializeToElement(messages[0]);
        Assert.Equal("log", first.GetProperty("target").GetString());
        var arguments = first.GetProperty("arguments");
        Assert.Equal(("scan-1", "Warn", "Could not decode a.heic"), (arguments[0].GetString(), arguments[2].GetString(), arguments[3].GetString()));
        Assert.Empty(SignalRLogFunctions.BroadcastScanLogs(new TimerInfo())); // the queue was emptied
    }

    // ---- by scan

    [Fact]
    public async Task Lines_Of_One_Scan_Can_Be_Listed_And_Filtered_By_Time()
    {
        _sink.Log("scan-1", "Info", "first");
        await Task.Delay(20);
        var between = DateTime.UtcNow;
        await Task.Delay(20);
        _sink.Log("scan-1", "Info", "second");
        _sink.Log("scan-2", "Info", "another scan");

        var all = (await ByInstance().GetLogs(Http.Get("scan/logs/scan-1"), "scan-1")).Json();
        Assert.Equal(new[] { "first", "second" }, all.EnumerateArray().Select(l => l.GetProperty("message").GetString()));

        var recent = (await ByInstance().GetLogs(Http.Get($"scan/logs/scan-1?since={Uri.EscapeDataString(between.ToString("o"))}"), "scan-1")).Json();
        Assert.Equal("second", Assert.Single(recent.EnumerateArray()).GetProperty("message").GetString());

        var badDate = (await ByInstance().GetLogs(Http.Get("scan/logs/scan-1?since=yesterday-ish"), "scan-1")).Json();
        Assert.Equal(2, badDate.GetArrayLength());
    }

    [Fact]
    public async Task Without_An_Id_The_Latest_Scan_Is_Meant_And_With_No_Scan_There_Is_Nothing()
    {
        Assert.Equal(0, (await ByInstance().GetLogs(Http.Get("scan/logs/"), null)).Json().GetArrayLength());
        Assert.Contains("No active scan", (await ByInstance().GetLogsStream(Http.Get("scan/logs/stream/"), " ")).Text());

        _progress.ScanStarted("scan-7");
        _sink.Log("scan-7", "Info", "latest");
        Assert.Equal("latest", Assert.Single((await ByInstance().GetLogs(Http.Get("scan/logs/"), "")).Json().EnumerateArray()).GetProperty("message").GetString());
        Assert.Contains("Info latest", (await ByInstance().GetLogsStream(Http.Get("scan/logs/stream/"), null)).Text());
    }

    [Theory]
    [InlineData("scan/logs/stream/scan-1")]
    [InlineData("scan/logs/stream/scan-1?follow=false")]
    [InlineData("scan/logs/stream/scan-1?follow=maybe")]
    public async Task A_Stream_Sends_What_There_Is_And_Ends(string url)
    {
        _sink.Log("scan-1", "Info", "first");
        var response = await ByInstance().GetLogsStream(Http.Get(url), "scan-1");
        Assert.Equal("text/event-stream", response.Header("Content-Type"));
        Assert.Contains("event: log", response.Text());
        Assert.Contains("Info first", response.Text());
        Assert.DoesNotContain("event: end", response.Text());
    }

    [Fact]
    public async Task A_Followed_Stream_Also_Sends_Lines_That_Arrive_While_It_Is_Open()
    {
        _sink.Log("scan-1", "Info", "before");
        var stream = ByInstance(TimeSpan.FromMilliseconds(600)).GetLogsStream(Http.Get("scan/logs/stream/scan-1?follow=true"), "scan-1");
        await Task.Delay(120);
        _sink.Log("scan-1", "Info", "during");

        var text = (await stream).Text();

        Assert.Contains("Info before", text);
        Assert.Contains("Info during", text);
        Assert.EndsWith("event: end\ndata: stream closed\n\n", text);
    }
}
