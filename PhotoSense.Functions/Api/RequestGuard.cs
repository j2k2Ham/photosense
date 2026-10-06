using System.Net.Http.Headers;

namespace PhotoSense.Functions.Api;

public static class RequestGuard
{
    public const string ClientHeader = "x-photosense-client";
    public const string ApiKeyHeader = "x-api-key";

    /// <summary>Whether a request may change, move or remove photos.</summary>
    /// <param name="configuredKey">The API key the server was started with, if any.</param>
    public static bool Allows(HttpHeaders headers, string? configuredKey)
    {
        // Any web page can make a browser send a plain request to a server on this machine. It cannot make
        // the browser add a custom header: for that the browser first asks this server, which only grants
        // it to the UI's own address (Host:CORS in local.settings.json).
        if (!headers.Contains(ClientHeader)) return false;
        if (string.IsNullOrEmpty(configuredKey)) return true;
        return headers.TryGetValues(ApiKeyHeader, out var values) && values.FirstOrDefault() == configuredKey;
    }
}
