using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Trovolve.Api.Http;

/// <summary>
/// The service's addresses, each with what answers it. A template is a path below /api with literal
/// segments and named ones: "photos/{id:guid}/image", "organize/undo/{batchId}". Routes are tried in the
/// order they were added, so a literal address is added before one with a part that could match it.
/// </summary>
/// <remarks>
/// What answers is given the request, the values of the named segments, and the request's token, which is cancelled when
/// whoever asked has gone away. Each answer is written to the log at Debug, by its route, and told to whoever watches the
/// routes used (usage metrics): by its method and template, such as "POST scan/start", never with the values in its address.
/// </remarks>
public sealed class ApiRouter
{
    private const string Prefix = "/api/";
    private readonly List<(string Route, Segment[] Segments, Func<ApiRequest, IReadOnlyDictionary<string, string>, CancellationToken, Task<ApiResponse>> Answer)> _routes = [];
    private readonly TimeProvider _time;
    private readonly Func<string, int, CancellationToken, Task>? _used;

    private sealed record Segment(string Text, bool IsValue, bool IsGuid);

    /// <summary>Addresses that write nothing to a log and tell no one what was used.</summary>
    public ApiRouter() : this(NullLogger.Instance, TimeProvider.System, null) { }

    /// <summary>Addresses that write each answer to <paramref name="log"/>, and tell <paramref name="used"/> each route answered.</summary>
    /// <param name="log">The program's log: each answer at Debug, a failure nothing else would catch at Error (<see cref="Hosting.InProcessApi"/>).</param>
    /// <param name="time">How long each answer took.</param>
    /// <param name="used">Told of each route answered, by its method and template, with the answer's status; null for no one.</param>
    public ApiRouter(ILogger log, TimeProvider time, Func<string, int, CancellationToken, Task>? used)
    {
        Log = log;
        _time = time;
        _used = used;
    }

    /// <summary>The program's log, for the host that answers through these addresses.</summary>
    internal ILogger Log { get; }

    /// <summary>Every route, by its method and template ("POST scan/start"), in the order they are tried.</summary>
    public IReadOnlyList<string> Routes => [.. _routes.Select(r => r.Route)];

    /// <summary>Adds an address that answers GET.</summary>
    public ApiRouter Get(string template, Func<ApiRequest, IReadOnlyDictionary<string, string>, CancellationToken, Task<ApiResponse>> answer) => Add("GET", template, answer);
    /// <summary>Adds an address that answers POST.</summary>
    public ApiRouter Post(string template, Func<ApiRequest, IReadOnlyDictionary<string, string>, CancellationToken, Task<ApiResponse>> answer) => Add("POST", template, answer);
    /// <summary>Adds an address that answers DELETE.</summary>
    public ApiRouter Delete(string template, Func<ApiRequest, IReadOnlyDictionary<string, string>, CancellationToken, Task<ApiResponse>> answer) => Add("DELETE", template, answer);

    private ApiRouter Add(string method, string template, Func<ApiRequest, IReadOnlyDictionary<string, string>, CancellationToken, Task<ApiResponse>> answer)
    {
        var segments = template.Split('/').Select(s => s.StartsWith('{')
            ? new Segment(s.Trim('{', '}').Split(':')[0], IsValue: true, IsGuid: s.Contains(":guid"))
            : new Segment(s, IsValue: false, IsGuid: false)).ToArray();
        _routes.Add(($"{method} {template}", segments, answer));
        return this;
    }

    /// <summary>The answer of the first route that matches, or null when none does.</summary>
    public Task<ApiResponse>? Answer(ApiRequest request)
    {
        var path = request.Url.AbsolutePath;
        if (!path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var parts = path[Prefix.Length..].TrimEnd('/').Split('/');
        foreach (var (route, segments, answer) in _routes)
            if (route.StartsWith($"{request.Method.ToUpperInvariant()} ", StringComparison.Ordinal) && Match(segments, parts) is { } values)
                return AnsweredAsync(route, answer, request, values);
        return null;
    }

    private async Task<ApiResponse> AnsweredAsync(string route, Func<ApiRequest, IReadOnlyDictionary<string, string>, CancellationToken, Task<ApiResponse>> answer,
        ApiRequest request, IReadOnlyDictionary<string, string> values)
    {
        var started = _time.GetTimestamp();
        var answered = await answer(request, values, request.Aborted);
        ApiLog.Answered(Log, route, (int)answered.StatusCode, _time.GetElapsedTime(started).TotalMilliseconds);
        if (_used is not null) await _used(route, (int)answered.StatusCode, request.Aborted);
        return answered;
    }

    private static Dictionary<string, string>? Match(Segment[] segments, string[] parts)
    {
        if (parts.Length != segments.Length) return null;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < parts.Length; i++)
        {
            var part = Uri.UnescapeDataString(parts[i]);
            var segment = segments[i];
            if (!segment.IsValue)
            {
                if (!string.Equals(segment.Text, part, StringComparison.OrdinalIgnoreCase)) return null;
                continue;
            }
            if (part.Length == 0 || (segment.IsGuid && !Guid.TryParse(part, out _))) return null;
            values[segment.Text] = part;
        }
        return values;
    }
}
