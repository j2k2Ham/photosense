using PhotoSense.Domain.Repositories;
using PhotoSense.Application.Scanning.Interfaces;
using System.Net;
using PhotoSense.Api.Http;

namespace PhotoSense.Api.Scanning;

/// <summary>The scan running now, or the latest, and how many photos scans have recorded.</summary>
internal sealed class ScanStatusEndpoints
{
    private readonly IPhotoRepository _repo;
    private readonly IScanProgressStore _progress;

    /// <summary>Status from <paramref name="progress"/>, with the records in <paramref name="repo"/> counted.</summary>
    public ScanStatusEndpoints(IPhotoRepository repo, IScanProgressStore progress) { _repo = repo; _progress = progress; }

    /// <summary>GET /api/scan/status: how far the latest scan has got, so the page can follow one still running after a reload.</summary>
    public async Task<ApiResponse> GetStatusAsync(ApiRequest req, CancellationToken ct)
    {
        var photos = await _repo.GetAllAsync(ct);
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
        await resp.WriteAsJsonAsync(payload, ct);
        return resp;
    }
}
