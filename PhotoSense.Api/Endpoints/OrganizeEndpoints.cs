using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using PhotoSense.Application.Organizing;
using PhotoSense.Contracts.Organize;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Api.Http;

namespace PhotoSense.Api.Endpoints;

/// <summary>
/// Organize: lists the pictures and videos of a folder with when and where each was taken, works out
/// where a set of them would go, moves or copies them there, takes files out, and undoes any of that.
/// </summary>
internal sealed class OrganizeEndpoints
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    // A page asks for dozens of previews at once. Made all together they keep the disk and the processor from
    // everything else the page asks for, so they are made a few at a time.
    private static readonly SemaphoreSlim PreviewsBeingMade = new(3);
    private readonly OrganizeLibrary _library;
    private readonly OrganizePlanner _planner;
    private readonly OrganizeMover _mover;
    private readonly IOrganizeBatchStore _batches;
    private readonly OrganizeListingMapper _mapper;
    private readonly IThumbnailStore _thumbnails;
    private readonly IImageAnalyzer _analyzer;
    private readonly ISystemViewer _viewer;
    private readonly IPhotoFileSystem _files;

    /// <summary>Organize over the services given.</summary>
    public OrganizeEndpoints(OrganizeLibrary library, OrganizePlanner planner, OrganizeMover mover, IOrganizeBatchStore batches,
        OrganizeListingMapper mapper, IThumbnailStore thumbnails, IImageAnalyzer analyzer, ISystemViewer viewer, IPhotoFileSystem files)
    {
        _library = library;
        _planner = planner;
        _mover = mover;
        _batches = batches;
        _mapper = mapper;
        _thumbnails = thumbnails;
        _analyzer = analyzer;
        _viewer = viewer;
        _files = files;
    }

    private static string Wall(DateTime clock) => OrganizeListingMapper.Wall(clock);

    // What the person asked for cannot be done as asked: the reason, in words, with the status that says whose fault.
    private static async Task<ApiResponse> RefuseAsync(ApiRequest req, HttpStatusCode status, string why, CancellationToken ct)
    {
        var resp = req.CreateResponse();
        await resp.WriteStringAsync(why, ct);
        resp.StatusCode = status;
        return resp;
    }

    /// <summary>GET /api/organize/files?root=: every picture and video under a folder, with when and where each was taken.</summary>
    public async Task<ApiResponse> FilesAsync(ApiRequest req, CancellationToken ct)
    {
        // The names of someone's files are told to the UI itself and to no other web page.
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var root = HttpUtility.ParseQueryString(req.Url.Query).Get("root");
        IReadOnlyList<LibraryFile> files;
        // A caller that goes away only stops waiting: the reading goes on, and asking again joins it.
        try { files = await _library.ListAsync(root ?? string.Empty, ct); }
        // Not there, or not something that could be a path at all.
        catch (Exception ex) when (ex is IOException or ArgumentException) { return await RefuseAsync(req, HttpStatusCode.NotFound, $"Folder not found: {root}", ct); }

        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(_mapper.Whole(Path.GetFullPath(root!), files), ct);
        return resp;
    }

    /// <summary>
    /// GET /api/organize/progress?root=&amp;from=&amp;places=: what has been read of a folder while it is read. The page asks
    /// again and again, each time saying how much it already has.
    /// </summary>
    public async Task<ApiResponse> ProgressAsync(ApiRequest req, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var query = HttpUtility.ParseQueryString(req.Url.Query);
        var root = query.Get("root");
        int.TryParse(query.Get("from"), out var from);
        int.TryParse(query.Get("places"), out var placesFrom);
        var progress = string.IsNullOrWhiteSpace(root) ? null : _library.ProgressOf(root, from, OrganizeListingMapper.BatchSize);
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(_mapper.Partial(progress, placesFrom), ct);
        return resp;
    }

    /// <summary>POST /api/organize/plan: where each file would go, and which names are taken there. Nothing moves.</summary>
    public async Task<ApiResponse> PlanAsync(ApiRequest req, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var (request, destination, refusal) = await ReadAsync(req, ct);
        if (refusal is not null) return refusal;

        var plan = await _planner.PlanAsync(destination!, request!.Files.Select(f => f.Id ?? string.Empty).ToList(), request.Files.Select(f => f.Subfolder).ToList(), ct);
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(new OrganizePlanDto
        {
            Destination = plan.Destination, Exists = plan.Exists, Companions = plan.Companions,
            Items = plan.Items.Select(i => new OrganizePlanItemDto { Id = i.File.Id, Folder = i.Folder }).ToList(),
            Taken = plan.Taken.ToDictionary(t => t.Key, t => t.Value.ToList()),
            Clashes = plan.Clashes.Select(c => new OrganizeClashDto
            {
                Id = c.File.Id, NextFree = c.NextFree,
                Existing = c.Existing.Select(e => new OrganizeExistingDto
                {
                    Id = e.File.Id, Name = e.File.Name, SizeBytes = e.File.SizeBytes, Date = Wall(e.File.Date), Width = e.File.Details.Width, Height = e.File.Details.Height,
                    IsVideo = e.File.IsVideo, PlaceName = e.PlaceName, Identical = e.Identical,
                }).ToList(),
            }).ToList(),
        }, ct);
        return resp;
    }

    /// <summary>POST /api/organize/apply: moves or copies the files, as one change that can be undone.</summary>
    public async Task<ApiResponse> ApplyAsync(ApiRequest req, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var (request, destination, refusal) = await ReadAsync(req, ct);
        if (refusal is not null) return refusal;

        var copy = string.Equals(request!.Mode, "copy", StringComparison.OrdinalIgnoreCase);
        var label = string.IsNullOrWhiteSpace(request.Label) ? Path.GetFileName(destination!.Folder) : request.Label.Trim();
        var result = await _mover.ApplyAsync(destination!, copy, request.Companions, label, request.Files.Select(f => (f.Id ?? string.Empty, f.Name)).ToList(),
            request.Files.Select(f => f.Subfolder).ToList(), ct);
        return await DoneAsync(req, result, ct);
    }

    /// <summary>
    /// POST /api/organize/remove: nothing is erased. The files go to the folder that holds removed files inside the
    /// root, and an undo brings them back.
    /// </summary>
    public async Task<ApiResponse> RemoveAsync(ApiRequest req, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var request = await req.ReadJsonAsync<OrganizeRemoveRequest>(Json, ct);
        if (request?.Files is null) return await RefuseAsync(req, HttpStatusCode.BadRequest, "The request could not be read.", ct);
        if (string.IsNullOrWhiteSpace(request.Root) || !Path.IsPathFullyQualified(request.Root) || !_files.FolderExists(request.Root))
            return await RefuseAsync(req, HttpStatusCode.NotFound, $"Folder not found: {request.Root}", ct);

        return await DoneAsync(req, await _mover.RemoveAsync(request.Root, request.Files.Select(f => f.Id ?? string.Empty).ToList(), ct), ct);
    }

    private static async Task<ApiResponse> DoneAsync(ApiRequest req, OrganizeResult result, CancellationToken ct)
    {
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(new OrganizeApplyDto
        {
            BatchId = result.BatchId?.ToString(), Done = result.Done, Bytes = result.Bytes, Companions = result.Companions, Renamed = result.Renamed,
            Skipped = result.Skipped, Problems = result.Problems.ToList(),
            Items = result.Items.Select(i => new OrganizeMovedDto { Id = i.Id, Path = i.Path }).ToList(),
        }, ct);
        return resp;
    }

    /// <summary>The request and the destination it names, or the answer that says what is wrong with them.</summary>
    private async Task<(OrganizeRequest?, OrganizeDestination?, ApiResponse?)> ReadAsync(ApiRequest req, CancellationToken ct)
    {
        var request = await req.ReadJsonAsync<OrganizeRequest>(Json, ct);
        if (request?.Files is null) return (null, null, await RefuseAsync(req, HttpStatusCode.BadRequest, "The request could not be read.", ct));
        try
        {
            // A subfolder that cannot be made is said now, before anything is planned or moved.
            foreach (var file in request.Files) OrganizeDestination.Subfolder(file.Subfolder);
            return (request, new OrganizeDestination(request.BasePath, request.FolderName, request.Direct, request.YearSplit, _files), null);
        }
        catch (DirectoryNotFoundException ex) { return (null, null, await RefuseAsync(req, HttpStatusCode.NotFound, ex.Message, ct)); }
        catch (ArgumentException ex) { return (null, null, await RefuseAsync(req, HttpStatusCode.BadRequest, ex.Message, ct)); }
    }

    /// <summary>POST /api/organize/undo/{batchId}: puts back what a move, copy or removal did.</summary>
    public async Task<ApiResponse> UndoAsync(ApiRequest req, string batchId, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var result = Guid.TryParse(batchId, out var id) ? await _mover.UndoAsync(id, ct) : null;
        if (result is not { Found: true }) return await RefuseAsync(req, HttpStatusCode.NotFound, "There is nothing to undo: this was undone already, or was never done.", ct);
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(new OrganizeUndoDto { Restored = result.Restored, Skipped = result.Skipped, Problems = result.Problems.ToList() }, ct);
        return resp;
    }

    /// <summary>GET /api/organize/batches: the latest changes that can be undone, newest first.</summary>
    public async Task<ApiResponse> BatchesAsync(ApiRequest req, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync((await _batches.RecentAsync(20, ct)).Select(b => new OrganizeBatchDto
        {
            Id = b.Id.ToString(), Label = b.Label, Mode = b.Removed ? "remove" : b.Copy ? "copy" : "move", Count = b.Files, Bytes = b.Bytes, Utc = new DateTime(b.UtcTicks, DateTimeKind.Utc),
        }).ToList(), ct);
        return resp;
    }

    /// <summary>GET /api/organize/files/{id}/thumbnail: a file's preview, made once and kept.</summary>
    public async Task<ApiResponse> ThumbnailAsync(ApiRequest req, string id, CancellationToken ct)
    {
        // Videos are never decoded, so they have no picture to show.
        if (await _library.FindAsync(id, ct) is not { IsVideo: false } file) return req.CreateResponse(HttpStatusCode.NotFound);

        // A scan keeps previews by what the file holds. A file no scan has read is kept by its size and the
        // instant it was last changed: those stay with it when it is moved, and a copy that shares them
        // shares the picture too.
        var key = file.ContentHash
            ?? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Path.GetExtension(file.Path).ToUpperInvariant()}|{file.SizeBytes}|{file.ModifiedUtc.Ticks}")));
        var jpeg = await _thumbnails.GetAsync(key, ct);
        if (jpeg is null)
        {
            await PreviewsBeingMade.WaitAsync(ct);
            try
            {
                await using var stream = _files.OpenRead(file.Path);
                jpeg = (await _analyzer.AnalyzeAsync(stream, ct)).ThumbnailJpeg;
                await _thumbnails.SaveAsync(key, jpeg, ct);
            }
            // A picture that cannot be decoded, or read, has no preview to show.
            catch (Exception ex) when (ex is UnreadableImageException or IOException or UnauthorizedAccessException)
            {
                return req.CreateResponse(HttpStatusCode.NotFound);
            }
            finally { PreviewsBeingMade.Release(); }
        }
        return await PhotosEndpoints.ImageResponseAsync(req, jpeg, "image/jpeg", key, ct);
    }

    /// <summary>GET /api/organize/files/{id}/image: the picture, in a form a browser can show at full size.</summary>
    public async Task<ApiResponse> ImageAsync(ApiRequest req, string id, CancellationToken ct)
    {
        if (await _library.FindAsync(id, ct) is not { IsVideo: false } file) return req.CreateResponse(HttpStatusCode.NotFound);
        return await PhotosEndpoints.ShowImageAsync(req, _files, file.Path, file.ContentHash, _analyzer, ct);
    }

    /// <summary>POST /api/organize/files/{id}/open: opens the file in the default application of the machine that holds it.</summary>
    public async Task<ApiResponse> OpenAsync(ApiRequest req, string id, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        if (await _library.FindAsync(id, ct) is not { } file) return await RefuseAsync(req, HttpStatusCode.NotFound, "The file is no longer there. Choose the folder again.", ct);
        try
        {
            // Opens on the machine this service runs on, which is the machine that holds the files.
            _viewer.Open(file.Path);
            return req.CreateResponse(HttpStatusCode.NoContent);
        }
        catch (FileNotFoundException) { return await RefuseAsync(req, HttpStatusCode.NotFound, "The file is no longer there. Choose the folder again.", ct); }
        catch (InvalidOperationException ex) { return await RefuseAsync(req, HttpStatusCode.InternalServerError, ex.Message, ct); }
    }
}
