using System.Net;
using System.Text.Json;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PhotoSense.Application.Removal;
using PhotoSense.Contracts.Removed;

namespace PhotoSense.Functions.Api;

/// <summary>What Clean up and Organize have removed: how much is waiting in the folders that hold it, and erasing it for good.</summary>
public class RemovedFunctions
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly RemovedFiles _removed;

    public RemovedFunctions(RemovedFiles removed) => _removed = removed;

    [Function("RemovedFiles")] // GET /api/removed?root=&root=
    public async Task<HttpResponseData> FindAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "removed")] HttpRequestData req)
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
    [Function("EraseRemoved")] // POST /api/removed/erase
    public async Task<HttpResponseData> EraseAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "removed/erase")] HttpRequestData req)
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
