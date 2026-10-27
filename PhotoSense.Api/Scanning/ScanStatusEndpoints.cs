using PhotoSense.Domain.Repositories;
using PhotoSense.Application.Scanning.Interfaces;
using System.Net;
using PhotoSense.Api.Http;

namespace PhotoSense.Api.Scanning;

public class ScanStatusEndpoints
{
    private readonly IPhotoRepository _repo;
    private readonly IScanProgressStore _progress;
    public ScanStatusEndpoints(IPhotoRepository repo, IScanProgressStore progress) { _repo = repo; _progress = progress; }

    public async Task<ApiResponse> GetStatus(
        ApiRequest req)
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
