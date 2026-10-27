using System.Net;
using System.Text.Json;
using System.Web;
using PhotoSense.Application.Removal;
using PhotoSense.Contracts.Removed;
using PhotoSense.Api.Http;

namespace PhotoSense.Api.Endpoints;

/// <summary>What Clean up and Organize have removed: how much is waiting in the folders that hold it, and erasing it for good.</summary>
public class RemovedEndpoints
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly RemovedFiles _removed;

    public RemovedEndpoints(RemovedFiles removed) => _removed = removed;

    // GET /api/removed?root=&root=
    public async Task<ApiResponse> FindAsync(
        ApiRequest req)
    {
        // Where someone's files are is told to the UI itself and to no other web page.
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var folders = await _removed.FindAsync(HttpUtility.ParseQueryString(req.Url.Query).GetValues("root") ?? []);
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(new RemovedFilesDto
        {
            Folders = folders.Select(f => new RemovedFolderDto { Path = f.Path, Files = f.Files, Bytes = f.Bytes }).ToList(),
            Files = folders.Sum(f => f.Files), Bytes = folders.Sum(f => f.Bytes),
        });
        return resp;
    }

    // The one request that erases files. It names the folders as they were listed, and only folders of removed files are ever emptied.
    // POST /api/removed/erase
    public async Task<ApiResponse> EraseAsync(
        ApiRequest req)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        EraseRemovedRequest? request = null;
        try { request = JsonSerializer.Deserialize<EraseRemovedRequest>(await new StreamReader(req.Body).ReadToEndAsync(), Json); }
        catch (JsonException) { /* said below */ }
        var resp = req.CreateResponse();
        try
        {
            if (request?.Folders is null) throw new ArgumentException("The request could not be read.");
            var result = await _removed.EraseAsync(request.Folders);
            await resp.WriteAsJsonAsync(new EraseRemovedDto { Erased = result.Erased, Bytes = result.Bytes, Skipped = result.Skipped, Problems = result.Problems.ToList() });
        }
        catch (ArgumentException ex)
        {
            await resp.WriteStringAsync(ex.Message);
            resp.StatusCode = HttpStatusCode.BadRequest;
        }
        return resp;
    }
}
