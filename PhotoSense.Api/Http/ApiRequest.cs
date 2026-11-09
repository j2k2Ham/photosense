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
    /// <summary>A request as the program that received it hands it over.</summary>
    /// <param name="method">GET, POST or DELETE.</param>
    /// <param name="url">The whole address asked for, query included.</param>
    /// <param name="body">What was sent with it; nothing when null.</param>
    /// <param name="aborted">Cancelled when whoever asked has gone away.</param>
    public ApiRequest(string method, Uri url, Stream? body = null, CancellationToken aborted = default)
    {
        Method = method;
        Url = url;
        Body = body ?? Stream.Null;
        Aborted = aborted;
    }

    /// <summary>GET, POST or DELETE.</summary>
    public string Method { get; }
    /// <summary>The whole address asked for, query included.</summary>
    public Uri Url { get; }
    /// <summary>The headers sent with it.</summary>
    public ApiHeaders Headers { get; } = new();
    /// <summary>What was sent with it.</summary>
    public Stream Body { get; }
    /// <summary>Cancelled when whoever asked has gone away; the router hands it to the endpoint that answers.</summary>
    public CancellationToken Aborted { get; }

    /// <summary>A new answer to this request, with the status given.</summary>
    public ApiResponse CreateResponse(HttpStatusCode status = HttpStatusCode.OK) => new() { StatusCode = status };

    /// <summary>What was sent, read as JSON; null when it is not JSON of that shape, which the caller says.</summary>
    /// <param name="options">How the JSON is read.</param>
    /// <param name="ct">Stops the reading.</param>
    public async Task<T?> ReadJsonAsync<T>(JsonSerializerOptions options, CancellationToken ct) where T : class
        => FromJson<T>(await new StreamReader(Body).ReadToEndAsync(ct), options);

    /// <summary>Text read as JSON; null when it is not JSON of that shape.</summary>
    /// <param name="text">The text.</param>
    /// <param name="options">How the JSON is read.</param>
    public static T? FromJson<T>(string text, JsonSerializerOptions options) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(text, options); }
        // Text that is not JSON of that shape asks for nothing; whoever asked is told what was wrong with it.
        catch (JsonException) { return null; }
    }
}

/// <summary>An answer, held whole until the program that received the request sends it.</summary>
public sealed class ApiResponse
{
    // Property names go out as they are written, as they always have: the page reads them that way.
    private static readonly JsonSerializerOptions Json = new();
    private readonly MemoryStream _body = new();

    /// <summary>The status sent; it stays as set, before or after anything is written.</summary>
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
    /// <summary>The headers sent.</summary>
    public ApiHeaders Headers { get; } = new();
    /// <summary>What is sent, as written so far.</summary>
    public Stream Body => _body;

    /// <summary>Writes text, as plain text unless another type was set.</summary>
    public async Task WriteStringAsync(string text, CancellationToken ct)
    {
        if (!Headers.Contains("Content-Type")) Headers.Add("Content-Type", "text/plain; charset=utf-8");
        await _body.WriteAsync(Encoding.UTF8.GetBytes(text), ct);
    }

    /// <summary>Writes a value as JSON, with its property names as declared.</summary>
    public async Task WriteAsJsonAsync<T>(T value, CancellationToken ct)
    {
        if (!Headers.Contains("Content-Type")) Headers.Add("Content-Type", "application/json; charset=utf-8");
        // As what it is rather than as what it was declared: a list of objects keeps each object's fields.
        await JsonSerializer.SerializeAsync<object?>(_body, value, Json, ct);
    }
}
