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

public class PhotosEndpoints
{
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

    public PhotosEndpoints(IPhotoRepository repo, IDuplicateRemovalService remover, IThumbnailStore thumbnails, IImageAnalyzer analyzer,
        IAuditRepository audit, IScanLogSink log, ISystemViewer viewer, IPhotoFileSystem files)
    {
        _viewer = viewer;
        _files = files;
        _repo = repo; _remover = remover;
        _thumbnails = thumbnails; _analyzer = analyzer; _audit = audit; _log = log;
    }

    public async Task<ApiResponse> GetThumbnailAsync(
        ApiRequest req,
        string id)
    {
        var photo = await _repo.GetAsync(new PhotoId(Guid.Parse(id)));
        // Videos are never decoded, so they have no picture to show.
        if (photo?.ContentHash is null || photo.IsVideo) return req.CreateResponse(HttpStatusCode.NotFound);

        var jpeg = await _thumbnails.GetAsync(photo.ContentHash);
        if (jpeg is null)
        {
            // The cache was cleared since the scan: make the thumbnail again from the file.
            try
            {
                await using var stream = _files.OpenRead(photo.SourcePath);
                jpeg = (await _analyzer.AnalyzeAsync(stream)).ThumbnailJpeg;
                await _thumbnails.SaveAsync(photo.ContentHash, jpeg);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return req.CreateResponse(HttpStatusCode.NotFound);
            }
        }
        return await ImageResponseAsync(req, jpeg, "image/jpeg", photo.ContentHash);
    }

    public async Task<ApiResponse> GetImageAsync(
        ApiRequest req,
        string id)
    {
        var photo = await _repo.GetAsync(new PhotoId(Guid.Parse(id)));
        if (photo is null || photo.IsVideo || !_files.FileExists(photo.SourcePath)) return req.CreateResponse(HttpStatusCode.NotFound);
        return await ShowImageAsync(req, _files, photo.SourcePath, photo.ContentHash, _analyzer);
    }

    /// <summary>The picture in a file, in a form a browser can show at full size.</summary>
    public static async Task<ApiResponse> ShowImageAsync(ApiRequest req, IPhotoFileSystem files, string path, string? contentHash, IImageAnalyzer analyzer)
    {
        try
        {
            // What browsers can show is sent as it is; anything else is converted for viewing.
            var asItIs = BrowserFormats.TryGetValue(Path.GetExtension(path), out var contentType);
            byte[] bytes;
            await using (var stream = files.OpenRead(path))
            {
                if (asItIs) await stream.ReadExactlyAsync(bytes = new byte[stream.Length]);
                else bytes = await analyzer.RenderJpegAsync(stream, ReviewImageEdge);
            }
            return await ImageResponseAsync(req, bytes, contentType ?? "image/jpeg", contentHash);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failed = req.CreateResponse(HttpStatusCode.UnprocessableEntity);
            await failed.WriteStringAsync($"This file could not be shown: {ex.Message}");
            return failed;
        }
    }

    // GET /api/photos/{id}/video, a piece at a time
    public async Task<ApiResponse> GetVideoAsync(
        ApiRequest req,
        string id)
    {
        var photo = await _repo.GetAsync(new PhotoId(Guid.Parse(id)));
        if (photo is null || !photo.IsVideo || !_files.FileExists(photo.SourcePath)) return req.CreateResponse(HttpStatusCode.NotFound);

        await using var file = _files.OpenRead(photo.SourcePath);
        var requested = req.Headers.TryGetValues("Range", out var values) ? values.FirstOrDefault() : null;
        if (ByteRange.Parse(requested, file.Length) is not { } range)
        {
            var unsatisfiable = req.CreateResponse(HttpStatusCode.RequestedRangeNotSatisfiable);
            unsatisfiable.Headers.Add("Content-Range", $"bytes */{file.Length}");
            return unsatisfiable;
        }

        var piece = new byte[range.Length];
        file.Position = range.Start;
        await file.ReadExactlyAsync(piece);

        var resp = req.CreateResponse(HttpStatusCode.PartialContent);
        resp.Headers.Add("Content-Type", VideoFormats.GetValueOrDefault(Path.GetExtension(photo.SourcePath), "application/octet-stream"));
        resp.Headers.Add("Accept-Ranges", "bytes");
        resp.Headers.Add("Content-Range", range.ContentRange(file.Length));
        await resp.Body.WriteAsync(piece);
        return resp;
    }

    // POST /api/photos/{id}/open
    public async Task<ApiResponse> OpenPhotoAsync(
        ApiRequest req,
        string id)
    {
        var resp = req.CreateResponse();
        if (!Authorize(req)) { resp.StatusCode = HttpStatusCode.Unauthorized; return resp; }
        var photo = await _repo.GetAsync(new PhotoId(Guid.Parse(id)));
        if (photo is null) { resp.StatusCode = HttpStatusCode.NotFound; return resp; }
        try
        {
            // Opens on the machine this service runs on, which is the machine that holds the photos.
            _viewer.Open(photo.SourcePath);
            resp.StatusCode = HttpStatusCode.NoContent;
        }
        catch (FileNotFoundException)
        {
            await resp.WriteStringAsync("The file is no longer there. Scan again.");
            resp.StatusCode = HttpStatusCode.NotFound;
        }
        catch (InvalidOperationException ex)
        {
            await resp.WriteStringAsync(ex.Message);
            resp.StatusCode = HttpStatusCode.InternalServerError;
        }
        return resp;
    }

    // A file's bytes never change under the same content hash, so the browser may keep what it was sent.
    public static async Task<ApiResponse> ImageResponseAsync(ApiRequest req, byte[] bytes, string contentType, string? contentHash)
    {
        var resp = req.CreateResponse(HttpStatusCode.OK);
        resp.Headers.Add("Content-Type", contentType);
        resp.Headers.Add("Cache-Control", "private, max-age=86400");
        if (contentHash is not null) resp.Headers.Add("ETag", $"\"{contentHash}\"");
        await resp.Body.WriteAsync(bytes);
        return resp;
    }

    public async Task<ApiResponse> DeletePhotoAsync(
        ApiRequest req,
        string id)
    {
        var resp = req.CreateResponse();
        if (!Authorize(req)) { resp.StatusCode = HttpStatusCode.Unauthorized; return resp; }
        var physical = System.Web.HttpUtility.ParseQueryString(req.Url.Query).Get("physical") == "true";
        var gid = Guid.Parse(id);
        var result = await _remover.RemoveAsync(new PhotoId(gid), physical);
        switch (result.Outcome)
        {
            case RemovalOutcome.NotFound:
                resp.StatusCode = HttpStatusCode.NotFound;
                return resp;
            case RemovalOutcome.Changed:
                await resp.WriteStringAsync("The file has changed since it was scanned, so it was left alone. Scan again.");
                resp.StatusCode = HttpStatusCode.Conflict;
                return resp;
            case RemovalOutcome.LastCopy:
                await resp.WriteStringAsync("Not removed: the file it is a copy of is no longer there as it was scanned, so this may be the only copy left. Scan again.");
                resp.StatusCode = HttpStatusCode.Conflict;
                return resp;
            case RemovalOutcome.Failed:
                // Most often the file is open in another program: nobody's mistake, and it can be tried again.
                await resp.WriteStringAsync(result.Error!);
                resp.StatusCode = HttpStatusCode.Conflict;
                return resp;
        }
        _log.Log("audit","Info",$"Removed photo {gid} physical={physical}");
        await _audit.AddAsync(new AuditEntry { Action = "Delete", PhotoId = gid.ToString(), Details = result.HeldAt is null ? "logical" : $"moved to {result.HeldAt} with {result.Companions.Count} linked files" });
        await resp.WriteAsJsonAsync(new { heldAt = result.HeldAt, companions = result.Companions.Count, batchId = result.BatchId?.ToString() });
        return resp;
    }

    // POST /api/photos/{id}/keep  (add ?kept=false to undo)
    public async Task<ApiResponse> KeepPhotoAsync(
        ApiRequest req,
        string id)
    {
        var resp = req.CreateResponse();
        if (!Authorize(req)) { resp.StatusCode = HttpStatusCode.Unauthorized; return resp; }
        var gid = Guid.Parse(id);
        var kept = System.Web.HttpUtility.ParseQueryString(req.Url.Query).Get("kept") != "false";
        var photo = await _repo.GetAsync(new PhotoId(gid));
        if (photo == null) { resp.StatusCode = HttpStatusCode.NotFound; return resp; }
        if (photo.IsKept != kept)
        {
            photo.IsKept = kept;
            await _repo.AddOrUpdateAsync(photo);
            _log.Log("audit","Info",$"{(kept ? "Kept" : "Unkept")} photo {gid}");
            await _audit.AddAsync(new AuditEntry { Action = kept ? "Keep" : "Unkeep", PhotoId = gid.ToString(), Details = "" });
        }
        resp.StatusCode = HttpStatusCode.NoContent;
        return resp;
    }

    // POST /api/photos/bulk/remove-duplicates: add ?group=key for one group, once for each of several, or name
    // them in the body as { "groups": [...] } when there are more than an address holds; &mode=similar for their look-alikes.
    public async Task<ApiResponse> RemoveDuplicatesAsync(
        ApiRequest req)
    {
        var resp = req.CreateResponse();
        if (!Authorize(req)) { resp.StatusCode = HttpStatusCode.Unauthorized; return resp; }
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var body = await new StreamReader(req.Body).ReadToEndAsync();
        RemoveGroupsRequest? named = null;
        if (body.Length > 0)
        {
            try { named = JsonSerializer.Deserialize<RemoveGroupsRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
            catch (JsonException) { /* said below */ }
            if (named?.Groups is null)
            {
                await resp.WriteStringAsync("The groups to remove could not be read.");
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
            await resp.WriteStringAsync("Similar shots are removed one group at a time: name the group.");
            resp.StatusCode = HttpStatusCode.BadRequest;
            return resp;
        }
        var result = groups.Count > 1 ? await _remover.RemoveGroupsAsync(groups, similar) : await _remover.RemoveDuplicatesAsync(group, similar);
        var what = similar ? "similar shots" : "duplicates";
        _log.Log("audit", "Info", $"Removed {result.Removed} {what} ({result.Bytes} bytes) and {result.Companions} linked files, skipped {result.Skipped}");
        await _audit.AddAsync(new AuditEntry { Action = similar ? "RemoveSimilar" : "RemoveDuplicates", PhotoId = group, Details = $"removed={result.Removed} bytes={result.Bytes} linked={result.Companions} skipped={result.Skipped}{(groups.Count > 1 ? $" groups={groups.Count}" : "")}" });
        await resp.WriteAsJsonAsync(new BulkRemovalResultDto { Removed = result.Removed, Bytes = result.Bytes, Skipped = result.Skipped, Companions = result.Companions, Problems = result.Problems, BatchId = result.BatchId?.ToString() });
        return resp;
    }

    private static bool Authorize(ApiRequest req)
        => RequestGuard.Allows(req.Headers);
}
