using System.Net;
using System.Text.Json;
using System.Web;
using PhotoSense.Api.Http;
using PhotoSense.Api.Scanning;
using PhotoSense.Application.Organizing;
using PhotoSense.Contracts.Cleaning;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;

namespace PhotoSense.Api.Endpoints;

/// <summary>
/// What a phone's Library and viewer ask: search, a picture's camera and favorite mark, marking a favorite, and sharing a
/// copy without its place and camera details. Only a phone has them.
/// </summary>
internal sealed class ViewerEndpoints
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private const string OnlyAPhone = "Only a phone has this.";
    private readonly PhotoSearch? _search;
    private readonly IFavoritePhotos? _favorites;
    private readonly ISharingCopies? _copies;
    private readonly IShareSheet? _share;
    private readonly IPhotoRepository _repo;
    private readonly PhotoDtoMapper _mapper;

    /// <summary>The viewer's endpoints, with what this program has of them.</summary>
    /// <param name="search">Search; null on a computer.</param>
    /// <param name="favorites">The phone's favorites; null on a computer.</param>
    /// <param name="copies">Copies to share; null on a computer.</param>
    /// <param name="share">The phone's share sheet; null on a computer.</param>
    /// <param name="repo">What the scans recorded, for a picture's camera.</param>
    /// <param name="mapper">Makes a photo into what the page is sent.</param>
    public ViewerEndpoints(PhotoSearch? search, IFavoritePhotos? favorites, ISharingCopies? copies, IShareSheet? share, IPhotoRepository repo, PhotoDtoMapper mapper)
    {
        _search = search;
        _favorites = favorites;
        _copies = copies;
        _share = share;
        _repo = repo;
        _mapper = mapper;
    }

    /// <summary>GET /api/search?q=: the photos that match, latest first, with the search in its parts.</summary>
    public async Task<ApiResponse> SearchAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _search, ct) is { } refused) return await refused;
        var found = await _search!.SearchAsync(HttpUtility.ParseQueryString(req.Url.Query).Get("q") ?? string.Empty, ct);
        return await Answers.JsonAsync(req, new SearchResultDto
        {
            Parts = [.. found.Query.Parts.Select(p => new SearchPartDto { Kind = JsonNamingPolicy.CamelCase.ConvertName(p.Kind.ToString()), Text = p.Text })],
            Photos = [.. found.Photos.Select(_mapper.Map)], Total = found.Total, Videos = found.Videos, ProNeeded = found.ProNeeded,
        }, ct);
    }

    /// <summary>GET /api/viewer/details?path=: a picture or video's camera, and whether it is a favorite.</summary>
    public async Task<ApiResponse> DetailsAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _favorites, ct) is { } refused) return await refused;
        if (HttpUtility.ParseQueryString(req.Url.Query).Get("path") is not { Length: > 0 } path)
            return await Answers.RefuseAsync(req, HttpStatusCode.BadRequest, "Say which picture or video.", ct);
        var record = await _repo.GetByPathAsync(path, ct);
        var favorite = (await _favorites!.FavoritesAsync(ct)).Contains(path);
        return await Answers.JsonAsync(req, new PhotoDetailsDto { Camera = record?.CameraModel, Favorite = favorite }, ct);
    }

    /// <summary>POST /api/viewer/favorite: marks a picture or video a favorite in the phone's own photos app, or takes the mark away.</summary>
    public async Task<ApiResponse> FavoriteAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _favorites, ct) is { } refused) return await refused;
        if (await req.ReadJsonAsync<FavoriteRequest>(Json, ct) is not { Path.Length: > 0 } asked)
            return await Answers.RefuseAsync(req, HttpStatusCode.BadRequest, "Say which picture or video.", ct);
        try
        {
            _favorites!.SetFavorite(asked.Path, asked.Favorite);
            return await Answers.JsonAsync(req, new PhotoDetailsDto { Camera = (await _repo.GetByPathAsync(asked.Path, ct))?.CameraModel, Favorite = asked.Favorite }, ct);
        }
        catch (IOException ex) { return await Answers.RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
    }

    /// <summary>
    /// POST /api/share/photo: shows the phone's share sheet with a copy of a picture or video, without where it was taken
    /// and the camera's details as asked. The original keeps them.
    /// </summary>
    public async Task<ApiResponse> SharePhotoAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _copies, ct) is { } refused) return await refused;
        if (await req.ReadJsonAsync<SharePhotoRequest>(Json, ct) is not { Path.Length: > 0 } asked)
            return await Answers.RefuseAsync(req, HttpStatusCode.BadRequest, "Say which picture or video.", ct);
        try
        {
            var copy = await _copies!.CopyForSharingAsync(asked.Path, asked.RemoveLocation, asked.RemoveCamera, ct);
            await _share!.ShareFileAsync(copy.Path, copy.MimeType, ct);
            return req.CreateResponse(HttpStatusCode.NoContent);
        }
        catch (IOException ex) { return await Answers.RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
    }

    private static Task<ApiResponse>? Refused(ApiRequest req, object? feature, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return Task.FromResult(req.CreateResponse(HttpStatusCode.Unauthorized));
        return feature is null ? Answers.RefuseAsync(req, HttpStatusCode.NotFound, OnlyAPhone, ct) : null;
    }
}
