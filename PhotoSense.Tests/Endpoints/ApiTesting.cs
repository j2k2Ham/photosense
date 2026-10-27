using System.Text;
using System.Text.Json;
using PhotoSense.Api.Endpoints;
using PhotoSense.Api.Http;

namespace PhotoSense.Tests.Endpoints;

/// <summary>Requests as a host hands them to the endpoints, built in memory, and what can be read off the answers.</summary>
internal static class Http
{
    public static ApiRequest Get(string pathAndQuery) => Make("GET", pathAndQuery);
    public static ApiRequest Post(string pathAndQuery, string? body = null) => Make("POST", pathAndQuery, body);
    public static ApiRequest Delete(string pathAndQuery) => Make("DELETE", pathAndQuery);
    /// <summary>A GET whose caller has already gone away.</summary>
    public static ApiRequest Abandoned(string pathAndQuery) => Make("GET", pathAndQuery, aborted: new CancellationToken(true));

    private static ApiRequest Make(string method, string pathAndQuery, string? body = null, CancellationToken aborted = default)
        => new(method, new Uri("http://localhost:7071/api/" + pathAndQuery.TrimStart('/')), new MemoryStream(Encoding.UTF8.GetBytes(body ?? string.Empty)), aborted);

    /// <summary>Marks the request as coming from the web client, as changes to photos must be.</summary>
    public static ApiRequest FromClient(this ApiRequest request, string? apiKey = null)
    {
        request.Headers.Add(RequestGuard.ClientHeader, "test");
        if (apiKey is not null) request.Headers.Add(RequestGuard.ApiKeyHeader, apiKey);
        return request;
    }

    public static ApiRequest With(this ApiRequest request, string header, string value)
    {
        request.Headers.Add(header, value);
        return request;
    }

    public static byte[] Bytes(this ApiResponse response) => ((MemoryStream)response.Body).ToArray();
    public static string Text(this ApiResponse response) => Encoding.UTF8.GetString(response.Bytes());
    public static JsonElement Json(this ApiResponse response) => JsonDocument.Parse(response.Bytes()).RootElement;
    public static string? Header(this ApiResponse response, string name)
        => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
}
