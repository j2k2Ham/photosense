using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;
using Microsoft.Extensions.Options;
using PhotoSense.Domain.Configuration;

namespace PhotoSense.Functions.Api;

public class HealthFunctions
{
    private readonly IOptions<PhotoStorageOptions> _storage;
    public HealthFunctions(IOptions<PhotoStorageOptions> storage) => _storage = storage;

    [Function("Health")]
    public async Task<HttpResponseData> GetAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req)
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
