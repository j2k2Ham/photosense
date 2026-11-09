using PhotoSense.Application.Scanning;
using PhotoSense.Application.Scanning.Interfaces;
using System.Net;
using PhotoSense.Api.Http;

namespace PhotoSense.Api.Scanning;

/// <summary>How far one scan has got, with how long it has left.</summary>
internal sealed class ScanProgressEndpoints
{
    private readonly IScanProgressStore _progress;
    private readonly TimeProvider _time;

    /// <summary>Progress from <paramref name="progress"/>, timed by <paramref name="time"/> or the system's clock when none is given.</summary>
    public ScanProgressEndpoints(IScanProgressStore progress, TimeProvider? time = null) { _progress = progress; _time = time ?? TimeProvider.System; }

    /// <summary>GET /api/scan/progress/{instanceId}: how far the scan has got; 404 for a scan this run has not heard of.</summary>
    public async Task<ApiResponse> GetAsync(ApiRequest req, string instanceId, CancellationToken ct)
    {
        var snap = _progress.Get(instanceId);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        // The store answers for any id; one it has never heard of has no start time.
        if (snap.StartedUtc == default)
        {
            resp.StatusCode = HttpStatusCode.NotFound;
            return resp;
        }
        var payload = new
        {
            instanceId = snap.InstanceId,
            startedUtc = snap.StartedUtc,
            completedUtc = snap.CompletedUtc,
            primaryTotal = snap.PrimaryTotal,
            primaryProcessed = snap.PrimaryProcessed,
            primaryPercent = snap.PrimaryPercent,
            secondaryTotal = snap.SecondaryTotal,
            secondaryProcessed = snap.SecondaryProcessed,
            secondaryPercent = snap.SecondaryPercent,
            overallPercent = snap.OverallPercent,
            // Null until there is something to go by: this scan's own pace, or what earlier scans took.
            secondsLeft = ScanEstimate.SecondsLeft(snap, _time.GetUtcNow().UtcDateTime)
        };
        await resp.WriteAsJsonAsync(payload, ct);
        return resp;
    }
}
