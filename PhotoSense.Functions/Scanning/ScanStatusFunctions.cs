using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PhotoSense.Domain.Repositories;
using PhotoSense.Application.Scanning.Interfaces;
using System.Net;

namespace PhotoSense.Functions.Scanning;

public class ScanStatusFunctions
{
    private readonly IPhotoRepository _repo;
    private readonly IScanProgressStore _progress;
    public ScanStatusFunctions(IPhotoRepository repo, IScanProgressStore progress) { _repo = repo; _progress = progress; }

    [Function("ScanStatus")]
    public async Task<HttpResponseData> GetStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "scan/status")] HttpRequestData req)
    {
        var photos = await _repo.GetAllAsync();
        var snap = _progress.GetLatest();
        var resp = req.CreateResponse(HttpStatusCode.OK);
        var payload = new
        {
            instanceId = snap.InstanceId,
            started = snap.StartedUtc,
            completed = snap.CompletedUtc,
            primaryTotal = snap.PrimaryTotal,
            primaryProcessed = snap.PrimaryProcessed,
            primaryPercent = snap.PrimaryPercent,
            secondaryTotal = snap.SecondaryTotal,
            secondaryProcessed = snap.SecondaryProcessed,
            secondaryPercent = snap.SecondaryPercent,
            overallPercent = snap.OverallPercent,
            totalPhotos = photos.Count
        };
        await resp.WriteAsJsonAsync(payload);
        return resp;
    }
}
