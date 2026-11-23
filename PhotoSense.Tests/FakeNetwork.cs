using System.Net;
using System.Text;

namespace PhotoSense.Tests;

/// <summary>A request as the fake network heard it.</summary>
/// <param name="Method">Its method.</param>
/// <param name="Url">Its address, whole.</param>
/// <param name="Headers">Its headers and its body's, each with its values joined.</param>
/// <param name="Body">Its body as text; empty when it had none.</param>
/// <param name="Bytes">Its body as sent.</param>
internal sealed record Heard(HttpMethod Method, string Url, IReadOnlyDictionary<string, string> Headers, string Body, byte[] Bytes);

/// <summary>
/// The network, as the test sets it up: each request is answered by the last answer added that takes it, or not found.
/// Every request is kept, to look at after.
/// </summary>
internal sealed class FakeNetwork : HttpMessageHandler
{
    private readonly List<(HttpMethod? Method, string Start, Func<Heard, HttpResponseMessage> Answer)> _answers = [];

    /// <summary>Every request, in order.</summary>
    public List<Heard> Asked { get; } = [];

    /// <summary>The network is down: every request fails.</summary>
    public bool Down { get; set; }

    /// <summary>Answers requests of this method to addresses that start so; null takes any method.</summary>
    public FakeNetwork On(HttpMethod? method, string start, Func<Heard, HttpResponseMessage> answer)
    {
        _answers.Add((method, start, answer));
        return this;
    }

    /// <summary>Answers requests to addresses that start so with this JSON, whatever their method.</summary>
    public FakeNetwork On(string start, string json, HttpStatusCode status = HttpStatusCode.OK) => On(null, start, _ => Json(json, status));

    /// <summary>An answer with a JSON body.</summary>
    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json"), ReasonPhrase = status.ToString() };

    /// <summary>An answer with these bytes.</summary>
    public static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    /// <summary>An answer with no body.</summary>
    public static HttpResponseMessage Status(HttpStatusCode status) => new(status) { ReasonPhrase = status.ToString() };

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var bytes = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var heard = new Heard(request.Method, request.RequestUri!.AbsoluteUri, headers, Encoding.UTF8.GetString(bytes), bytes);
        lock (Asked) Asked.Add(heard);
        if (Down) throw new HttpRequestException("No route to host.");
        for (var i = _answers.Count - 1; i >= 0; i--)
        {
            var (method, start, answer) = _answers[i];
            if ((method is null || method == request.Method) && heard.Url.StartsWith(start, StringComparison.Ordinal)) return answer(heard);
        }
        return Status(HttpStatusCode.NotFound);
    }
}
