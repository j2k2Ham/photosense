using System.Net;
using Microsoft.Extensions.Options;
using PhotoSense.Domain.Configuration;
using PhotoSense.Api.Http;

namespace PhotoSense.Api.Endpoints;

public class HealthEndpoints
{
    private readonly IOptions<PhotoStorageOptions> _storage;
    public HealthEndpoints(IOptions<PhotoStorageOptions> storage) => _storage = storage;

    public async Task<ApiResponse> GetAsync(ApiRequest req)
    {
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new {
            status = "ok",
            storagePrimaryConfigured = !string.IsNullOrWhiteSpace(_storage.Value.PrimaryPath),
            storageSecondaryConfigured = !string.IsNullOrWhiteSpace(_storage.Value.SecondaryPath)
        });
        return resp;
    }
}
