using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PhotoSense.Domain.Repositories;
using System.Net;
using PhotoSense.Application.Photos.Interfaces;
using PhotoSense.Domain.Entities;

namespace PhotoSense.Functions.Scanning;

public class DuplicateGroupsFunctions
{
    private readonly IPhotoRepository _repo;

    public DuplicateGroupsFunctions(IPhotoRepository repo)
    {
        _repo = repo;
    }

    [Function("GetDuplicateGroups")] // GET /api/scan/groups
    public async Task<HttpResponseData> GetDuplicateGroups(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "scan/groups")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var q = query.Get("q") ?? string.Empty;
        var near = bool.TryParse(query.Get("near"), out var n) && n;
        var photos = await _repo.GetAllAsync();
        // naive grouping by content hash or near-duplicate predicate placeholder
        IEnumerable<IGrouping<string, PhotoSense.Domain.Entities.Photo>> groups = near
            ? photos.GroupBy(p => p.PerceptualHash ?? p.ContentHash ?? p.Id.ToString())
            : photos.Where(p => !string.IsNullOrEmpty(p.ContentHash)).GroupBy(p => p.ContentHash!);
        if (!string.IsNullOrWhiteSpace(q))
            groups = groups.Where(g => g.Any(p => (p.FileName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)));

        var payload = groups
            .Where(g => g.Count() > 1)
            .Take(500)
            .Select(g => new
            {
                key = g.Key,
                photos = g.Select(p => new {
                    id = p.Id,
                    fileName = p.FileName,
                    sourcePath = p.SourcePath,
                    fileSizeBytes = p.FileSizeBytes,
                    contentHash = p.ContentHash,
                    perceptualHash = p.PerceptualHash,
                    takenOn = p.TakenOn,
                    cameraModel = p.CameraModel,
                    set = p.Set.ToString()
                }).ToList(),
                perceptual = near
            });

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(payload);
        return resp;
    }
}
