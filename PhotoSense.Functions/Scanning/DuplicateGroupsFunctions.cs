using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Entities;

namespace PhotoSense.Functions.Scanning;

public sealed class ScanGroupingFacade
{
    private readonly IDuplicateGroupingService _dups;
    private readonly INearDuplicateService _near;
    public ScanGroupingFacade(IDuplicateGroupingService dups, INearDuplicateService near) { _dups = dups; _near = near; }

    public async Task<object> BuildAsync(bool near, int threshold, string? q, int page, int pageSize, CancellationToken ct)
    {
        if (!near)
        {
            var groups = await _dups.GetDuplicateGroupsAsync(ct);
            if (!string.IsNullOrWhiteSpace(q))
                groups = groups.Where(g => g.Photos.Any(p => p.FileName.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
            var total = groups.Count;
            var pageItems = groups.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return new
            {
                mode = "exact",
                page,
                pageSize,
                total,
                totalPages = (int)Math.Ceiling(total / (double)pageSize),
                items = pageItems.Select(g => new
                {
                    key = g.Hash,
                    photos = g.Photos.Select(MapPhoto).ToList(),
                    perceptual = false
                })
            };
        }
        else
        {
            var groups = await _near.GetNearDuplicatesAsync(threshold, ct);
            if (!string.IsNullOrWhiteSpace(q))
                groups = groups.Where(g => g.Photos.Any(p => p.FileName.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
            var total = groups.Count;
            var pageItems = groups.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return new
            {
                mode = "near",
                threshold,
                page,
                pageSize,
                total,
                totalPages = (int)Math.Ceiling(total / (double)pageSize),
                items = pageItems.Select(g => new
                {
                    key = g.RepresentativeHash,
                    photos = g.Photos.Select(MapPhoto).ToList(),
                    perceptual = true,
                    distance = g.Photos.Max(p => Hamming(g.RepresentativeHash, p.PerceptualHash ?? g.RepresentativeHash))
                })
            };
        }
    }

    private static int Hamming(string a, string b)
    {
        if (a.Length != b.Length) return int.MaxValue;
        int d = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) d++;
        }
        return d;
    }

    private static object MapPhoto(PhotoSense.Domain.Entities.Photo p) => new
    {
        id = p.Id,
        fileName = p.FileName,
        sourcePath = p.SourcePath,
        fileSizeBytes = p.FileSizeBytes,
        contentHash = p.ContentHash,
        perceptualHash = p.PerceptualHash,
        takenOn = p.TakenOn,
        cameraModel = p.CameraModel,
        set = p.Set.ToString()
    };
}

public class DuplicateGroupsFunctions
{
    private readonly ScanGroupingFacade _facade;
    public DuplicateGroupsFunctions(ScanGroupingFacade facade) => _facade = facade;

    [Function("GetDuplicateGroupsUnified")] // GET /api/scan/groups
    public async Task<HttpResponseData> GetDuplicateGroups(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "scan/groups")] HttpRequestData req)
    {
        var q = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        bool near = bool.TryParse(q.Get("near"), out var n) && n;
        int threshold = int.TryParse(q.Get("threshold"), out var t) ? Math.Clamp(t, 0, 32) : 12;
        int page = int.TryParse(q.Get("page"), out var p) ? Math.Max(1, p) : 1;
        int pageSize = int.TryParse(q.Get("pageSize"), out var ps) ? Math.Clamp(ps, 1, 200) : 100;
        var text = q.Get("q");
        var payload = await _facade.BuildAsync(near, threshold, text, page, pageSize, CancellationToken.None);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(payload);
        return resp;
    }
}

public class ScanLogsStubFunction
{
    [Function("GetScanLogs")] // GET /api/scan/logs
    public async Task<HttpResponseData> GetLogs([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "scan/logs")] HttpRequestData req)
    {
        var resp = req.CreateResponse(HttpStatusCode.OK);
        var now = DateTime.UtcNow;
        await resp.WriteAsJsonAsync(new[] {
            new { ts = now.AddSeconds(-5), level = "Info", message = "Scan subsystem idle." },
            new { ts = now.AddSeconds(-1), level = "Info", message = "No active scan." }
        });
        return resp;
    }
}
