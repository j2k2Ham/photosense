using System.Net;
using System.Text;
using PhotoSense.Api.Http;

namespace PhotoSense.Api.Hosting;

/// <summary>An answer whole, as a program with no web server hands it back to the web view that asked.</summary>
public sealed record InProcessAnswer(int Status, IReadOnlyDictionary<string, string> Headers, byte[] Body);

/// <summary>
/// Answers the page's requests inside the program that shows it, as the app on a phone does: there is no web
/// server there, only a web view that hands over each request it makes.
/// </summary>
public static class InProcessApi
{
    /// <summary>The service's answer to one request, or why there is none.</summary>
    /// <exception cref="OperationCanceledException">Whoever asked has gone away.</exception>
    public static async Task<InProcessAnswer> AnswerAsync(this ApiRouter router, string method, Uri url,
        IEnumerable<KeyValuePair<string, string>> headers, byte[]? body, CancellationToken ct = default)
    {
        var request = new ApiRequest(method, url, body is null ? null : new MemoryStream(body, writable: false), ct);
        foreach (var (name, value) in headers) request.Headers.TryAddWithoutValidation(name, value);
        ApiResponse answer;
        try
        {
            if (router.Answer(request) is not { } answering) return Text(HttpStatusCode.NotFound, $"Nothing answers at {url.AbsolutePath}.");
            answer = await answering;
        }
        // A web server would answer a failure with an error of its own; here nothing else would, and the page would wait.
        catch (Exception ex) when (ex is not OperationCanceledException) { return Text(HttpStatusCode.InternalServerError, ex.Message); }

        var said = answer.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);
        return new InProcessAnswer((int)answer.StatusCode, said, ((MemoryStream)answer.Body).ToArray());
    }

    private static InProcessAnswer Text(HttpStatusCode status, string text)
        => new((int)status, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Content-Type"] = "text/plain; charset=utf-8" }, Encoding.UTF8.GetBytes(text));
}
