using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
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
        if (snap.InstanceId == string.Empty)
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
            overallPercent = snap.OverallPercent
        };
        await resp.WriteAsJsonAsync(payload);
        return resp;
    }
}
