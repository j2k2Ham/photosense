using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using System.Net;

namespace PhotoSense.Functions.Scanning;

public class ScanHttpStarter
{
    [Function("StartPhotoScan")]
    public async Task<HttpResponseData> StartAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "scan/start")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        var body = await new StreamReader(req.Body).ReadToEndAsync();
        var (primary, secondary, recursive) = ParseBody(body);
        var instanceId = await client.ScheduleNewOrchestrationInstanceAsync(nameof(ScanOrchestrator.RunScanAsync), (primary, secondary, recursive));
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
            var primary = root.GetProperty("primary").GetString() ?? string.Empty;
            var secondary = root.GetProperty("secondary").GetString() ?? string.Empty;
            var recursive = root.TryGetProperty("recursive", out var r) && r.GetBoolean();
            return (primary, secondary, recursive);
        }
        catch
        {
            return (string.Empty, string.Empty, true);
        }
    }
}
