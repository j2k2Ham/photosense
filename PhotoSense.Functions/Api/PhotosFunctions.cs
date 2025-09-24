using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Application.Photos.Interfaces;
using System.Web;
using PhotoSense.Application.Scanning.Interfaces;

namespace PhotoSense.Functions.Api;

public class PhotosFunctions
{
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
        IPhotoDeletionService deleter)
    {
        var resp = req.CreateResponse();
        var physical = System.Web.HttpUtility.ParseQueryString(req.Url.Query).Get("physical") == "true";
        if (!Guid.TryParse(id, out var gid))
        {
            resp.StatusCode = HttpStatusCode.BadRequest;
            await resp.WriteStringAsync("Invalid id");
            return resp;
        }
        await deleter.DeleteAsync(new PhotoId(gid), physical);
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
}