using System.Net;
using PhotoSense.Api.Endpoints;
using PhotoSense.Api.Http;
using PhotoSense.Application.Scanning;
using PhotoSense.Application.Scanning.Interfaces;

namespace PhotoSense.Api.Scanning;

public class ScanLogsEndpoints
{
    private readonly IScanLogSink _sink;
    private readonly IScanProgressStore _progress;
    private readonly TimeProvider _time;
    public ScanLogsEndpoints(IScanLogSink sink, IScanProgressStore progress, TimeProvider? time = null) { _sink = sink; _progress = progress; _time = time ?? TimeProvider.System; }

    /// <summary>How long a followed stream stays open, and how often it looks for new lines.</summary>
    public TimeSpan FollowFor { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan PollEvery { get; init; } = TimeSpan.FromSeconds(2);

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

    // GET /api/scan/logs/{instanceId}: the lines of one scan, or of the latest; ?since= leaves out the older ones.
    public async Task<ApiResponse> GetLogs(ApiRequest req, string? instanceId)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var snap = _progress.GetLatest();
        var id = string.IsNullOrWhiteSpace(instanceId) ? snap.InstanceId : instanceId;
        var resp = req.CreateResponse(HttpStatusCode.OK);
        DateTime? since = null;
        var qs = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var sinceRaw = qs.Get("since");
    if (DateTime.TryParse(sinceRaw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed)) since = parsed.ToUniversalTime();
        var lines = string.IsNullOrWhiteSpace(id)
            ? new List<object>()
            : _sink.GetRecent(id).Where(l => !since.HasValue || l.ts > since.Value)
                .Select(l => (object)new { l.ts, l.level, l.message }).ToList();
        await resp.WriteAsJsonAsync(lines);
        return resp;
    }

    // GET /api/scan/logs/stream/{instanceId}: the same as server-sent events; with ?follow=true it waits for more lines before it ends.
    public async Task<ApiResponse> GetLogsStream(ApiRequest req, string? instanceId)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var snap = _progress.GetLatest();
        var id = string.IsNullOrWhiteSpace(instanceId) ? snap.InstanceId : instanceId;
        var resp = req.CreateResponse(System.Net.HttpStatusCode.OK);
        resp.Headers.Add("Content-Type", "text/event-stream");
        if (string.IsNullOrWhiteSpace(id)) { await resp.WriteStringAsync("event: message\ndata: No active scan\n\n"); return resp; }
        var qs = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        bool follow = bool.TryParse(qs.Get("follow"), out var f) && f;
        var recent = _sink.GetRecent(id).Select(l => $"event: log\ndata: {l.ts:o} {l.level} {l.message}\n\n");
        await resp.WriteStringAsync(string.Join(string.Empty, recent));
        if (follow)
        {
            var start = _time.GetUtcNow();
            var lastCount = _sink.GetRecent(id).Count;
            while (_time.GetUtcNow() - start < FollowFor)
            {
                await Task.Delay(PollEvery, _time);
                var nowLogs = _sink.GetRecent(id);
                if (nowLogs.Count > lastCount)
                {
                    foreach (var l in nowLogs.Skip(lastCount))
                        await resp.WriteStringAsync($"event: log\ndata: {l.ts:o} {l.level} {l.message}\n\n");
                    lastCount = nowLogs.Count;
                }
            }
            await resp.WriteStringAsync("event: end\ndata: stream closed\n\n");
        }
        return resp;
    }
}
