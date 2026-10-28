using System.Net;
using PhotoSense.Api.Http;
using PhotoSense.Contracts.Library;
using PhotoSense.Domain.Services;

namespace PhotoSense.Api.Endpoints;

/// <summary>What the person has let PhotoSense see of a phone's photos, and the system's ways of letting it see more.</summary>
public sealed class LibraryEndpoints
{
    private readonly IPhotoAccess _access;

    public LibraryEndpoints(IPhotoAccess access) => _access = access;

    // GET /api/library/access
    public async Task<ApiResponse> AccessAsync(ApiRequest req)
    {
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(new PhotoAccessDto { Access = _access.Current.ToString().ToLowerInvariant() });
        return resp;
    }

    // POST /api/library/choose-more: answered once the system's picker has been put away, with the access then.
    public async Task<ApiResponse> ChooseMoreAsync(ApiRequest req)
    {
        // Only the page itself may put the system's picker in front of someone.
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        await _access.ChooseMoreAsync(req.Aborted);
        return await AccessAsync(req);
    }

    // POST /api/library/open-settings
    public ApiResponse OpenSettings(ApiRequest req)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        _access.OpenSettings();
        return req.CreateResponse(HttpStatusCode.NoContent);
    }
}
