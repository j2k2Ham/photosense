using System.Net;
using System.Text.Json;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Entities;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Contracts.Duplicates;
using PhotoSense.Api.Http;

namespace PhotoSense.Api.Endpoints;

/// <summary>The photos a scan found: their previews, pictures and videos, opening them, keeping them, and removing them.</summary>
internal sealed class PhotosEndpoints
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    // Largest edge of the picture sent to the review window when the original cannot be shown by a browser.
    private const int ReviewImageEdge = 2560;

    // Formats a browser displays as they are; anything else (HEIC, TIFF) is converted to JPEG for viewing.
    private static readonly Dictionary<string, string> BrowserFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".gif"] = "image/gif", [".webp"] = "image/webp", [".bmp"] = "image/bmp"
    };

    // What a browser is told a video is. A QuickTime file holds the same streams as an MP4 and browsers
    // that refuse "video/quicktime" outright will play it when it is called MP4.
    private static readonly Dictionary<string, string> VideoFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = "video/mp4", [".m4v"] = "video/mp4", [".mov"] = "video/mp4", [".3gp"] = "video/3gpp"
    };

    private readonly IPhotoRepository _repo;
    private readonly IDuplicateRemovalService _remover;
    private readonly IThumbnailStore _thumbnails;
    private readonly IImageAnalyzer _analyzer;
    private readonly IAuditRepository _audit;
    private readonly IScanLogSink _log;
    private readonly ISystemViewer _viewer;
    private readonly IPhotoFileSystem _files;

    /// <summary>The photos over the services given.</summary>
    public PhotosEndpoints(IPhotoRepository repo, IDuplicateRemovalService remover, IThumbnailStore thumbnails, IImageAnalyzer analyzer,
        IAuditRepository audit, IScanLogSink log, ISystemViewer viewer, IPhotoFileSystem files)
    {
        _viewer = viewer;
        _files = files;
        _repo = repo; _remover = remover;
        _thumbnails = thumbnails; _analyzer = analyzer; _audit = audit; _log = log;
    }

    /// <summary>GET /api/photos/{id}/thumbnail: a photo's preview, made again from the file when the cache has lost it.</summary>
    public async Task<ApiResponse> GetThumbnailAsync(ApiRequest req, string id, CancellationToken ct)
    {
        var photo = await _repo.GetAsync(new PhotoId(Guid.Parse(id)), ct);
        // Videos are never decoded, so they have no picture to show.
        if (photo?.ContentHash is null || photo.IsVideo) return req.CreateResponse(HttpStatusCode.NotFound);

        var jpeg = await _thumbnails.GetAsync(photo.ContentHash, ct);
        if (jpeg is null)
        {
            // The cache was cleared since the scan: make the thumbnail again from the file.
            try
            {
                await using var stream = await _files.OpenReadAsync(photo.SourcePath, ct);
                jpeg = (await _analyzer.AnalyzeAsync(stream, ct)).ThumbnailJpeg;
                await _thumbnails.SaveAsync(photo.ContentHash, jpeg, ct);
            }
            // A picture that cannot be decoded, or read, has no preview to show.
            catch (Exception ex) when (ex is UnreadableImageException or IOException or UnauthorizedAccessException)
            {
                return req.CreateResponse(HttpStatusCode.NotFound);
            }
        }
        return await ImageResponseAsync(req, jpeg, "image/jpeg", photo.ContentHash, ct);
    }

    /// <summary>GET /api/photos/{id}/image: the picture, in a form a browser can show at full size.</summary>
    public async Task<ApiResponse> GetImageAsync(ApiRequest req, string id, CancellationToken ct)
    {
        var photo = await _repo.GetAsync(new PhotoId(Guid.Parse(id)), ct);
        if (photo is null || photo.IsVideo || !_files.FileExists(photo.SourcePath)) return req.CreateResponse(HttpStatusCode.NotFound);
        return await ShowImageAsync(req, _files, photo.SourcePath, photo.ContentHash, _analyzer, ct);
    }

    /// <summary>The picture in a file, in a form a browser can show at full size.</summary>
    /// <param name="req">The request answered.</param>
    /// <param name="files">Where the photos are kept.</param>
    /// <param name="path">The file.</param>
    /// <param name="contentHash">The hash of its bytes, which lets the browser keep what it was sent; null when not worked out.</param>
    /// <param name="analyzer">Converts a picture the browser cannot show.</param>
    /// <param name="ct">Stops the reading and the converting.</param>
    public static async Task<ApiResponse> ShowImageAsync(ApiRequest req, IPhotoFileSystem files, string path, string? contentHash, IImageAnalyzer analyzer, CancellationToken ct)
    {
        try
        {
            // What browsers can show is sent as it is; anything else is converted for viewing.
            var asItIs = BrowserFormats.TryGetValue(Path.GetExtension(path), out var contentType);
            byte[] bytes;
            await using (var stream = await files.OpenReadAsync(path, ct))
            {
                if (asItIs) await stream.ReadExactlyAsync(bytes = new byte[stream.Length], ct);
                else bytes = await analyzer.RenderJpegAsync(stream, ReviewImageEdge, ct);
            }
            return await ImageResponseAsync(req, bytes, contentType ?? "image/jpeg", contentHash, ct);
        }
        // A picture that cannot be decoded, or read, is said so, with why.
        catch (Exception ex) when (ex is UnreadableImageException or IOException or UnauthorizedAccessException)
        {
            var failed = req.CreateResponse(HttpStatusCode.UnprocessableEntity);
            await failed.WriteStringAsync($"This file could not be shown: {ex.Message}", ct);
            return failed;
        }
    }

    /// <summary>GET /api/photos/{id}/video: the piece of a video the player asked for.</summary>
    public async Task<ApiResponse> GetVideoAsync(ApiRequest req, string id, CancellationToken ct)
    {
        var photo = await _repo.GetAsync(new PhotoId(Guid.Parse(id)), ct);
        if (photo is null || !photo.IsVideo || !_files.FileExists(photo.SourcePath)) return req.CreateResponse(HttpStatusCode.NotFound);

        await using var file = await _files.OpenReadAsync(photo.SourcePath, ct);
        var requested = req.Headers.TryGetValues("Range", out var values) ? values.FirstOrDefault() : null;
        if (ByteRange.Parse(requested, file.Length) is not { } range)
        {
            var unsatisfiable = req.CreateResponse(HttpStatusCode.RequestedRangeNotSatisfiable);
            unsatisfiable.Headers.Add("Content-Range", $"bytes */{file.Length}");
            return unsatisfiable;
        }

        var piece = new byte[range.Length];
        file.Position = range.Start;
        await file.ReadExactlyAsync(piece, ct);

        var resp = req.CreateResponse(HttpStatusCode.PartialContent);
        resp.Headers.Add("Content-Type", VideoFormats.GetValueOrDefault(Path.GetExtension(photo.SourcePath), "application/octet-stream"));
        resp.Headers.Add("Accept-Ranges", "bytes");
        resp.Headers.Add("Content-Range", range.ContentRange(file.Length));
        await resp.Body.WriteAsync(piece, ct);
        return resp;
    }

    /// <summary>POST /api/photos/{id}/open: opens the photo in the default application of the machine that holds it.</summary>
    public async Task<ApiResponse> OpenPhotoAsync(ApiRequest req, string id, CancellationToken ct)
    {
        var resp = req.CreateResponse();
        if (!RequestGuard.Allows(req.Headers)) { resp.StatusCode = HttpStatusCode.Unauthorized; return resp; }
        var photo = await _repo.GetAsync(new PhotoId(Guid.Parse(id)), ct);
        if (photo is null) { resp.StatusCode = HttpStatusCode.NotFound; return resp; }
        try
        {
            // Opens on the machine this service runs on, which is the machine that holds the photos.
            _viewer.Open(photo.SourcePath);
            resp.StatusCode = HttpStatusCode.NoContent;
        }
        catch (FileNotFoundException)
        {
            await resp.WriteStringAsync("The file is no longer there. Scan again.", ct);
            resp.StatusCode = HttpStatusCode.NotFound;
        }
        catch (InvalidOperationException ex)
        {
            await resp.WriteStringAsync(ex.Message, ct);
            resp.StatusCode = HttpStatusCode.InternalServerError;
        }
        return resp;
    }

    /// <summary>A picture's bytes as an answer. A file's bytes never change under the same content hash, so the browser may keep what it was sent.</summary>
    /// <param name="req">The request answered.</param>
    /// <param name="bytes">The picture.</param>
    /// <param name="contentType">What the browser is told it is.</param>
    /// <param name="contentHash">The hash it is known by, which lets the browser keep it; null for none.</param>
    /// <param name="ct">Stops the writing.</param>
    public static async Task<ApiResponse> ImageResponseAsync(ApiRequest req, byte[] bytes, string contentType, string? contentHash, CancellationToken ct)
    {
        var resp = req.CreateResponse(HttpStatusCode.OK);
        resp.Headers.Add("Content-Type", contentType);
        resp.Headers.Add("Cache-Control", "private, max-age=86400");
        if (contentHash is not null) resp.Headers.Add("ETag", $"\"{contentHash}\"");
        await resp.Body.WriteAsync(bytes, ct);
        return resp;
    }

    /// <summary>
    /// DELETE /api/photos/{id}?physical=: forgets a photo, and with physical=true moves its file into the folder that
    /// holds removed files, as long as removing it is safe.
    /// </summary>
    public async Task<ApiResponse> DeletePhotoAsync(ApiRequest req, string id, CancellationToken ct)
    {
        var resp = req.CreateResponse();
        if (!RequestGuard.Allows(req.Headers)) { resp.StatusCode = HttpStatusCode.Unauthorized; return resp; }
        var physical = System.Web.HttpUtility.ParseQueryString(req.Url.Query).Get("physical") == "true";
        var gid = Guid.Parse(id);
        var result = await _remover.RemoveAsync(new PhotoId(gid), physical, ct);
        switch (result.Outcome)
        {
            case RemovalOutcome.NotFound:
                resp.StatusCode = HttpStatusCode.NotFound;
                return resp;
            case RemovalOutcome.Changed:
                await resp.WriteStringAsync("The file has changed since it was scanned, so it was left alone. Scan again.", ct);
                resp.StatusCode = HttpStatusCode.Conflict;
                return resp;
            case RemovalOutcome.LastCopy:
                await resp.WriteStringAsync("Not removed: the file it is a copy of is no longer there as it was scanned, so this may be the only copy left. Scan again.", ct);
                resp.StatusCode = HttpStatusCode.Conflict;
                return resp;
            case RemovalOutcome.Failed:
                // Most often the file is open in another program: nobody's mistake, and it can be tried again.
                await resp.WriteStringAsync(result.Error!, ct);
                resp.StatusCode = HttpStatusCode.Conflict;
                return resp;
        }
        _log.Log("audit","Info",$"Removed photo {gid} physical={physical}");
        // The photo has gone; what was done is recorded whatever happens.
        await _audit.AddAsync(new AuditEntry { Action = "Delete", PhotoId = gid.ToString(), Details = result.HeldAt is null ? "logical" : $"moved to {result.HeldAt} with {result.Companions.Count} linked files" }, CancellationToken.None);
        await resp.WriteAsJsonAsync(new { heldAt = result.HeldAt, companions = result.Companions.Count, batchId = result.BatchId?.ToString() }, ct);
        return resp;
    }

    /// <summary>POST /api/photos/{id}/keep?kept=: marks a photo to keep, so no bulk removal takes it; kept=false takes the mark off.</summary>
    public async Task<ApiResponse> KeepPhotoAsync(ApiRequest req, string id, CancellationToken ct)
    {
        var resp = req.CreateResponse();
        if (!RequestGuard.Allows(req.Headers)) { resp.StatusCode = HttpStatusCode.Unauthorized; return resp; }
        var gid = Guid.Parse(id);
        var kept = System.Web.HttpUtility.ParseQueryString(req.Url.Query).Get("kept") != "false";
        var photo = await _repo.GetAsync(new PhotoId(gid), ct);
        if (photo == null) { resp.StatusCode = HttpStatusCode.NotFound; return resp; }
        if (photo.IsKept != kept)
        {
            photo.IsKept = kept;
            await _repo.AddOrUpdateAsync(photo, ct);
            _log.Log("audit","Info",$"{(kept ? "Kept" : "Unkept")} photo {gid}");
            // The mark is made; what was done is recorded whatever happens.
            await _audit.AddAsync(new AuditEntry { Action = kept ? "Keep" : "Unkeep", PhotoId = gid.ToString(), Details = "" }, CancellationToken.None);
        }
        resp.StatusCode = HttpStatusCode.NoContent;
        return resp;
    }

    /// <summary>
    /// POST /api/photos/bulk/remove-duplicates: add ?group=key for one group, once for each of several, or name them in
    /// the body as { "groups": [...] } when there are more than an address holds; &amp;mode=similar for their look-alikes.
    /// </summary>
    public async Task<ApiResponse> RemoveDuplicatesAsync(ApiRequest req, CancellationToken ct)
    {
        var resp = req.CreateResponse();
        if (!RequestGuard.Allows(req.Headers)) { resp.StatusCode = HttpStatusCode.Unauthorized; return resp; }
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var body = await new StreamReader(req.Body).ReadToEndAsync(ct);
        RemoveGroupsRequest? named = null;
        if (body.Length > 0)
        {
            named = ApiRequest.FromJson<RemoveGroupsRequest>(body, Json);
            if (named?.Groups is null)
            {
                await resp.WriteStringAsync("The groups to remove could not be read.", ct);
                resp.StatusCode = HttpStatusCode.BadRequest;
                return resp;
            }
        }
        // No group, or only blank ones, means every group.
        var groups = (query.GetValues("group") ?? []).Concat(named?.Groups ?? []).Where(g => !string.IsNullOrWhiteSpace(g)).ToList();
        var group = groups.Count == 1 ? groups[0] : null;
        var similar = string.Equals(query.Get("mode"), "similar", StringComparison.OrdinalIgnoreCase);
        if (similar && groups.Count == 0)
        {
            await resp.WriteStringAsync("Similar shots are removed one group at a time: name the group.", ct);
            resp.StatusCode = HttpStatusCode.BadRequest;
            return resp;
        }
        var result = groups.Count > 1 ? await _remover.RemoveGroupsAsync(groups, similar, ct) : await _remover.RemoveDuplicatesAsync(group, similar, ct);
        var what = similar ? "similar shots" : "duplicates";
        _log.Log("audit", "Info", $"Removed {result.Removed} {what} ({result.Bytes} bytes) and {result.Companions} linked files, skipped {result.Skipped}");
        // The files have gone; what was done is recorded whatever happens.
        await _audit.AddAsync(new AuditEntry { Action = similar ? "RemoveSimilar" : "RemoveDuplicates", PhotoId = group, Details = $"removed={result.Removed} bytes={result.Bytes} linked={result.Companions} skipped={result.Skipped}{(groups.Count > 1 ? $" groups={groups.Count}" : "")}" }, CancellationToken.None);
        await resp.WriteAsJsonAsync(new BulkRemovalResultDto { Removed = result.Removed, Bytes = result.Bytes, Skipped = result.Skipped, Companions = result.Companions, Problems = result.Problems, BatchId = result.BatchId?.ToString() }, ct);
        return resp;
    }
}
