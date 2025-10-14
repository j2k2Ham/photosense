using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
// Optional SignalR integration behind ENABLE_SIGNALR
#if ENABLE_SIGNALR
using Microsoft.Azure.Functions.Worker.SignalRService;
#endif
using System.Net;
using PhotoSense.Infrastructure.Scanning;

namespace PhotoSense.Functions.Realtime;

public static class SignalRLogFunctions
{
    // Basic in-memory token bucket for rate limiting (global). Not distributed; adequate for local / dev.
    private static readonly object _rateLock = new();
    private static double _tokens = 20; // start full
    private const double _capacity = 20; // max requests per window
    private const double _refillPerSecond = 1; // steady refill
    private static DateTime _lastRefill = DateTime.UtcNow;

    private static void RefillTokens()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastRefill).TotalSeconds;
        if (elapsed <= 0) return;
        _tokens = Math.Min(_capacity, _tokens + elapsed * _refillPerSecond);
        _lastRefill = now;
    }

    private static bool TryConsumeToken()
    {
        lock (_rateLock)
        {
            RefillTokens();
            if (_tokens >= 1)
            {
                _tokens -= 1;
                return true;
            }
            return false;
        }
    }

    internal static IEnumerable<object> DrainPending(int max)
    {
        var count = 0;
        while (count < max && InMemoryScanLogSink.TryDequeuePending(out var item))
        {
            yield return new { item.instanceId, timestamp = item.ts, item.level, item.message };
            count++;
        }
    }

    // Fallback: HTTP polling to fetch accumulated scan log lines
    [Function("GetScanLogs")]
    public static async Task<HttpResponseData> GetScanLogs(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "scan/logs")] HttpRequestData req)
    {
        // Parse limit
        var uri = req.Url;
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        int limit = 200;
        if (int.TryParse(query["limit"], out var parsed) && parsed > 0)
        {
            limit = Math.Min(parsed, 1000); // hard cap
        }

        if (!TryConsumeToken())
        {
            var tooMany = req.CreateResponse(HttpStatusCode.TooManyRequests);
            await tooMany.WriteStringAsync("Rate limit exceeded. Try again later.");
            return tooMany;
        }

        var drained = DrainPending(limit).ToList();
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new { items = drained, count = drained.Count });
        return resp;
    }

#if ENABLE_SIGNALR
    [Function("NegotiateScanLogs")]
    public static async Task<HttpResponseData> Negotiate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "get", Route = "scan/logs/negotiate")] HttpRequestData req,
        [SignalRConnectionInfoInput(HubName = "scanlogs")] SignalRConnectionInfo connectionInfo)
    {
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(connectionInfo);
        return resp;
    }

    // Attempt 2.x style: return array of SignalRMessage objects (if available) every 5s
    [Function("BroadcastScanLogs")]
    [SignalROutput(HubName = "scanlogs")] // If attribute differs in 2.x this will cause a compile error to adjust.
    public static object[] BroadcastScanLogs([TimerTrigger("*/5 * * * * *")] TimerInfo timer)
    {
        // Limit broadcast batch size to avoid giant payloads
        var batch = DrainPending(500)
            .Select(i => new { target = "log", arguments = new object[] { ((dynamic)i).instanceId, ((dynamic)i).timestamp.ToString("o"), ((dynamic)i).level, ((dynamic)i).message } })
            .ToArray();
        return batch;
    }
#endif
}