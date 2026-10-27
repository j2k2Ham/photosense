using PhotoSense.Application.Scanning;
using PhotoSense.Application.Scanning.Interfaces;
using System.Net;
using PhotoSense.Api.Http;

namespace PhotoSense.Api.Scanning;

public class ScanProgressEndpoints
{
    private readonly IScanProgressStore _progress;
    private readonly TimeProvider _time;
    public ScanProgressEndpoints(IScanProgressStore progress, TimeProvider? time = null) { _progress = progress; _time = time ?? TimeProvider.System; }

    // GET /api/scan/progress/{instanceId}
    public async Task<ApiResponse> Get(
        ApiRequest req,
        string instanceId)
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
        await resp.WriteAsJsonAsync(payload);
        return resp;
    }
}
