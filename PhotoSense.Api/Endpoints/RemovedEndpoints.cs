using System.Net;
using System.Text.Json;
using System.Web;
using PhotoSense.Application.Removal;
using PhotoSense.Contracts.Removed;
using PhotoSense.Api.Http;

namespace PhotoSense.Api.Endpoints;

/// <summary>What Clean up and Organize have removed: how much is waiting in the folders that hold it, and erasing it for good.</summary>
internal sealed class RemovedEndpoints
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly RemovedFiles _removed;

    /// <summary>The removed files <paramref name="removed"/> knows.</summary>
    public RemovedEndpoints(RemovedFiles removed) => _removed = removed;

    /// <summary>GET /api/removed?root=&amp;root=: every folder of removed files that holds something, with how much.</summary>
    public async Task<ApiResponse> FindAsync(ApiRequest req, CancellationToken ct)
    {
        // Where someone's files are is told to the UI itself and to no other web page.
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var folders = await _removed.FindAsync(HttpUtility.ParseQueryString(req.Url.Query).GetValues("root") ?? [], ct);
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(new RemovedFilesDto
        {
            Folders = folders.Select(f => new RemovedFolderDto { Path = f.Path, Files = f.Files, Bytes = f.Bytes }).ToList(),
            Files = folders.Sum(f => f.Files), Bytes = folders.Sum(f => f.Bytes),
        }, ct);
        return resp;
    }

    /// <summary>
    /// POST /api/removed/erase: the one request that erases files. It names the folders as they were listed, and only
    /// folders of removed files are ever emptied.
    /// </summary>
    public async Task<ApiResponse> EraseAsync(ApiRequest req, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var request = await req.ReadJsonAsync<EraseRemovedRequest>(Json, ct);
        var resp = req.CreateResponse();
        try
        {
            if (request?.Folders is null) throw new ArgumentException("The request could not be read.");
            var result = await _removed.EraseAsync(request.Folders, ct);
            await resp.WriteAsJsonAsync(new EraseRemovedDto { Erased = result.Erased, Bytes = result.Bytes, Skipped = result.Skipped, Problems = result.Problems.ToList() }, ct);
        }
        catch (ArgumentException ex)
        {
            await resp.WriteStringAsync(ex.Message, ct);
            resp.StatusCode = HttpStatusCode.BadRequest;
        }
        return resp;
    }
}
