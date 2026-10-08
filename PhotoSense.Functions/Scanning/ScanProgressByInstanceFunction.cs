using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PhotoSense.Application.Scanning;
using PhotoSense.Application.Scanning.Interfaces;
using System.Net;

namespace PhotoSense.Functions.Scanning;

public class ScanProgressByInstanceFunction
{
    private readonly IScanProgressStore _progress;
    public ScanProgressByInstanceFunction(IScanProgressStore progress) => _progress = progress;

    [Function("GetScanProgressByInstance")] // GET /api/scan/progress/{instanceId}
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "scan/progress/{instanceId}")] HttpRequestData req,
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
            secondsLeft = ScanEstimate.SecondsLeft(snap, DateTime.UtcNow)
        };
        await resp.WriteAsJsonAsync(payload);
        return resp;
    }
}
