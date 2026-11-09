using System.Net;
using Microsoft.Extensions.Options;
using PhotoSense.Domain.Configuration;
using PhotoSense.Api.Http;

namespace PhotoSense.Api.Endpoints;

/// <summary>Whether the service is up, for whatever checks on it.</summary>
internal sealed class HealthEndpoints
{
    private readonly IOptions<PhotoStorageOptions> _storage;

    /// <summary>Reports on the settings given.</summary>
    public HealthEndpoints(IOptions<PhotoStorageOptions> storage) => _storage = storage;

    /// <summary>GET /api/health: "ok", and whether folders to scan are set.</summary>
    public async Task<ApiResponse> GetAsync(ApiRequest req, CancellationToken ct)
    {
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new {
            status = "ok",
            storagePrimaryConfigured = !string.IsNullOrWhiteSpace(_storage.Value.PrimaryPath),
            storageSecondaryConfigured = !string.IsNullOrWhiteSpace(_storage.Value.SecondaryPath)
        }, ct);
        return resp;
    }
}
