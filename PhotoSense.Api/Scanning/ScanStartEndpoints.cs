using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PhotoSense.Api.Endpoints;
using PhotoSense.Api.Http;
using PhotoSense.Application.Scanning;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Services;

namespace PhotoSense.Api.Scanning;

/// <summary>Starts a scan, which runs on in the background.</summary>
internal sealed class ScanStartEndpoints
{
    private readonly IOptions<PhotoStorageOptions> _options;
    private readonly ScanRunner _runner;
    private readonly IPhotoFileSystem _files;

    /// <summary>Starts scans through <paramref name="runner"/>, of the folders asked for or, failing that, those set in <paramref name="options"/>.</summary>
    public ScanStartEndpoints(IOptions<PhotoStorageOptions> options, ScanRunner runner, IPhotoFileSystem files) { _options = options; _runner = runner; _files = files; }

    /// <summary>POST /api/scan/start: starts a scan of the folders asked for, and says what it is known by.</summary>
    public async Task<ApiResponse> StartAsync(ApiRequest req, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var body = await new StreamReader(req.Body).ReadToEndAsync(ct);
        var request = ParseBody(body, _options.Value);

        // Paths are resolved on this machine, so say so when one is not there instead of scanning nothing.
        var missing = new[] { request.PrimaryPath, request.SecondaryPath }
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && !_files.FolderExists(p));
        if (string.IsNullOrWhiteSpace(request.PrimaryPath) || missing is not null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = missing is null ? "A folder to scan is required." : $"Folder not found on the server: {missing}" }, ct);
            return bad;
        }

        if (_runner.Start(request) is not { } instanceId)
        {
            var busy = req.CreateResponse(HttpStatusCode.Conflict);
            await busy.WriteAsJsonAsync(new { error = "A scan is already running. Wait for it to finish." }, ct);
            return busy;
        }
        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new { instanceId }, ct);
        return response;
    }

    /// <summary>POST /api/scan/stop/{instanceId}: stops that scan, keeping what it has read; 404 when it is not running.</summary>
    public async Task<ApiResponse> StopAsync(ApiRequest req, string instanceId, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        if (_runner.Stop(instanceId)) return req.CreateResponse(HttpStatusCode.NoContent);
        var gone = req.CreateResponse(HttpStatusCode.NotFound);
        await gone.WriteAsJsonAsync(new { error = "That scan is not running." }, ct);
        return gone;
    }

    /// <summary>What a request asks to scan; what it leaves out, or a body that is not JSON at all, means the folders set in <paramref name="defaults"/>.</summary>
    /// <param name="body">The request's body.</param>
    /// <param name="defaults">The folders to scan when none is named.</param>
    public static ScanRequest ParseBody(string body, PhotoStorageOptions defaults)
    {
        string? primary = null, secondary = null;
        bool recursive = true, startOver = false;
        using var doc = ApiRequest.FromJson<JsonDocument>(body, JsonSerializerOptions.Default);
        if (doc?.RootElement is { ValueKind: JsonValueKind.Object } root)
        {
            primary = Text(root, "primaryLocation") ?? Text(root, "primary");
            secondary = Text(root, "secondaryLocation") ?? Text(root, "secondary");
            if (root.TryGetProperty("recursive", out var r) && r.ValueKind is JsonValueKind.True or JsonValueKind.False) recursive = r.GetBoolean();
            startOver = root.TryGetProperty("startOver", out var s) && s.ValueKind == JsonValueKind.True;
        }

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
