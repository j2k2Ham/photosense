using System.Net;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PhotoSense.Contracts.Folders;
using PhotoSense.Domain.Services;

namespace PhotoSense.Functions.Api;

public class FoldersFunctions
{
    private readonly IFolderBrowser _browser;

    public FoldersFunctions(IFolderBrowser browser) => _browser = browser;

    [Function("BrowseFolders")] // GET /api/folders?path=
    public async Task<HttpResponseData> BrowseAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "folders")] HttpRequestData req)
    {
        var resp = req.CreateResponse();
        // The names of someone's folders are told to the UI itself and to no other web page.
        if (!RequestGuard.Allows(req.Headers)) { resp.StatusCode = HttpStatusCode.Unauthorized; return resp; }

        var path = HttpUtility.ParseQueryString(req.Url.Query).Get("path");
        try
        {
            var listing = _browser.Browse(path);
            await resp.WriteAsJsonAsync(new FolderListingDto
            {
                Path = listing.Path,
                Parent = listing.Parent,
                Folders = listing.Folders.Select(f => new FolderDto { Name = f.Name, Path = f.Path }).ToList()
            });
        }
        // Not there, or not something that could be a path at all.
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            await resp.WriteStringAsync($"Folder not found: {path}");
            resp.StatusCode = HttpStatusCode.NotFound;
        }
        return resp;
    }
}
