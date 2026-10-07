using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Functions.Api;

namespace PhotoSense.Functions.Scanning;

public class ScanResetFunction
{
    private readonly IPhotoRepository _repo;
    private readonly IThumbnailStore _thumbnails;
    private readonly IScanProgressStore _progress;

    public ScanResetFunction(IPhotoRepository repo, IThumbnailStore thumbnails, IScanProgressStore progress)
    {
        _repo = repo;
        _thumbnails = thumbnails;
        _progress = progress;
    }

    /// <summary>
    /// Forgets everything earlier scans recorded, previews included, so that the next scan starts from
    /// nothing. Only the record of the photos goes: the photos themselves are not touched.
    /// </summary>
    [Function("ResetScanResults")] // POST /api/scan/reset
    public async Task<HttpResponseData> ResetAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "scan/reset")] HttpRequestData req)
    {
        var resp = req.CreateResponse();
        if (!RequestGuard.Allows(req.Headers)) { resp.StatusCode = HttpStatusCode.Unauthorized; return resp; }

        // A scan under way is writing the very records this would remove.
        if (ScanHttpStarter.IsRunning(_progress.GetLatest()))
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
