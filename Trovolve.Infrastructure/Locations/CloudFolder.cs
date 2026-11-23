using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Trovolve.Domain.Services;

namespace Trovolve.Infrastructure.Locations;

/// <summary>
/// What a cloud service's folder shares: its tokens, renewed before they run out and kept as they are; and its answers,
/// said in plain words when they are not what was asked for.
/// </summary>
public abstract class CloudFolder : IRemoteFolder
{
    private readonly CloudAccounts _accounts;
    private readonly LocationKind _kind;
    private readonly Func<IReadOnlyDictionary<string, string>, CancellationToken, Task> _renewed;
    private IReadOnlyDictionary<string, string> _secrets;

    /// <summary>A service's folder, reached with its tokens.</summary>
    /// <param name="http">Sends the requests.</param>
    /// <param name="accounts">Renews the tokens.</param>
    /// <param name="kind">Which service.</param>
    /// <param name="secrets">Its tokens.</param>
    /// <param name="renewed">Keeps tokens renewed.</param>
    protected CloudFolder(HttpClient http, CloudAccounts accounts, LocationKind kind, IReadOnlyDictionary<string, string> secrets,
        Func<IReadOnlyDictionary<string, string>, CancellationToken, Task> renewed)
    {
        Http = http;
        _accounts = accounts;
        _kind = kind;
        _secrets = secrets;
        _renewed = renewed;
    }

    /// <summary>Sends the requests.</summary>
    protected HttpClient Http { get; }

    /// <summary>The service's name: "Google Drive".</summary>
    protected string Service => LocationKinds.NameOf(_kind);

    /// <inheritdoc />
    public abstract string Where { get; }

    /// <inheritdoc />
    public abstract Task<IReadOnlyList<RemoteFile>> ListAsync(CancellationToken ct);

    /// <inheritdoc />
    public abstract Task<RemoteChecksum?> PutAsync(string name, Stream content, long length, CancellationToken ct);

    /// <inheritdoc />
    public abstract Task<RemoteChecksum?> ChecksumAsync(string name, CancellationToken ct);

    /// <inheritdoc />
    public abstract Task<Stream> OpenReadAsync(string name, CancellationToken ct);

    /// <inheritdoc />
    public abstract Task DeleteAsync(string name, CancellationToken ct);

    /// <inheritdoc />
    public abstract Task<RemoteSpace?> SpaceAsync(CancellationToken ct);

    /// <summary>
    /// Sends a request with the access token, renewed first if it is running out; a request the service turns away for
    /// its token is sent again once with one renewed, when it has no body or a JSON one: a file's bytes are not sent twice.
    /// </summary>
    /// <param name="request">Makes the request, afresh each time it is sent.</param>
    /// <param name="doing">What it is for, as the person would say it: "write to the folder".</param>
    /// <param name="name">The file it is about, if any.</param>
    /// <param name="ct">Stops it.</param>
    /// <param name="missing">An answer that means the file is not there, which is then not an error; null when none is.</param>
    /// <param name="read">The body is read as it arrives, by the caller.</param>
    /// <param name="bearer">Send the access token; not to an address the service gave that carries its own permission.</param>
    /// <returns>The answer, which the caller disposes; or null when it said the file is not there.</returns>
    protected async Task<HttpResponseMessage?> SendAsync(Func<HttpRequestMessage> request, string doing, string? name, CancellationToken ct,
        Func<HttpResponseMessage, Task<bool>>? missing = null, bool read = false, bool bearer = true)
    {
        for (var attempt = 0; ; attempt++)
        {
            if (_accounts.RunningOut(_secrets)) await RenewAsync(ct);
            using var asked = request();
            if (bearer) asked.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _secrets[CloudAccounts.Access]);
            HttpResponseMessage answer;
            try
            {
                answer = await Http.SendAsync(asked, read ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead, ct);
            }
            catch (HttpRequestException ex)
            {
                throw new IOException($"Trovolve could not reach {Service}: {ex.Message}", ex);
            }
            if (answer.IsSuccessStatusCode) return answer;
            if (missing is not null && await missing(answer))
            {
                answer.Dispose();
                return null;
            }
            using (answer)
            {
                if (answer.StatusCode == HttpStatusCode.Unauthorized && attempt == 0 && bearer && asked.Content is null or StringContent)
                {
                    await RenewAsync(ct);
                    continue;
                }
                throw answer.StatusCode switch
                {
                    HttpStatusCode.NotFound => new FileNotFoundException($"{name} is not in {Where}.", name),
                    HttpStatusCode.Unauthorized => new IOException($"{Service} signed Trovolve out. In Settings, choose Locations and turn it on again to sign in."),
                    HttpStatusCode.Forbidden => new IOException($"{Service} did not let Trovolve {doing}. {await ReasonAsync(answer, ct)}".TrimEnd()),
                    HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed => new IOException($"A file named {name} is already there, and Trovolve never replaces one."),
                    HttpStatusCode.InsufficientStorage or HttpStatusCode.RequestEntityTooLarge => new IOException($"{Service} is full."),
                    HttpStatusCode.TooManyRequests => new IOException($"{Service} is busy. Trovolve tries again at the next backup."),
                    _ => new IOException($"{Service} answered {(int)answer.StatusCode} {answer.ReasonPhrase}."),
                };
            }
        }
    }

    /// <summary>The body of an answer as JSON.</summary>
    protected static async Task<JsonNode> JsonAsync(HttpResponseMessage answer, CancellationToken ct)
        => JsonNode.Parse(await answer.Content.ReadAsStringAsync(ct)) ?? new JsonObject();

    /// <summary>A JSON body for a request.</summary>
    protected static StringContent Json(JsonNode body) => new(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");

    /// <summary>What a service says went wrong, when it says it in a way Trovolve can read.</summary>
    private static async Task<string> ReasonAsync(HttpResponseMessage answer, CancellationToken ct)
    {
        try
        {
            var body = await JsonAsync(answer, ct);
            return (body["error"]?["message"] ?? body["message"] ?? body["error_summary"]) is JsonValue said ? said.GetValue<string>() : "";
        }
        catch (System.Text.Json.JsonException)
        {
            return "";
        }
    }

    private async Task RenewAsync(CancellationToken ct)
    {
        _secrets = await _accounts.RenewAsync(_kind, _secrets, ct);
        await _renewed(_secrets, ct);
    }
}
