using System.Net;
using System.Text.Json;
using PhotoSense.Api.Http;
using PhotoSense.Api.Scanning;
using PhotoSense.Application.Removal;
using PhotoSense.Contracts.Cleaning;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Api.Endpoints;

/// <summary>Clean up's categories: what each holds, its pictures and videos, the bursts, and deleting those chosen.</summary>
internal sealed class CategoryEndpoints
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Dictionary<string, CleanUpCategory> Named = Enum.GetValues<CleanUpCategory>().ToDictionary(Name, StringComparer.Ordinal);
    private readonly CleanUpCategories _categories;
    private readonly PhotoDtoMapper _mapper;
    private readonly IAuditRepository _audit;

    /// <summary>The categories, with photos as the page shows them.</summary>
    /// <param name="categories">The categories.</param>
    /// <param name="mapper">Turns a photo into what the page shows.</param>
    /// <param name="audit">Where what was deleted is recorded.</param>
    public CategoryEndpoints(CleanUpCategories categories, PhotoDtoMapper mapper, IAuditRepository audit)
    {
        _categories = categories;
        _mapper = mapper;
        _audit = audit;
    }

    /// <summary>GET /api/cleanup/categories: how many each category holds and what they take, largest first, and a phone's storage.</summary>
    public async Task<ApiResponse> ReportAsync(ApiRequest req, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var report = await _categories.ReportAsync(ct);
        return await AnswerAsync(req, new CategoryReportDto
        {
            Categories = [.. report.Categories.Select(c => new CategorySummaryDto { Category = Name(c.Category), Count = c.Count, Bytes = c.Bytes })],
            Unmeasured = report.Unmeasured, StorageTotal = report.Storage?.Total, StorageFree = report.Storage?.Free,
        }, ct);
    }

    /// <summary>GET /api/cleanup/categories/{category}: the pictures or videos of a category; for bursts, each burst with the shot kept.</summary>
    public async Task<ApiResponse> PhotosAsync(ApiRequest req, string category, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        if (!Named.TryGetValue(category, out var which)) return await RefuseAsync(req, HttpStatusCode.NotFound, $"Clean up has no category called {category}.", ct);
        if (which == CleanUpCategory.Bursts)
            return await AnswerAsync(req, (await _categories.BurstsAsync(ct)).Select(b => new BurstDto { Keeper = b.Keeper.Id.Value, Shots = [.. b.Shots.Select(_mapper.Map)] }).ToList(), ct);
        return await AnswerAsync(req, (await _categories.PhotosAsync(which, ct)).Select(_mapper.Map).ToList(), ct);
    }

    /// <summary>
    /// POST /api/cleanup/categories/delete: deletes the pictures and videos chosen, on a phone to its Recently Deleted or
    /// bin after its own question, on a computer into _PhotoSense_Removed, which Undo puts back.
    /// </summary>
    public async Task<ApiResponse> DeleteAsync(ApiRequest req, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        if (await req.ReadJsonAsync<CategoryDeleteRequest>(Json, ct) is not { Ids.Count: > 0 } asked)
            return await RefuseAsync(req, HttpStatusCode.BadRequest, "Say which photos to delete.", ct);
        try
        {
            var done = await _categories.DeleteAsync([.. asked.Ids.Select(id => new PhotoId(id))], ct);
            // What was done is recorded whatever happens to the request now.
            await _audit.AddAsync(new AuditEntry { Action = "RemoveChosen", Details = $"removed={done.Deleted} bytes={done.Bytes} skipped={done.Skipped} bin={done.BatchId is null}" }, CancellationToken.None);
            return await AnswerAsync(req, new CategoryRemovalDto { Deleted = done.Deleted, Bytes = done.Bytes, Skipped = done.Skipped, Problems = done.Problems, BatchId = done.BatchId }, ct);
        }
        catch (InvalidOperationException ex) { return await RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
        // The person said no to the phone's question, or the phone could not delete them.
        catch (IOException ex) { return await RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
    }

    /// <summary>A category's name as the page knows it: "largeVideos".</summary>
    private static string Name(CleanUpCategory category) => JsonNamingPolicy.CamelCase.ConvertName(category.ToString());

    private static async Task<ApiResponse> AnswerAsync<T>(ApiRequest req, T value, CancellationToken ct)
    {
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(value, ct);
        return resp;
    }

    private static async Task<ApiResponse> RefuseAsync(ApiRequest req, HttpStatusCode status, string why, CancellationToken ct)
    {
        var resp = req.CreateResponse();
        await resp.WriteStringAsync(why, ct);
        resp.StatusCode = status;
        return resp;
    }
}
