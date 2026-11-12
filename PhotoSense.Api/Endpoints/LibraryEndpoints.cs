using System.Net;
using PhotoSense.Api.Http;
using PhotoSense.Contracts.Library;
using PhotoSense.Domain.Services;

namespace PhotoSense.Api.Endpoints;

/// <summary>What the person has let PhotoSense see of a phone's photos, and the system's ways of letting it see more.</summary>
internal sealed class LibraryEndpoints
{
    private readonly IPhotoAccess _access;

    /// <summary>Access as <paramref name="access"/> says it.</summary>
    public LibraryEndpoints(IPhotoAccess access) => _access = access;

    /// <summary>GET /api/library/access: everything, selected or nothing.</summary>
    public async Task<ApiResponse> AccessAsync(ApiRequest req, CancellationToken ct)
    {
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(new PhotoAccessDto { Access = _access.Current.ToString().ToLowerInvariant() }, ct);
        return resp;
    }

    /// <summary>POST /api/library/ask: answered once the system's question of what PhotoSense may see is answered, with the access then.</summary>
    public async Task<ApiResponse> AskAsync(ApiRequest req, CancellationToken ct)
    {
        // Only the page itself may put the system's question in front of someone.
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        await _access.AskAsync(ct);
        return await AccessAsync(req, ct);
    }

    /// <summary>POST /api/library/choose-more: answered once the system's picker has been put away, with the access then.</summary>
    public async Task<ApiResponse> ChooseMoreAsync(ApiRequest req, CancellationToken ct)
    {
        // Only the page itself may put the system's picker in front of someone.
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        await _access.ChooseMoreAsync(ct);
        return await AccessAsync(req, ct);
    }

    /// <summary>POST /api/library/open-settings: opens PhotoSense's page of the system's settings.</summary>
    public ApiResponse OpenSettings(ApiRequest req)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        _access.OpenSettings();
        return req.CreateResponse(HttpStatusCode.NoContent);
    }
}
