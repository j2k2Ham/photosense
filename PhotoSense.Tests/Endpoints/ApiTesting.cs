using System.Text;
using System.Text.Json;
using PhotoSense.Api.Endpoints;
using PhotoSense.Api.Http;

namespace PhotoSense.Tests.Endpoints;

/// <summary>Requests as a host hands them to the endpoints, built in memory, and what can be read off the answers.</summary>
internal static class Http
{
    /// <summary>A GET of an address below /api.</summary>
    public static ApiRequest Get(string pathAndQuery) => Make("GET", pathAndQuery);
    /// <summary>A POST of an address below /api, with a body when one is given.</summary>
    public static ApiRequest Post(string pathAndQuery, string? body = null) => Make("POST", pathAndQuery, body);
    /// <summary>A DELETE of an address below /api.</summary>
    public static ApiRequest Delete(string pathAndQuery) => Make("DELETE", pathAndQuery);
    private static ApiRequest Make(string method, string pathAndQuery, string? body = null)
        => new(method, new Uri("http://localhost:7071/api/" + pathAndQuery.TrimStart('/')), new MemoryStream(Encoding.UTF8.GetBytes(body ?? string.Empty)));

    /// <summary>Marks the request as coming from the web client, as changes to photos must be.</summary>
    public static ApiRequest FromClient(this ApiRequest request, string? apiKey = null)
    {
        request.Headers.Add(RequestGuard.ClientHeader, "test");
        if (apiKey is not null) request.Headers.Add(RequestGuard.ApiKeyHeader, apiKey);
        return request;
    }

    /// <summary>Adds a header to the request.</summary>
    public static ApiRequest With(this ApiRequest request, string header, string value)
    {
        request.Headers.Add(header, value);
        return request;
    }

    /// <summary>What the answer holds.</summary>
    public static byte[] Bytes(this ApiResponse response) => ((MemoryStream)response.Body).ToArray();
    /// <summary>What the answer holds, as text.</summary>
    public static string Text(this ApiResponse response) => Encoding.UTF8.GetString(response.Bytes());
    /// <summary>What the answer holds, as JSON.</summary>
    public static JsonElement Json(this ApiResponse response) => JsonDocument.Parse(response.Bytes()).RootElement;
    /// <summary>A header of the answer, or null when it has none.</summary>
    public static string? Header(this ApiResponse response, string name)
        => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
}
