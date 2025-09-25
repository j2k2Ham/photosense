using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using System.Net;
using Microsoft.Extensions.Options;
using PhotoSense.Domain.Configuration;

namespace PhotoSense.Functions.Scanning;

public class ScanHttpStarter
{
    private readonly IOptions<PhotoStorageOptions> _options;
    public ScanHttpStarter(IOptions<PhotoStorageOptions> options) => _options = options;

    [Function("StartPhotoScan")]
    public async Task<HttpResponseData> StartAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "scan/start")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        var body = await new StreamReader(req.Body).ReadToEndAsync();
        var (primary, secondary, recursive) = ParseBody(body);
        primary = string.IsNullOrWhiteSpace(primary) ? _options.Value.PrimaryPath : primary;
        secondary = string.IsNullOrWhiteSpace(secondary) ? _options.Value.SecondaryPath : secondary;
        var instanceId = await client.ScheduleNewOrchestrationInstanceAsync(nameof(ScanOrchestrator.RunScanAsync), (primary, secondary, recursive));
        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteStringAsync(instanceId);
        return response;
    }

    [Function("StartSelfScan")]
    public async Task<HttpResponseData> StartSelfAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "scan/self")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        var body = await new StreamReader(req.Body).ReadToEndAsync();
        var (root, _, recursive) = ParseBody(body); // ignore secondary
        root = string.IsNullOrWhiteSpace(root) ? _options.Value.PrimaryPath : root;
        var instanceId = await client.ScheduleNewOrchestrationInstanceAsync(nameof(ScanOrchestrator.RunScanAsync), (root, string.Empty, recursive));
        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteStringAsync(instanceId);
        return response;
    }

    private static (string primary, string secondary, bool recursive) ParseBody(string body)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var root = doc.RootElement;
            var primary = root.TryGetProperty("primary", out var p) ? p.GetString() ?? "" : "";
            // allow root alias
            if (string.IsNullOrWhiteSpace(primary) && root.TryGetProperty("root", out var rroot)) primary = rroot.GetString() ?? "";
            var secondary = root.TryGetProperty("secondary", out var s) ? s.GetString() ?? "" : "";
            var recursive = root.TryGetProperty("recursive", out var r) && r.GetBoolean();
            return (primary, secondary, recursive);
        }
        catch
        {
            return ("", "", true);
        }
    }
}
