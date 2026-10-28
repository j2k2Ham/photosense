using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PhotoSense.Api.Endpoints;
using PhotoSense.Api.Http;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Services;

namespace PhotoSense.Api.Scanning;

public class ScanStartEndpoints
{
    private readonly IOptions<PhotoStorageOptions> _options;
    private readonly ScanRunner _runner;
    private readonly IPhotoFileSystem _files;
    public ScanStartEndpoints(IOptions<PhotoStorageOptions> options, ScanRunner runner, IPhotoFileSystem files) { _options = options; _runner = runner; _files = files; }

    // POST /api/scan/start
    public async Task<ApiResponse> StartAsync(ApiRequest req)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var body = await new StreamReader(req.Body).ReadToEndAsync();
        var request = ParseBody(body, _options.Value);

        // Paths are resolved on this machine, so say so when one is not there instead of scanning nothing.
        var missing = new[] { request.PrimaryPath, request.SecondaryPath }
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && !_files.FolderExists(p));
        if (string.IsNullOrWhiteSpace(request.PrimaryPath) || missing is not null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = missing is null ? "A folder to scan is required." : $"Folder not found on the server: {missing}" });
            return bad;
        }

        if (_runner.Start(request) is not { } instanceId)
        {
            var busy = req.CreateResponse(HttpStatusCode.Conflict);
            await busy.WriteAsJsonAsync(new { error = "A scan is already running. Wait for it to finish." });
            return busy;
        }
        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new { instanceId });
        return response;
    }

    public static ScanRequest ParseBody(string body, PhotoStorageOptions defaults)
    {
        string? primary = null, secondary = null;
        bool recursive = true, startOver = false;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                primary = Text(root, "primaryLocation") ?? Text(root, "primary");
                secondary = Text(root, "secondaryLocation") ?? Text(root, "secondary");
                if (root.TryGetProperty("recursive", out var r) && r.ValueKind is JsonValueKind.True or JsonValueKind.False) recursive = r.GetBoolean();
                startOver = root.TryGetProperty("startOver", out var s) && s.ValueKind == JsonValueKind.True;
            }
        }
        catch (JsonException) { /* no usable body: scan the configured folders */ }

        return new ScanRequest(
            string.IsNullOrWhiteSpace(primary) ? defaults.PrimaryPath : primary.Trim(),
            string.IsNullOrWhiteSpace(secondary) ? NullIfBlank(defaults.SecondaryPath) : secondary.Trim(),
            recursive,
            startOver);
    }

    private static string? Text(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
