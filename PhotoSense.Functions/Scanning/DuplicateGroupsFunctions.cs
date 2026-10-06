using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.DTOs;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Services;
using D = PhotoSense.Contracts.Duplicates; // alias for shared duplicate group DTOs

namespace PhotoSense.Functions.Scanning;

/// <summary>Turns a photo record into what the UI shows, including the name of the place it was taken.</summary>
public sealed class PhotoDtoMapper
{
    private readonly IPlaceNameResolver _places;
    public PhotoDtoMapper(IPlaceNameResolver places) => _places = places;

    public D.PhotoItemDto Map(Photo p) => new()
    {
        Id = p.Id.Value,
        FileName = p.FileName,
        SourcePath = p.SourcePath,
        Folder = Path.GetDirectoryName(p.SourcePath) ?? string.Empty,
        FileSizeBytes = p.FileSizeBytes,
        Width = p.Width,
        Height = p.Height,
        Format = p.Format,
        IsVideo = p.IsVideo,
        DurationSeconds = p.DurationSeconds,
        TakenOn = p.TakenOn,
        CameraModel = p.CameraModel,
        Latitude = p.Latitude,
        Longitude = p.Longitude,
        PlaceName = p.Latitude is { } latitude && p.Longitude is { } longitude ? _places.Describe(latitude, longitude) : null,
        Set = p.Set.ToString(),
        Kept = p.IsKept
    };
}

public sealed class ScanGroupingFacade
{
    private readonly IDuplicateAnalysisService _analysis;
    private readonly PhotoDtoMapper _mapper;
    private readonly PhotoRanking _ranking;
    public ScanGroupingFacade(IDuplicateAnalysisService analysis, PhotoDtoMapper mapper, PhotoRanking ranking) { _analysis = analysis; _mapper = mapper; _ranking = ranking; }

    /// <param name="similar">False for duplicates (safe to remove in bulk), true for look-alikes (review only).</param>
    /// <param name="hideKept">Leave out groups in which every member has been marked to keep.</param>
    public async Task<D.DuplicateGroupsPageDto> BuildAsync(bool similar, string? q, bool hideKept, int page, int pageSize, CancellationToken ct)
    {
        var analysis = await _analysis.GetAsync(ct);
        IEnumerable<DuplicateGroup> groups = similar ? analysis.Similar : analysis.Duplicates;
        if (!string.IsNullOrWhiteSpace(q))
            groups = groups.Where(g => g.Members.Select(m => m.Photo).Prepend(g.Keeper).Any(p => p.SourcePath.Contains(q, StringComparison.OrdinalIgnoreCase)));
        if (hideKept)
            groups = groups.Where(g => g.Removable.Any());

        var filtered = groups.ToList();
        var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).Select(MapGroup).ToList();
        return new D.DuplicateGroupsPageDto
        {
            Mode = similar ? "similar" : "duplicates",
            Page = page,
            PageSize = pageSize,
            Total = filtered.Count,
            TotalPages = (int)Math.Ceiling(filtered.Count / (double)pageSize),
            RemovableCount = analysis.Duplicates.Sum(g => g.Removable.Count()),
            ReclaimableBytes = analysis.Duplicates.Sum(g => g.ReclaimableBytes),
            Items = items
        };
    }

    private D.DuplicateGroupDto MapGroup(DuplicateGroup g) => new()
    {
        Key = g.Key,
        Keeper = _mapper.Map(g.Keeper),
        ReclaimableBytes = g.ReclaimableBytes,
        Members = g.Members.Select(m => new D.GroupMemberDto
        {
            Photo = _mapper.Map(m.Photo),
            Match = m.Match switch { MatchKind.Identical => "identical", MatchKind.SamePicture => "samePicture", _ => "similar" },
            KeeperReason = m.Match == MatchKind.Identical ? "Identical file" : _ranking.WhyKept(g.Keeper, m.Photo)
        }).ToList()
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
        bool similar = string.Equals(q.Get("mode"), "similar", StringComparison.OrdinalIgnoreCase);
        int page = int.TryParse(q.Get("page"), out var p) ? Math.Max(1, p) : 1;
        int pageSize = int.TryParse(q.Get("pageSize"), out var ps) ? Math.Clamp(ps, 1, 200) : 50;
        var text = q.Get("q");
        var hideKept = q.Get("hideKept") == "true";
        var payload = await _facade.BuildAsync(similar, text, hideKept, page, pageSize, CancellationToken.None);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(payload);
        return resp;
    }
}

public class ScanLogsStubFunction
{
    private readonly IScanLogSink _sink;
    private readonly IScanProgressStore _progress;
    public ScanLogsStubFunction(IScanLogSink sink, IScanProgressStore progress) { _sink = sink; _progress = progress; }

    /// <summary>How long a followed stream stays open, and how often it looks for new lines.</summary>
    public TimeSpan FollowFor { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan PollEvery { get; init; } = TimeSpan.FromSeconds(2);

    // Simple JSON list endpoint retained
    [Function("GetScanLogsByInstance")]
    public async Task<HttpResponseData> GetLogs([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "scan/logs/{instanceId?}")] HttpRequestData req, string? instanceId)
    {
        var snap = _progress.GetLatest();
        var id = string.IsNullOrWhiteSpace(instanceId) ? snap.InstanceId : instanceId;
        var resp = req.CreateResponse(HttpStatusCode.OK);
        DateTime? since = null;
        var qs = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var sinceRaw = qs.Get("since");
    if (DateTime.TryParse(sinceRaw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed)) since = parsed.ToUniversalTime();
        var lines = string.IsNullOrWhiteSpace(id)
            ? new List<object>()
            : _sink.GetRecent(id).Where(l => !since.HasValue || l.ts > since.Value)
                .Select(l => (object)new { l.ts, l.level, l.message }).ToList();
        await resp.WriteAsJsonAsync(lines);
        return resp;
    }

    // Basic SSE stream; in production you might bridge to SignalR or Service Bus
    [Function("GetScanLogsStream")] // GET /api/scan/logs/stream
    public async Task<HttpResponseData> GetLogsStream([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "scan/logs/stream/{instanceId?}")] HttpRequestData req, string? instanceId)
    {
        var snap = _progress.GetLatest();
        var id = string.IsNullOrWhiteSpace(instanceId) ? snap.InstanceId : instanceId;
        var resp = req.CreateResponse(System.Net.HttpStatusCode.OK);
        resp.Headers.Add("Content-Type", "text/event-stream");
        if (string.IsNullOrWhiteSpace(id)) { await resp.WriteStringAsync("event: message\ndata: No active scan\n\n"); return resp; }
        var qs = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        bool follow = bool.TryParse(qs.Get("follow"), out var f) && f;
        var recent = _sink.GetRecent(id).Select(l => $"event: log\ndata: {l.ts:o} {l.level} {l.message}\n\n");
        await resp.WriteStringAsync(string.Join(string.Empty, recent));
        if (follow)
        {
            var start = DateTime.UtcNow;
            var lastCount = _sink.GetRecent(id).Count;
            while (DateTime.UtcNow - start < FollowFor)
            {
                await Task.Delay(PollEvery);
                var nowLogs = _sink.GetRecent(id);
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
