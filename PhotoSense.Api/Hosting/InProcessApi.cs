using System.Net;
using System.Text;
using PhotoSense.Api.Http;

namespace PhotoSense.Api.Hosting;

/// <summary>An answer whole, as a program with no web server hands it back to the web view that asked.</summary>
/// <param name="Status">The HTTP status.</param>
/// <param name="Headers">The headers, each with its values joined.</param>
/// <param name="Body">What is sent.</param>
public sealed record InProcessAnswer(int Status, IReadOnlyDictionary<string, string> Headers, byte[] Body);

/// <summary>
/// Answers the page's requests inside the program that shows it, as the app on a phone does: there is no web
/// server there, only a web view that hands over each request it makes.
/// </summary>
public static class InProcessApi
{
    /// <summary>The service's answer to one request, or why there is none.</summary>
    /// <param name="router">The service's addresses.</param>
    /// <param name="method">GET, POST or DELETE.</param>
    /// <param name="url">The whole address asked for.</param>
    /// <param name="headers">The headers sent.</param>
    /// <param name="body">What was sent; null for nothing.</param>
    /// <param name="ct">Cancelled when the web view gives up on the request.</param>
    /// <exception cref="OperationCanceledException">Whoever asked has gone away.</exception>
    public static async Task<InProcessAnswer> AnswerAsync(this ApiRouter router, string method, Uri url,
        IEnumerable<KeyValuePair<string, string>> headers, byte[]? body, CancellationToken ct)
    {
        var request = new ApiRequest(method, url, body is null ? null : new MemoryStream(body, writable: false), ct);
        foreach (var (name, value) in headers) request.Headers.TryAddWithoutValidation(name, value);
        ApiResponse answer;
        try
        {
            if (router.Answer(request) is not { } answering) return Text(HttpStatusCode.NotFound, $"Nothing answers at {url.AbsolutePath}.");
            answer = await answering;
        }
        // One of the catches of every error PhotoSense keeps (CLAUDE.md): a web server answers an endpoint that fails with
        // an error of its own, but here nothing else would, and the page would wait for ever. A request given up on is no failure.
        catch (Exception ex) when (ex is not OperationCanceledException) { return Text(HttpStatusCode.InternalServerError, ex.Message); }

        var said = answer.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);
        return new InProcessAnswer((int)answer.StatusCode, said, ((MemoryStream)answer.Body).ToArray());
    }

    private static InProcessAnswer Text(HttpStatusCode status, string text)
        => new((int)status, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Content-Type"] = "text/plain; charset=utf-8" }, Encoding.UTF8.GetBytes(text));
}
