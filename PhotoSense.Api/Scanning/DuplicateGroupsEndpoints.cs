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
internal sealed class PhotoDtoMapper
{
    private readonly IPlaceNameResolver _places;

    /// <summary>Names places through <paramref name="places"/>.</summary>
    public PhotoDtoMapper(IPlaceNameResolver places) => _places = places;

    /// <summary>The photo as the page shows it.</summary>
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

/// <summary>A page of the groups a scan found, as the Clean up screens show them.</summary>
internal sealed class ScanGroupingFacade
{
    private readonly IDuplicateAnalysisService _analysis;
    private readonly PhotoDtoMapper _mapper;
    private readonly PhotoRanking _ranking;

    /// <summary>Pages of the groups <paramref name="analysis"/> finds, each saying why its keeper was chosen.</summary>
    public ScanGroupingFacade(IDuplicateAnalysisService analysis, PhotoDtoMapper mapper, PhotoRanking ranking) { _analysis = analysis; _mapper = mapper; _ranking = ranking; }

    /// <summary>One page of the groups.</summary>
    /// <param name="similar">False for duplicates (safe to remove in bulk), true for look-alikes (review only).</param>
    /// <param name="q">Only groups with a file whose path holds this text; null or blank for every group.</param>
    /// <param name="hideKept">Leave out groups in which every member has been marked to keep.</param>
    /// <param name="page">Which page, from 1.</param>
    /// <param name="pageSize">How many groups a page holds.</param>
    /// <param name="ct">Stops the work.</param>
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

/// <summary>The groups a scan found, a page at a time.</summary>
internal sealed class DuplicateGroupsEndpoints
{
    private readonly ScanGroupingFacade _facade;

    /// <summary>Pages made by <paramref name="facade"/>.</summary>
    public DuplicateGroupsEndpoints(ScanGroupingFacade facade) => _facade = facade;

    /// <summary>GET /api/scan/groups?mode=&amp;page=&amp;pageSize=&amp;q=&amp;hideKept=: one page of duplicate or look-alike groups.</summary>
    public async Task<ApiResponse> GetDuplicateGroupsAsync(ApiRequest req, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var q = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        bool similar = string.Equals(q.Get("mode"), "similar", StringComparison.OrdinalIgnoreCase);
        int page = int.TryParse(q.Get("page"), out var p) ? Math.Max(1, p) : 1;
        int pageSize = int.TryParse(q.Get("pageSize"), out var ps) ? Math.Clamp(ps, 1, 200) : 50;
        var text = q.Get("q");
        var hideKept = q.Get("hideKept") == "true";
        var payload = await _facade.BuildAsync(similar, text, hideKept, page, pageSize, ct);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(payload, ct);
        return resp;
    }
}
