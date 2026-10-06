using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Configuration;

namespace PhotoSense.Functions.Scanning;

public class ScanHttpStarter
{
    private readonly IOptions<PhotoStorageOptions> _options;
    private readonly IScanProgressStore _progress;
    public ScanHttpStarter(IOptions<PhotoStorageOptions> options, IScanProgressStore progress) { _options = options; _progress = progress; }

    [Function("StartPhotoScan")]
    public async Task<HttpResponseData> StartAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "scan/start")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        var body = await new StreamReader(req.Body).ReadToEndAsync();
        var request = ParseBody(body, _options.Value);

        // Paths are resolved on this machine, so say so when one is not there instead of scanning nothing.
        var missing = new[] { request.PrimaryPath, request.SecondaryPath }
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && !Directory.Exists(p));
        if (string.IsNullOrWhiteSpace(request.PrimaryPath) || missing is not null)
        {
            var bad = req.CreateResponse();
            await bad.WriteAsJsonAsync(new { error = missing is null ? "A folder to scan is required." : $"Folder not found on the server: {missing}" });
            bad.StatusCode = HttpStatusCode.BadRequest; // set after writing: WriteAsJsonAsync resets it to 200
            return bad;
        }

        // Two scans at once would each forget the files the other had found. Requests are taken one at a
        // time and the scan is marked as started here, before its activity begins, so a second click cannot slip in.
        await StartGate.WaitAsync();
        try
        {
            if (IsRunning(_progress.GetLatest()))
            {
                var busy = req.CreateResponse();
                await busy.WriteAsJsonAsync(new { error = "A scan is already running. Wait for it to finish." });
                busy.StatusCode = HttpStatusCode.Conflict;
                return busy;
            }

            var instanceId = await client.ScheduleNewOrchestrationInstanceAsync(nameof(ScanOrchestrator.RunScanAsync), request);
            _progress.ScanStarted(instanceId);
            var response = req.CreateResponse();
            await response.WriteAsJsonAsync(new { instanceId });
            response.StatusCode = HttpStatusCode.Accepted;
            return response;
        }
        finally { StartGate.Release(); }
    }

    private static readonly SemaphoreSlim StartGate = new(1, 1);

    public static bool IsRunning(ScanProgressSnapshot latest) => latest.InstanceId.Length > 0 && latest.CompletedUtc is null;

    public static ScanRequest ParseBody(string body, PhotoStorageOptions defaults)
    {
        string? primary = null, secondary = null;
        var recursive = true;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                primary = Text(root, "primaryLocation") ?? Text(root, "primary");
                secondary = Text(root, "secondaryLocation") ?? Text(root, "secondary");
                if (root.TryGetProperty("recursive", out var r) && r.ValueKind is JsonValueKind.True or JsonValueKind.False) recursive = r.GetBoolean();
            }
        }
        catch (JsonException) { /* no usable body: scan the configured folders */ }

        return new ScanRequest(
            string.IsNullOrWhiteSpace(primary) ? defaults.PrimaryPath : primary.Trim(),
            string.IsNullOrWhiteSpace(secondary) ? NullIfBlank(defaults.SecondaryPath) : secondary.Trim(),
            recursive);
    }

    private static string? Text(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
