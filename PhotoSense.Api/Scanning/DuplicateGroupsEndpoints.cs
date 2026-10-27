using System.Net;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.DTOs;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Services;
using PhotoSense.Api.Endpoints;
using PhotoSense.Api.Http;
using D = PhotoSense.Contracts.Duplicates; // alias for shared duplicate group DTOs

namespace PhotoSense.Api.Scanning;

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

public class DuplicateGroupsEndpoints
{
    private readonly ScanGroupingFacade _facade;
    public DuplicateGroupsEndpoints(ScanGroupingFacade facade) => _facade = facade;

    // GET /api/scan/groups
    public async Task<ApiResponse> GetDuplicateGroups(
        ApiRequest req)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var q = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        bool similar = string.Equals(q.Get("mode"), "similar", StringComparison.OrdinalIgnoreCase);
        int page = int.TryParse(q.Get("page"), out var p) ? Math.Max(1, p) : 1;
        int pageSize = int.TryParse(q.Get("pageSize"), out var ps) ? Math.Clamp(ps, 1, 200) : 50;
        var text = q.Get("q");
        var hideKept = q.Get("hideKept") == "true";
        var payload = await _facade.BuildAsync(similar, text, hideKept, page, pageSize, req.Aborted);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(payload);
        return resp;
    }
}
