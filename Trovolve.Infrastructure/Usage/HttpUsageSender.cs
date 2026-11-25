using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trovolve.Domain.Entities;
using Trovolve.Domain.Services;

namespace Trovolve.Infrastructure.Usage;

/// <summary>
/// Sends uses to the usage server as JSON, in one POST a batch:
/// <c>{ "install": "…", "source": { "program": "server", "version": "1.2.0.57", "system": "…" }, "uses": [ { "sequence": 1, "at": "2026-11-25T09:41:00+00:00", "feature": "POST scan/start", "status": 200, "session": "…" } ] }</c>.
/// The JSON is sent whole, with its length, as a server or proxy may not take a body sent in pieces. Any answer from
/// 200 to 299 means the server has taken them.
/// </summary>
/// <param name="http">One client for every request, given by the program; a request is stopped by its time limit or its token.</param>
public sealed class HttpUsageSender(HttpClient http) : IUsageSender
{
    /// <inheritdoc />
    public async Task SendAsync(Uri to, UsageReport report, CancellationToken ct)
    {
        HttpResponseMessage answer;
        try
        {
            using var body = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(report, UsageJson.Default.UsageReport));
            body.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            answer = await http.PostAsync(to, body, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new IOException($"The usage server could not be reached: {ex.Message}", ex);
        }
        // The client's own time limit, not the caller giving up, which goes on as it is.
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new IOException("The usage server did not answer in time.", ex);
        }
        using (answer)
            if (!answer.IsSuccessStatusCode) throw new IOException($"The usage server answered {(int)answer.StatusCode} and did not take the uses.");
    }
}

/// <summary>
/// The usage report's JSON, written by code made when Trovolve is built rather than found by reflection as it runs, so that
/// a phone's trimmed app keeps every type it needs. Names are camelCased, as the web writes them.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(UsageReport))]
internal sealed partial class UsageJson : JsonSerializerContext;
