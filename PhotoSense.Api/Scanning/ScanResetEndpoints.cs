using System.Net;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Api.Endpoints;
using PhotoSense.Api.Http;

namespace PhotoSense.Api.Scanning;

/// <summary>Forgets what earlier scans recorded.</summary>
internal sealed class ScanResetEndpoints
{
    private readonly IPhotoRepository _repo;
    private readonly IThumbnailStore _thumbnails;
    private readonly IScanProgressStore _progress;

    /// <summary>Forgets the records in <paramref name="repo"/> and the previews in <paramref name="thumbnails"/>.</summary>
    public ScanResetEndpoints(IPhotoRepository repo, IThumbnailStore thumbnails, IScanProgressStore progress)
    {
        _repo = repo;
        _thumbnails = thumbnails;
        _progress = progress;
    }

    /// <summary>
    /// POST /api/scan/reset: forgets everything earlier scans recorded, previews included, so that the next scan
    /// starts from nothing. Only the record of the photos goes: the photos themselves are not touched.
    /// </summary>
    public async Task<ApiResponse> ResetAsync(ApiRequest req, CancellationToken ct)
    {
        var resp = req.CreateResponse();
        if (!RequestGuard.Allows(req.Headers)) { resp.StatusCode = HttpStatusCode.Unauthorized; return resp; }

        // A scan under way is writing the very records this would remove.
        if (ScanRunner.IsRunning(_progress.GetLatest()))
        {
            await resp.WriteAsJsonAsync(new { error = "A scan is running. Wait for it to finish before clearing its results." }, ct);
            resp.StatusCode = HttpStatusCode.Conflict;
            return resp;
        }

        var forgotten = await _repo.ClearAsync(ct);
        await _thumbnails.ClearAsync(ct);
        await resp.WriteAsJsonAsync(new { forgotten }, ct);
        return resp;
    }
}
