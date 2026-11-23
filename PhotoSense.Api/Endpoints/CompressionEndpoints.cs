using System.Net;
using System.Text.Json;
using PhotoSense.Api.Http;
using PhotoSense.Api.Scanning;
using PhotoSense.Application.Removal;
using PhotoSense.Contracts.Cleaning;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Api.Endpoints;

/// <summary>Compressing large videos, which only a phone does: the videos with about what each copy takes, starting, how it stands, and cancelling.</summary>
internal sealed class CompressionEndpoints
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private const string OnlyAPhone = "Only a phone compresses videos.";
    private readonly VideoCompression? _compression;
    private readonly PhotoDtoMapper _mapper;

    /// <summary>Compressing, where this program has it.</summary>
    /// <param name="compression">Compressing; null on a computer.</param>
    /// <param name="mapper">Makes a photo into what the page is sent.</param>
    public CompressionEndpoints(VideoCompression? compression, PhotoDtoMapper mapper)
    {
        _compression = compression;
        _mapper = mapper;
    }

    /// <summary>GET /api/compress/videos: the large videos, largest first, each with about what its copy takes at each quality.</summary>
    public async Task<ApiResponse> VideosAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        var videos = await _compression!.VideosAsync(ct);
        return await Answers.JsonAsync(req, videos.Select(v => new CompressibleVideoDto
        {
            Video = _mapper.Map(v.Video),
            Estimates = new CompressionEstimatesDto
            {
                High = v.Estimates[CompressionQuality.High], Balanced = v.Estimates[CompressionQuality.Balanced], Small = v.Estimates[CompressionQuality.Small],
            },
        }).ToList(), ct);
    }

    /// <summary>GET /api/compress: how compressing stands.</summary>
    public async Task<ApiResponse> StateAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        ct.ThrowIfCancellationRequested();
        return await Answers.JsonAsync(req, Dto(_compression!.State), ct);
    }

    /// <summary>
    /// POST /api/compress/start: starts compressing the videos given at the quality given, in the background. 409 off
    /// Pro, with none it can compress, or while it is compressing already.
    /// </summary>
    public async Task<ApiResponse> StartAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        if (await req.ReadJsonAsync<CompressStartRequest>(Json, ct) is not { Ids.Count: > 0 } asked || Quality(asked.Quality) is not { } quality)
            return await Answers.RefuseAsync(req, HttpStatusCode.BadRequest, "Say which videos, and how small: high, balanced or small.", ct);
        try { return await Answers.JsonAsync(req, Dto(await _compression!.StartAsync([.. asked.Ids.Select(id => new PhotoId(id))], quality, ct)), ct); }
        catch (InvalidOperationException ex) { return await Answers.RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
    }

    /// <summary>POST /api/compress/cancel: stops compressing after the video it is on; what was compressed is kept.</summary>
    public async Task<ApiResponse> CancelAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        ct.ThrowIfCancellationRequested();
        return await Answers.JsonAsync(req, Dto(_compression!.Cancel()), ct);
    }

    private static CompressionQuality? Quality(string name)
        => Enum.GetValues<CompressionQuality>().Cast<CompressionQuality?>().FirstOrDefault(q => Name(q!.Value) == name);

    private static string Name<T>(T value) where T : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private static CompressionStateDto Dto(CompressionState s) => new()
    {
        Stage = Name(s.Stage), Quality = Name(s.Quality), Total = s.Total, Done = s.Done, Current = s.Current, Progress = s.Progress,
        Results = [.. s.Results.Select(r => new CompressedVideoDto { Id = r.Id.Value, FileName = r.FileName, Before = r.Before, After = r.After })],
        Saved = s.Saved, OriginalsDeleted = s.OriginalsDeleted, Problem = s.Problem,
    };

    private Task<ApiResponse>? Refused(ApiRequest req, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return Task.FromResult(req.CreateResponse(HttpStatusCode.Unauthorized));
        return _compression is null ? Answers.RefuseAsync(req, HttpStatusCode.NotFound, OnlyAPhone, ct) : null;
    }
}
