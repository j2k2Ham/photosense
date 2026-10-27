using System.Net;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Api.Endpoints;
using PhotoSense.Api.Http;

namespace PhotoSense.Api.Scanning;

public class ScanResetEndpoints
{
    private readonly IPhotoRepository _repo;
    private readonly IThumbnailStore _thumbnails;
    private readonly IScanProgressStore _progress;

    public ScanResetEndpoints(IPhotoRepository repo, IThumbnailStore thumbnails, IScanProgressStore progress)
    {
        _repo = repo;
        _thumbnails = thumbnails;
        _progress = progress;
    }

    /// <summary>
    /// Forgets everything earlier scans recorded, previews included, so that the next scan starts from
    /// nothing. Only the record of the photos goes: the photos themselves are not touched.
    /// </summary>
    // POST /api/scan/reset
    public async Task<ApiResponse> ResetAsync(
        ApiRequest req)
    {
        var resp = req.CreateResponse();
        if (!RequestGuard.Allows(req.Headers)) { resp.StatusCode = HttpStatusCode.Unauthorized; return resp; }

        // A scan under way is writing the very records this would remove.
        if (ScanRunner.IsRunning(_progress.GetLatest()))
        {
            await resp.WriteAsJsonAsync(new { error = "A scan is running. Wait for it to finish before clearing its results." });
            resp.StatusCode = HttpStatusCode.Conflict; // set after writing: WriteAsJsonAsync resets it to 200
            return resp;
        }

        var forgotten = await _repo.ClearAsync();
        await _thumbnails.ClearAsync();
        await resp.WriteAsJsonAsync(new { forgotten });
        return resp;
    }
}
