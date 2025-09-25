using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Entities;
using System.Security.Cryptography;
using System.Text;

namespace PhotoSense.Functions.Scanning;

public sealed class ScanGroupingFacade
{
    private readonly IDuplicateGroupingService _dups;
    private readonly INearDuplicateService _near;
    public ScanGroupingFacade(IDuplicateGroupingService dups, INearDuplicateService near) { _dups = dups; _near = near; }

    public async Task<object> BuildAsync(bool near, int threshold, string? q, bool hideKept, int page, int pageSize, CancellationToken ct)
    {
        if (!near)
        {
            var groups = await _dups.GetDuplicateGroupsAsync(ct);
            if (!string.IsNullOrWhiteSpace(q))
                groups = groups.Where(g => g.Photos.Any(p => p.FileName.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
            if (hideKept)
                groups = groups.Select(g => new PhotoSense.Domain.DTOs.DuplicateGroup(g.Hash, g.Photos.Where(p=>!p.IsKept).ToList()))
                               .Where(g=>g.Photos.Count>0).ToList();
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
            if (hideKept)
                groups = groups.Select(g => new PhotoSense.Domain.DTOs.NearDuplicateGroup(g.RepresentativeHash, g.Photos.Where(p=>!p.IsKept).ToList()))
                               .Where(g=>g.Photos.Count>0).ToList();
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
        set = p.Set.ToString(),
        kept = p.IsKept
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
        var hideKept = q.Get("hideKept") == "true";
        var payload = await _facade.BuildAsync(near, threshold, text, hideKept, page, pageSize, CancellationToken.None);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        // Compute weak ETag for basic caching
        try
        {
            var itemsProp = payload.GetType().GetProperty("items")?.GetValue(payload) as IEnumerable<object>;
            var sb = new StringBuilder();
            if (itemsProp != null)
            {
                foreach (var it in itemsProp.Take(5))
                {
                    var keyVal = it.GetType().GetProperty("key")?.GetValue(it)?.ToString();
                    if (keyVal != null) sb.Append(keyVal).Append('|');
                }
            }
            using var md5 = MD5.Create();
            var hash = Convert.ToHexString(md5.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
            resp.Headers.Add("ETag", $"W/\"{(near?"near":"exact")}-{page}-{hash}\"");
        }
        catch { /* non-fatal */ }
        await resp.WriteAsJsonAsync(payload);
        return resp;
    }
}

public class ScanLogsStubFunction
{
    // Simple JSON list endpoint retained
    [Function("GetScanLogs")] 
    public async Task<HttpResponseData> GetLogs([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "scan/logs/{instanceId?}")] HttpRequestData req, string? instanceId, IScanLogSink sink, IScanProgressStore progress)
    {
        var snap = progress.GetLatest();
        var id = string.IsNullOrWhiteSpace(instanceId) ? snap.InstanceId : instanceId;
        var resp = req.CreateResponse(HttpStatusCode.OK);
        DateTime? since = null;
        var qs = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var sinceRaw = qs.Get("since");
    if (DateTime.TryParse(sinceRaw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed)) since = parsed.ToUniversalTime();
        var lines = string.IsNullOrWhiteSpace(id)
            ? new List<object>()
            : sink.GetRecent(id).Where(l => !since.HasValue || l.ts > since.Value)
                .Select(l => (object)new { l.ts, l.level, l.message }).ToList();
        await resp.WriteAsJsonAsync(lines);
        return resp;
    }

    // Basic SSE stream; in production you might bridge to SignalR or Service Bus
    [Function("GetScanLogsStream")] // GET /api/scan/logs/stream
    public async Task<HttpResponseData> GetLogsStream([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "scan/logs/stream/{instanceId?}")] HttpRequestData req, string? instanceId, IScanLogSink sink, IScanProgressStore progress)
    {
        var snap = progress.GetLatest();
        var id = string.IsNullOrWhiteSpace(instanceId) ? snap.InstanceId : instanceId;
        var resp = req.CreateResponse(System.Net.HttpStatusCode.OK);
        resp.Headers.Add("Content-Type", "text/event-stream");
        if (string.IsNullOrWhiteSpace(id)) { await resp.WriteStringAsync("event: message\ndata: No active scan\n\n"); return resp; }
        var qs = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        bool follow = bool.TryParse(qs.Get("follow"), out var f) && f;
        var recent = sink.GetRecent(id).Select(l => $"event: log\ndata: {l.ts:o} {l.level} {l.message}\n\n");
        await resp.WriteStringAsync(string.Join(string.Empty, recent));
        if (follow)
        {
            var start = DateTime.UtcNow;
            var lastCount = sink.GetRecent(id).Count;
            while (DateTime.UtcNow - start < TimeSpan.FromSeconds(30))
            {
                await Task.Delay(2000);
                var nowLogs = sink.GetRecent(id);
                if (nowLogs.Count > lastCount)
                {
                    foreach (var l in nowLogs.Skip(lastCount))
                        await resp.WriteStringAsync($"event: log\ndata: {l.ts:o} {l.level} {l.message}\n\n");
                    lastCount = nowLogs.Count;
                }
            }
            await resp.WriteStringAsync("event: end\ndata: stream closed\n\n");
        }
        return resp;
    }
}
