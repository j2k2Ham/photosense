namespace PhotoSense.Api.Http;

/// <summary>
/// The service's addresses, each with what answers it. A template is a path below /api with literal
/// segments and named ones: "photos/{id:guid}/image", "scan/logs/{instanceId?}". Routes are tried in the
/// order they were added, so a literal address is added before one with a part that could match it.
/// </summary>
public sealed class ApiRouter
{
    private const string Prefix = "/api/";
    private readonly List<(string Method, Segment[] Segments, Func<ApiRequest, IReadOnlyDictionary<string, string>, Task<ApiResponse>> Answer)> _routes = [];

    private sealed record Segment(string Text, bool IsValue, bool IsGuid, bool Optional);

    public ApiRouter Get(string template, Func<ApiRequest, IReadOnlyDictionary<string, string>, Task<ApiResponse>> answer) => Add("GET", template, answer);
    public ApiRouter Post(string template, Func<ApiRequest, IReadOnlyDictionary<string, string>, Task<ApiResponse>> answer) => Add("POST", template, answer);
    public ApiRouter Delete(string template, Func<ApiRequest, IReadOnlyDictionary<string, string>, Task<ApiResponse>> answer) => Add("DELETE", template, answer);

    private ApiRouter Add(string method, string template, Func<ApiRequest, IReadOnlyDictionary<string, string>, Task<ApiResponse>> answer)
    {
        var segments = template.Split('/').Select(s => s.StartsWith('{')
            ? new Segment(s.Trim('{', '}', '?').Split(':')[0], IsValue: true, IsGuid: s.Contains(":guid"), Optional: s.EndsWith("?}", StringComparison.Ordinal))
            : new Segment(s, IsValue: false, IsGuid: false, Optional: false)).ToArray();
        _routes.Add((method, segments, answer));
        return this;
    }

    /// <summary>The answer of the first route that matches, or null when none does.</summary>
    public Task<ApiResponse>? Answer(ApiRequest request)
    {
        var path = request.Url.AbsolutePath;
        if (!path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var parts = path[Prefix.Length..].TrimEnd('/').Split('/');
        foreach (var (method, segments, answer) in _routes)
            if (string.Equals(method, request.Method, StringComparison.OrdinalIgnoreCase) && Match(segments, parts) is { } values)
                return answer(request, values);
        return null;
    }

    private static Dictionary<string, string>? Match(Segment[] segments, string[] parts)
    {
        // Only a last segment can be left out.
        if (parts.Length > segments.Length || parts.Length < segments.Length - (segments[^1].Optional ? 1 : 0)) return null;
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
