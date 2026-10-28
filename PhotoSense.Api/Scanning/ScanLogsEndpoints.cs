using System.Net;
using PhotoSense.Api.Endpoints;
using PhotoSense.Api.Http;
using PhotoSense.Application.Scanning;

namespace PhotoSense.Api.Scanning;

public class ScanLogsEndpoints
{
    private readonly TimeProvider _time;
    public ScanLogsEndpoints(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    // A few requests a second at most, refilled at one a second: the page asks every second and a half.
    private const double Allowance = 20, RefillPerSecond = 1;
    private readonly object _allowanceGate = new();
    private double _tokens = Allowance;
    private DateTimeOffset? _lastRefill;

    // GET /api/scan/logs?limit=: the lines logged since the page last asked, taken off the queue as they are read.
    public async Task<ApiResponse> GetPendingAsync(ApiRequest req)
    {
        // Lines are taken off the queue as they are read, so another page asking would steal them from the UI.
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var limit = int.TryParse(System.Web.HttpUtility.ParseQueryString(req.Url.Query)["limit"], out var parsed) && parsed > 0 ? Math.Min(parsed, 1000) : 200;
        if (!TakeAllowance())
        {
            var tooMany = req.CreateResponse(HttpStatusCode.TooManyRequests);
            await tooMany.WriteStringAsync("Rate limit exceeded. Try again later.");
            return tooMany;
        }
        var drained = Drain(limit).ToList();
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new { items = drained, count = drained.Count });
        return resp;
    }

    private bool TakeAllowance()
    {
        lock (_allowanceGate)
        {
            var now = _time.GetUtcNow();
            if (_lastRefill is { } last) _tokens = Math.Min(Allowance, _tokens + Math.Max(0, (now - last).TotalSeconds) * RefillPerSecond);
            _lastRefill = now;
            if (_tokens < 1) return false;
            _tokens -= 1;
            return true;
        }
    }

    private static IEnumerable<object> Drain(int max)
    {
        for (var count = 0; count < max && InMemoryScanLogSink.TryDequeuePending(out var item); count++)
            yield return new { item.instanceId, timestamp = item.ts, item.level, item.message };
    }
}
