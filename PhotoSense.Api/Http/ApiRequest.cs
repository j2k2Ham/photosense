using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PhotoSense.Api.Http;

/// <summary>Headers of a request or a response. Any header may be set, as either can carry it.</summary>
public sealed class ApiHeaders : HttpHeaders;

/// <summary>A request as the endpoints see it, whichever program received it.</summary>
public sealed class ApiRequest
{
    /// <param name="url">The whole address asked for, query included.</param>
    /// <param name="aborted">Cancelled when whoever asked has gone away.</param>
    public ApiRequest(string method, Uri url, Stream? body = null, CancellationToken aborted = default)
    {
        Method = method;
        Url = url;
        Body = body ?? Stream.Null;
        Aborted = aborted;
    }

    public string Method { get; }
    public Uri Url { get; }
    public ApiHeaders Headers { get; } = new();
    public Stream Body { get; }
    public CancellationToken Aborted { get; }

    public ApiResponse CreateResponse(HttpStatusCode status = HttpStatusCode.OK) => new() { StatusCode = status };
}

/// <summary>An answer, held whole until the program that received the request sends it.</summary>
public sealed class ApiResponse
{
    // Property names go out as they are written, as they always have: the page reads them that way.
    private static readonly JsonSerializerOptions Json = new();
    private readonly MemoryStream _body = new();

    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
    public ApiHeaders Headers { get; } = new();
    public Stream Body => _body;

    public async Task WriteStringAsync(string text)
    {
        if (!Headers.Contains("Content-Type")) Headers.Add("Content-Type", "text/plain; charset=utf-8");
        await _body.WriteAsync(Encoding.UTF8.GetBytes(text));
    }

    public async Task WriteAsJsonAsync<T>(T value)
    {
        if (!Headers.Contains("Content-Type")) Headers.Add("Content-Type", "application/json; charset=utf-8");
        // As what it is rather than as what it was declared: a list of objects keeps each object's fields.
        await JsonSerializer.SerializeAsync<object?>(_body, value, Json);
    }
}
