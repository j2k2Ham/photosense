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

namespace PhotoSense.Functions.Api;

public class PhotosFunctions
{
    [Function("GetAudit")]
    public async Task<HttpResponseData> GetAuditAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "audit")] HttpRequestData req,
        IAuditRepository audit)
    {
        var resp = req.CreateResponse(HttpStatusCode.OK);
        var items = await audit.RecentAsync();
        await resp.WriteAsJsonAsync(items.Select(a => new { a.UtcTimestamp, a.Action, a.PhotoId, a.Details }));
        return resp;
    }
    [Function("GetPhotos")]
    public async Task<HttpResponseData> GetPhotosAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photos")] HttpRequestData req,
        IPhotoSearchService search)
    {
        var qs = HttpUtility.ParseQueryString(req.Url.Query);
        int page = int.TryParse(qs.Get("page"), out var p) ? p : 1;
        int pageSize = int.TryParse(qs.Get("pageSize"), out var ps) ? Math.Clamp(ps,1,500) : 50;
        var text = qs.Get("text");
        var hash = qs.Get("hash");
        var phash = qs.Get("phash");
        var set = qs.Get("set");
        var result = await search.SearchAsync(new PhotoSearchQuery(page, pageSize, text, hash, phash, set));
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new {
            result.Page,
            result.PageSize,
            result.TotalCount,
            result.TotalPages,
            items = result.Items.Select(p => new {
                id = p.Id.Value,
                p.FileName,
                p.SourcePath,
                p.FileSizeBytes,
                p.ContentHash,
                p.PerceptualHash,
                p.TakenOn,
                p.CameraModel,
                p.Latitude,
                p.Longitude,
                set = p.Set.ToString()
            })
        });
        return resp;
    }

    [Function("GetPhotoById")]
    public async Task<HttpResponseData> GetPhotoAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "photos/{id:guid}")] HttpRequestData req,
        string id,
        IPhotoQueryService query)
    {
        var resp = req.CreateResponse();
        if (!Guid.TryParse(id, out var gid))
        {
            resp.StatusCode = HttpStatusCode.BadRequest;
            await resp.WriteStringAsync("Invalid id");
            return resp;
        }
        var photo = await query.GetAsync(new PhotoId(gid));
        if (photo is null)
        {
            resp.StatusCode = HttpStatusCode.NotFound;
            return resp;
        }
        resp.StatusCode = HttpStatusCode.OK;
        await resp.WriteAsJsonAsync(photo);
        return resp;
    }

    [Function("DeletePhoto")]
    public async Task<HttpResponseData> DeletePhotoAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "photos/{id:guid}")] HttpRequestData req,
        string id,
        IPhotoDeletionService deleter,
        IScanLogSink? logSink,
        IAuditRepository audit)
    {
        var resp = req.CreateResponse();
        if (!Authorize(req)) { resp.StatusCode = HttpStatusCode.Unauthorized; return resp; }
        var physical = System.Web.HttpUtility.ParseQueryString(req.Url.Query).Get("physical") == "true";
        if (!Guid.TryParse(id, out var gid))
        {
            resp.StatusCode = HttpStatusCode.BadRequest;
            await resp.WriteStringAsync("Invalid id");
            return resp;
        }
        await deleter.DeleteAsync(new PhotoId(gid), physical);
        logSink?.Log("audit","Info",$"Deleted photo {gid} physical={physical}");
        await audit.AddAsync(new AuditEntry { Action = "Delete", PhotoId = gid.ToString(), Details = physical?"physical":"logical" });
        resp.StatusCode = HttpStatusCode.NoContent;
        return resp;
    }

    [Function("GetDuplicateGroups")]
    public async Task<HttpResponseData> GetDuplicateGroupsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "duplicates")] HttpRequestData req,
        IDuplicateGroupingService dups)
    {
        var groups = await dups.GetDuplicateGroupsAsync();
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(groups.Select(g => new
        {
            g.Hash,
            count = g.Photos.Count,
            photos = g.Photos.Select(p => new { id = p.Id.Value, p.FileName, p.SourcePath })
        }));
        return resp;
    }

    [Function("GetNearDuplicateGroups")]
    public async Task<HttpResponseData> GetNearDuplicateGroupsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "near-duplicates")] HttpRequestData req,
        INearDuplicateService near)
    {
        var groups = await near.GetNearDuplicatesAsync();
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(groups.Select(g => new
        {
            g.RepresentativeHash,
            count = g.Photos.Count,
            photos = g.Photos.Select(p => new { id = p.Id.Value, p.FileName, p.SourcePath, p.PerceptualHash })
        }));
        return resp;
    }

    [Function("KeepPhoto")]
    public async Task<HttpResponseData> KeepPhotoAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photos/{id:guid}/keep")] HttpRequestData req,
        string id,
        IPhotoRepository repo,
        IScanLogSink? logSink,
        IAuditRepository audit)
    {
        var resp = req.CreateResponse();
        if (!Authorize(req)) { resp.StatusCode = HttpStatusCode.Unauthorized; return resp; }
        if (!Guid.TryParse(id, out var gid)) { resp.StatusCode = HttpStatusCode.BadRequest; return resp; }
        var photo = await repo.GetAsync(new PhotoId(gid));
        if (photo == null) { resp.StatusCode = HttpStatusCode.NotFound; return resp; }
        if (!photo.IsKept)
        {
            photo.IsKept = true;
            await repo.AddOrUpdateAsync(photo);
            logSink?.Log("audit","Info",$"Kept photo {gid}");
            await audit.AddAsync(new AuditEntry { Action = "Keep", PhotoId = gid.ToString(), Details = "" });
        }
        resp.StatusCode = HttpStatusCode.NoContent;
        return resp;
    }

    [Function("MovePhoto")]
    public async Task<HttpResponseData> MovePhotoAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "photos/{id:guid}/move")] HttpRequestData req,
        string id,
        IPhotoQueryService query,
        IPhotoRepository repo,
        IScanLogSink? logSink,
        IAuditRepository audit)
    {
        var resp = req.CreateResponse();
        if (!Authorize(req)) { resp.StatusCode = HttpStatusCode.Unauthorized; return resp; }
        if (!Guid.TryParse(id, out var gid)) { resp.StatusCode = HttpStatusCode.BadRequest; return resp; }
        var qs = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var target = qs.Get("target");
        if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target)) { resp.StatusCode = HttpStatusCode.BadRequest; await resp.WriteStringAsync("Invalid target"); return resp; }
        var photo = await query.GetAsync(new PhotoId(gid));
        if (photo == null) { resp.StatusCode = HttpStatusCode.NotFound; return resp; }
        var source = photo.SourcePath;
        var newPath = Path.Combine(target, photo.FileName);
        try
        {
            File.Move(source, newPath, true);
            var updated = new Photo
            {
                Id = photo.Id,
                SourcePath = newPath,
                FileName = photo.FileName,
                FileSizeBytes = photo.FileSizeBytes,
                ContentHash = photo.ContentHash,
                PerceptualHash = photo.PerceptualHash,
                TakenOn = photo.TakenOn,
                CameraModel = photo.CameraModel,
                Latitude = photo.Latitude,
                Longitude = photo.Longitude,
                Set = photo.Set,
                IsKept = photo.IsKept
            };
            await repo.AddOrUpdateAsync(updated);
            logSink?.Log("audit","Info",$"Moved photo {gid} to {target}");
            await audit.AddAsync(new AuditEntry { Action = "Move", PhotoId = gid.ToString(), Details = target });
            resp.StatusCode = HttpStatusCode.NoContent;
        }
        catch (Exception ex)
        {
            resp.StatusCode = HttpStatusCode.InternalServerError;
            await resp.WriteStringAsync(ex.Message);
        }
        return resp;
    }

    private static bool Authorize(HttpRequestData req)
    {
        // Simple API key check (e.g., x-api-key header). For production replace with proper auth.
        if (!req.Headers.TryGetValues("x-api-key", out var vals)) return false;
        var key = vals.FirstOrDefault();
        var expected = Environment.GetEnvironmentVariable("PHOTOSENSE_API_KEY");
        if (string.IsNullOrEmpty(expected)) return true; // if not set, allow
        return key == expected;
    }
}