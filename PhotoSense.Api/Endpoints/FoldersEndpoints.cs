using System.Net;
using System.Web;
using PhotoSense.Contracts.Folders;
using PhotoSense.Domain.Services;
using PhotoSense.Api.Http;

namespace PhotoSense.Api.Endpoints;

public class FoldersEndpoints
{
    private readonly IFolderBrowser _browser;

    public FoldersEndpoints(IFolderBrowser browser) => _browser = browser;

    // GET /api/folders?path=
    public async Task<ApiResponse> BrowseAsync(
        ApiRequest req)
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
