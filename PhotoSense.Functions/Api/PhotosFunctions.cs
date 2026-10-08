using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Application.Photos.Interfaces;
using System.Web;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Entities;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Contracts.Duplicates;
using PhotoSense.Functions.Scanning;

namespace PhotoSense.Functions.Api;

public class PhotosFunctions
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
    private readonly IPhotoSearchService _search;
    private readonly IDuplicateRemovalService _remover;
    private readonly IThumbnailStore _thumbnails;
    private readonly IImageAnalyzer _analyzer;
    private readonly IAuditRepository _audit;
    private readonly IScanLogSink _log;
    private readonly PhotoDtoMapper _mapper;
    private readonly ISystemViewer _viewer;

    // Services arrive through the constructor: this Functions model does not pass them as method parameters.
    public PhotosFunctions(IPhotoRepository repo, IPhotoSearchService search, IDuplicateRemovalService remover,
        IThumbnailStore thumbnails, IImageAnalyzer analyzer, IAuditRepository audit, IScanLogSink log, PhotoDtoMapper mapper, ISystemViewer viewer)
    {
        _mapper = mapper;
        _viewer = viewer;
        _repo = repo; _search = search; _remover = remover;
        _thumbnails = thumbnails; _analyzer = analyzer; _audit = audit; _log = log;
    }

    [Function("GetAudit")]
    public async Task<HttpResponseData> GetAuditAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "audit")] HttpRequestData req)
    {
        var resp = req.CreateResponse(HttpStatusCode.OK);
        if (!Authorize(req)) { resp.StatusCode = HttpStatusCode.Unauthorized; return resp; }
        var items = await _audit.RecentAsync();
        await resp.WriteAsJsonAsync(items.Select(a => new { a.UtcTimestamp, a.Action, a.PhotoId, a.Details }));
        return resp;
    }

    [Function("GetPhotos")]
    public async Task<HttpResponseData> GetPhotosAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photos")] HttpRequestData req)
    {
        var qs = HttpUtility.ParseQueryString(req.Url.Query);
        int page = int.TryParse(qs.Get("page"), out var p) ? p : 1;
        int pageSize = int.TryParse(qs.Get("pageSize"), out var ps) ? Math.Clamp(ps,1,500) : 50;
        var text = qs.Get("text");
        var hash = qs.Get("hash");
        var phash = qs.Get("phash");
        var set = qs.Get("set");
        var result = await _search.SearchAsync(new PhotoSearchQuery(page, pageSize, text, hash, phash, set));
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new {
            result.Page,
            result.PageSize,
            result.TotalCount,
            result.TotalPages,
            items = result.Items.Select(_mapper.Map)
        });
        return resp;
    }

    [Function("GetPhotoById")]
    public async Task<HttpResponseData> GetPhotoAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photos/{id:guid}")] HttpRequestData req,
        string id)
    {
        var resp = req.CreateResponse();
        var photo = await _repo.GetAsync(new PhotoId(Guid.Parse(id)));
        if (photo is null)
        {
            resp.StatusCode = HttpStatusCode.NotFound;
            return resp;
        }
        await resp.WriteAsJsonAsync(_mapper.Map(photo));
        return resp;
    }

    [Function("GetPhotoThumbnail")]
    public async Task<HttpResponseData> GetThumbnailAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photos/{id:guid}/thumbnail")] HttpRequestData req,
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
                await using var stream = File.OpenRead(photo.SourcePath);
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

    [Function("GetPhotoImage")]
    public async Task<HttpResponseData> GetImageAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photos/{id:guid}/image")] HttpRequestData req,
        string id)
    {
        var photo = await _repo.GetAsync(new PhotoId(Guid.Parse(id)));
        if (photo is null || photo.IsVideo || !File.Exists(photo.SourcePath)) return req.CreateResponse(HttpStatusCode.NotFound);

        try
        {
            if (BrowserFormats.TryGetValue(Path.GetExtension(photo.SourcePath), out var contentType))
                return await ImageResponseAsync(req, await File.ReadAllBytesAsync(photo.SourcePath), contentType, photo.ContentHash);

            byte[] jpeg;
            await using (var stream = File.OpenRead(photo.SourcePath))
                jpeg = await _analyzer.RenderJpegAsync(stream, ReviewImageEdge);
            return await ImageResponseAsync(req, jpeg, "image/jpeg", photo.ContentHash);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failed = req.CreateResponse(HttpStatusCode.UnprocessableEntity);
            await failed.WriteStringAsync($"This file could not be shown: {ex.Message}");
            return failed;
        }
    }

    [Function("GetPhotoVideo")] // GET /api/photos/{id}/video, a piece at a time
    public async Task<HttpResponseData> GetVideoAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photos/{id:guid}/video")] HttpRequestData req,
        string id)
    {
        var photo = await _repo.GetAsync(new PhotoId(Guid.Parse(id)));
        if (photo is null || !photo.IsVideo || !File.Exists(photo.SourcePath)) return req.CreateResponse(HttpStatusCode.NotFound);

        await using var file = new FileStream(photo.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
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

    [Function("OpenPhoto")] // POST /api/photos/{id}/open
    public async Task<HttpResponseData> OpenPhotoAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photos/{id:guid}/open")] HttpRequestData req,
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
    private static async Task<HttpResponseData> ImageResponseAsync(HttpRequestData req, byte[] bytes, string contentType, string? contentHash)
    {
        var resp = req.CreateResponse(HttpStatusCode.OK);
        resp.Headers.Add("Content-Type", contentType);
        resp.Headers.Add("Cache-Control", "private, max-age=86400");
        if (contentHash is not null) resp.Headers.Add("ETag", $"\"{contentHash}\"");
        await resp.Body.WriteAsync(bytes);
        return resp;
    }

    [Function("DeletePhoto")]
    public async Task<HttpResponseData> DeletePhotoAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "photos/{id:guid}")] HttpRequestData req,
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
                await resp.WriteStringAsync($"The file could not be moved: {result.Error}");
                resp.StatusCode = HttpStatusCode.InternalServerError;
                return resp;
        }
        _log.Log("audit","Info",$"Removed photo {gid} physical={physical}");
        await _audit.AddAsync(new AuditEntry { Action = "Delete", PhotoId = gid.ToString(), Details = result.HeldAt is null ? "logical" : $"moved to {result.HeldAt} with {result.Companions.Count} linked files" });
        await resp.WriteAsJsonAsync(new { heldAt = result.HeldAt, companions = result.Companions.Count });
        return resp;
    }

    [Function("KeepPhoto")] // POST /api/photos/{id}/keep  (add ?kept=false to undo)
    public async Task<HttpResponseData> KeepPhotoAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photos/{id:guid}/keep")] HttpRequestData req,
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

    [Function("MovePhoto")]
    public async Task<HttpResponseData> MovePhotoAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photos/{id:guid}/move")] HttpRequestData req,
        string id)
    {
        var resp = req.CreateResponse();
        if (!Authorize(req)) { resp.StatusCode = HttpStatusCode.Unauthorized; return resp; }
        var gid = Guid.Parse(id);
        var qs = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var target = qs.Get("target");
        if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target)) { resp.StatusCode = HttpStatusCode.BadRequest; await resp.WriteStringAsync("Invalid target"); return resp; }
        var photo = await _repo.GetAsync(new PhotoId(gid));
        if (photo == null) { resp.StatusCode = HttpStatusCode.NotFound; return resp; }
        var newPath = Path.Combine(target, photo.FileName);
        try
        {
            // No overwrite: a file already at the target is someone else's picture.
            File.Move(photo.SourcePath, newPath);
            await _repo.AddOrUpdateAsync(photo.MovedTo(newPath));
            _log.Log("audit","Info",$"Moved photo {gid} to {target}");
            await _audit.AddAsync(new AuditEntry { Action = "Move", PhotoId = gid.ToString(), Details = target });
            resp.StatusCode = HttpStatusCode.NoContent;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await resp.WriteStringAsync(ex.Message);
            resp.StatusCode = HttpStatusCode.Conflict;
        }
        return resp;
    }

    [Function("RemoveDuplicates")] // POST /api/photos/bulk/remove-duplicates  (add ?group=key for one group, and &mode=similar for its look-alikes)
    public async Task<HttpResponseData> RemoveDuplicatesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photos/bulk/remove-duplicates")] HttpRequestData req)
    {
        var resp = req.CreateResponse();
        if (!Authorize(req)) { resp.StatusCode = HttpStatusCode.Unauthorized; return resp; }
        // No group, or a blank one, means every group.
        var group = System.Web.HttpUtility.ParseQueryString(req.Url.Query).Get("group");
        if (string.IsNullOrWhiteSpace(group)) group = null;
        var similar = string.Equals(System.Web.HttpUtility.ParseQueryString(req.Url.Query).Get("mode"), "similar", StringComparison.OrdinalIgnoreCase);
        if (similar && group is null)
        {
            await resp.WriteStringAsync("Similar shots are removed one group at a time: name the group.");
            resp.StatusCode = HttpStatusCode.BadRequest;
            return resp;
        }
        var result = await _remover.RemoveDuplicatesAsync(group, similar);
        var what = similar ? "similar shots" : "duplicates";
        _log.Log("audit", "Info", $"Removed {result.Removed} {what} ({result.Bytes} bytes) and {result.Companions} linked files, skipped {result.Skipped}");
        await _audit.AddAsync(new AuditEntry { Action = similar ? "RemoveSimilar" : "RemoveDuplicates", PhotoId = group, Details = $"removed={result.Removed} bytes={result.Bytes} linked={result.Companions} skipped={result.Skipped}" });
        await resp.WriteAsJsonAsync(new BulkRemovalResultDto { Removed = result.Removed, Bytes = result.Bytes, Skipped = result.Skipped, Companions = result.Companions, Problems = result.Problems });
        return resp;
    }

    private static bool Authorize(HttpRequestData req)
        => RequestGuard.Allows(req.Headers);
}
