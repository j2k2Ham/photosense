using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PhotoSense.Application.Organizing;
using PhotoSense.Contracts.Organize;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;

namespace PhotoSense.Functions.Api;

/// <summary>
/// Organize: lists the pictures and videos of a folder with when and where each was taken, works out
/// where a set of them would go, moves or copies them there, takes files out, and undoes any of that.
/// </summary>
public class OrganizeFunctions
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

    public OrganizeFunctions(OrganizeLibrary library, OrganizePlanner planner, OrganizeMover mover, IOrganizeBatchStore batches,
        OrganizeListingMapper mapper, IThumbnailStore thumbnails, IImageAnalyzer analyzer, ISystemViewer viewer)
    {
        _library = library;
        _planner = planner;
        _mover = mover;
        _batches = batches;
        _mapper = mapper;
        _thumbnails = thumbnails;
        _analyzer = analyzer;
        _viewer = viewer;
    }

    private static string Wall(DateTime clock) => OrganizeListingMapper.Wall(clock);

    // What the person asked for cannot be done as asked: the reason, in words, with the status that says whose fault.
    private static async Task<HttpResponseData> RefuseAsync(HttpRequestData req, HttpStatusCode status, string why)
    {
        var resp = req.CreateResponse();
        await resp.WriteStringAsync(why);
        resp.StatusCode = status;
        return resp;
    }

    [Function("OrganizeFiles")] // GET /api/organize/files?root=
    public async Task<HttpResponseData> FilesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "organize/files")] HttpRequestData req)
    {
        // The names of someone's files are told to the UI itself and to no other web page.
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var root = HttpUtility.ParseQueryString(req.Url.Query).Get("root");
        IReadOnlyList<LibraryFile> files;
        try { files = await _library.ListAsync(root ?? string.Empty); }
        // Not there, or not something that could be a path at all.
        catch (Exception ex) when (ex is IOException or ArgumentException) { return await RefuseAsync(req, HttpStatusCode.NotFound, $"Folder not found: {root}"); }

        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(_mapper.Whole(Path.GetFullPath(root!), files));
        return resp;
    }

    // While a folder is read, the page asks this again and again, each time saying how much it already has.
    [Function("OrganizeProgress")] // GET /api/organize/progress?root=&from=&places=
    public async Task<HttpResponseData> ProgressAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "organize/progress")] HttpRequestData req)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var query = HttpUtility.ParseQueryString(req.Url.Query);
        var root = query.Get("root");
        int.TryParse(query.Get("from"), out var from);
        int.TryParse(query.Get("places"), out var placesFrom);
        ListingProgress? progress = null;
        // Something that could not be a path at all is simply not being read.
        try { if (!string.IsNullOrWhiteSpace(root)) progress = _library.ProgressOf(root, from, OrganizeListingMapper.BatchSize); }
        catch (Exception ex) when (ex is IOException or ArgumentException) { }
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(_mapper.Partial(progress, placesFrom));
        return resp;
    }

    [Function("OrganizePlan")] // POST /api/organize/plan
    public async Task<HttpResponseData> PlanAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "organize/plan")] HttpRequestData req)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var (request, destination, refusal) = await ReadAsync(req);
        if (refusal is not null) return refusal;

        var plan = await _planner.PlanAsync(destination!, request!.Files.Select(f => f.Id ?? string.Empty).ToList(), request.Files.Select(f => f.Subfolder).ToList());
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
        });
        return resp;
    }

    [Function("OrganizeApply")] // POST /api/organize/apply
    public async Task<HttpResponseData> ApplyAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "organize/apply")] HttpRequestData req)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var (request, destination, refusal) = await ReadAsync(req);
        if (refusal is not null) return refusal;

        var copy = string.Equals(request!.Mode, "copy", StringComparison.OrdinalIgnoreCase);
        var label = string.IsNullOrWhiteSpace(request.Label) ? Path.GetFileName(destination!.Folder) : request.Label.Trim();
        var result = await _mover.ApplyAsync(destination!, copy, request.Companions, label, request.Files.Select(f => (f.Id ?? string.Empty, f.Name)).ToList(),
            request.Files.Select(f => f.Subfolder).ToList());
        return await DoneAsync(req, result);
    }

    // Nothing is erased: the files go to the folder that holds removed files inside the root, and an undo brings them back.
    [Function("OrganizeRemove")] // POST /api/organize/remove
    public async Task<HttpResponseData> RemoveAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "organize/remove")] HttpRequestData req)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        OrganizeRemoveRequest? request = null;
        try { request = JsonSerializer.Deserialize<OrganizeRemoveRequest>(await new StreamReader(req.Body).ReadToEndAsync(), Json); }
        catch (JsonException) { /* said below */ }
        if (request?.Files is null) return await RefuseAsync(req, HttpStatusCode.BadRequest, "The request could not be read.");
        if (string.IsNullOrWhiteSpace(request.Root) || !Path.IsPathFullyQualified(request.Root) || !Directory.Exists(request.Root))
            return await RefuseAsync(req, HttpStatusCode.NotFound, $"Folder not found: {request.Root}");

        return await DoneAsync(req, await _mover.RemoveAsync(request.Root, request.Files.Select(f => f.Id ?? string.Empty).ToList()));
    }

    private static async Task<HttpResponseData> DoneAsync(HttpRequestData req, OrganizeResult result)
    {
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(new OrganizeApplyDto
        {
            BatchId = result.BatchId?.ToString(), Done = result.Done, Bytes = result.Bytes, Companions = result.Companions, Renamed = result.Renamed,
            Skipped = result.Skipped, Problems = result.Problems.ToList(),
            Items = result.Items.Select(i => new OrganizeMovedDto { Id = i.Id, Path = i.Path }).ToList(),
        });
        return resp;
    }

    // The request and the destination it names, or the answer that says what is wrong with them.
    private static async Task<(OrganizeRequest?, OrganizeDestination?, HttpResponseData?)> ReadAsync(HttpRequestData req)
    {
        OrganizeRequest? request = null;
        try { request = JsonSerializer.Deserialize<OrganizeRequest>(await new StreamReader(req.Body).ReadToEndAsync(), Json); }
        catch (JsonException) { /* said below */ }
        if (request?.Files is null) return (null, null, await RefuseAsync(req, HttpStatusCode.BadRequest, "The request could not be read."));
        try
        {
            // A subfolder that cannot be made is said now, before anything is planned or moved.
            foreach (var file in request.Files) OrganizeDestination.Subfolder(file.Subfolder);
            return (request, new OrganizeDestination(request.BasePath, request.FolderName, request.Direct, request.YearSplit), null);
        }
        catch (DirectoryNotFoundException ex) { return (null, null, await RefuseAsync(req, HttpStatusCode.NotFound, ex.Message)); }
        catch (ArgumentException ex) { return (null, null, await RefuseAsync(req, HttpStatusCode.BadRequest, ex.Message)); }
    }

    [Function("OrganizeUndo")] // POST /api/organize/undo/{batchId}
    public async Task<HttpResponseData> UndoAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "organize/undo/{batchId}")] HttpRequestData req,
        string batchId)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var result = Guid.TryParse(batchId, out var id) ? await _mover.UndoAsync(id) : null;
        if (result is not { Found: true }) return await RefuseAsync(req, HttpStatusCode.NotFound, "There is nothing to undo: this was undone already, or was never done.");
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(new OrganizeUndoDto { Restored = result.Restored, Skipped = result.Skipped, Problems = result.Problems.ToList() });
        return resp;
    }

    [Function("OrganizeBatches")] // GET /api/organize/batches
    public async Task<HttpResponseData> BatchesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "organize/batches")] HttpRequestData req)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync((await _batches.RecentAsync()).Select(b => new OrganizeBatchDto
        {
            Id = b.Id.ToString(), Label = b.Label, Mode = b.Removed ? "remove" : b.Copy ? "copy" : "move", Count = b.Files, Bytes = b.Bytes, Utc = new DateTime(b.UtcTicks, DateTimeKind.Utc),
        }).ToList());
        return resp;
    }

    [Function("OrganizeThumbnail")] // GET /api/organize/files/{id}/thumbnail
    public async Task<HttpResponseData> ThumbnailAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "organize/files/{id}/thumbnail")] HttpRequestData req,
        string id)
    {
        // Videos are never decoded, so they have no picture to show.
        if (await _library.FindAsync(id) is not { IsVideo: false } file) return req.CreateResponse(HttpStatusCode.NotFound);

        // A scan keeps previews by what the file holds. A file no scan has read is kept by its size and the
        // instant it was last changed: those stay with it when it is moved, and a copy that shares them
        // shares the picture too.
        var key = file.ContentHash
            ?? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Path.GetExtension(file.Path).ToUpperInvariant()}|{file.SizeBytes}|{file.ModifiedUtc.Ticks}")));
        var jpeg = await _thumbnails.GetAsync(key);
        if (jpeg is null)
        {
            await PreviewsBeingMade.WaitAsync();
            try
            {
                await using var stream = PhotosFunctions.OpenForShowing(file.Path);
                jpeg = (await _analyzer.AnalyzeAsync(stream)).ThumbnailJpeg;
                await _thumbnails.SaveAsync(key, jpeg);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return req.CreateResponse(HttpStatusCode.NotFound);
            }
            finally { PreviewsBeingMade.Release(); }
        }
        return await PhotosFunctions.ImageResponseAsync(req, jpeg, "image/jpeg", key);
    }

    [Function("OrganizeImage")] // GET /api/organize/files/{id}/image
    public async Task<HttpResponseData> ImageAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "organize/files/{id}/image")] HttpRequestData req,
        string id)
    {
        if (await _library.FindAsync(id) is not { IsVideo: false } file) return req.CreateResponse(HttpStatusCode.NotFound);
        return await PhotosFunctions.ShowImageAsync(req, file.Path, file.ContentHash, _analyzer);
    }

    [Function("OrganizeOpen")] // POST /api/organize/files/{id}/open
    public async Task<HttpResponseData> OpenAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "organize/files/{id}/open")] HttpRequestData req,
        string id)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        if (await _library.FindAsync(id) is not { } file) return await RefuseAsync(req, HttpStatusCode.NotFound, "The file is no longer there. Choose the folder again.");
        try
        {
            // Opens on the machine this service runs on, which is the machine that holds the files.
            _viewer.Open(file.Path);
            return req.CreateResponse(HttpStatusCode.NoContent);
        }
        catch (FileNotFoundException) { return await RefuseAsync(req, HttpStatusCode.NotFound, "The file is no longer there. Choose the folder again."); }
        catch (InvalidOperationException ex) { return await RefuseAsync(req, HttpStatusCode.InternalServerError, ex.Message); }
    }
}
