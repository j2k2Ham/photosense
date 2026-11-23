using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Locations;

/// <summary>
/// PhotoSense's folder in OneDrive, its app folder, Apps/PhotoSense, the only folder its access reaches. A personal
/// OneDrive keeps each file's SHA-256; a work one keeps none PhotoSense compares, and is read back. A name already taken
/// is refused by OneDrive itself, so nothing there is ever replaced.
/// </summary>
public sealed class OneDriveFolder : CloudFolder
{
    /// <summary>What goes in one request: no more than 4 MB in one, and pieces of 10 MB, a multiple of 320 KB as OneDrive asks, for larger.</summary>
    public const int SmallFile = 4 * 1024 * 1024, Piece = 10 * 1024 * 1024;

    private const string Root = "https://graph.microsoft.com/v1.0/me/drive/special/approot";

    /// <summary>The app folder of the OneDrive the tokens reach.</summary>
    /// <param name="http">Sends the requests.</param>
    /// <param name="accounts">Renews the tokens.</param>
    /// <param name="secrets">The tokens.</param>
    /// <param name="renewed">Keeps tokens renewed.</param>
    public OneDriveFolder(HttpClient http, CloudAccounts accounts, IReadOnlyDictionary<string, string> secrets, Func<IReadOnlyDictionary<string, string>, CancellationToken, Task> renewed)
        : base(http, accounts, LocationKind.OneDrive, secrets, renewed) { }

    /// <inheritdoc />
    public override string Where => "Apps/PhotoSense";

    private static string Item(string name) => $"{Root}:/{Uri.EscapeDataString(name)}";

    /// <inheritdoc />
    public override async Task<IReadOnlyList<RemoteFile>> ListAsync(CancellationToken ct)
    {
        var files = new List<RemoteFile>();
        string? next = $"{Root}/children?$select=name,size,file&$top=999";
        while (next is not null)
        {
            var url = next;
            using var answer = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), "list the folder", null, ct);
            var page = await JsonAsync(answer!, ct);
            foreach (var item in page["value"]!.AsArray().Where(i => i!["file"] is not null))
                files.Add(new RemoteFile(item!["name"]!.GetValue<string>(), item["size"]!.GetValue<long>()));
            next = page["@odata.nextLink"]?.GetValue<string>();
        }
        return files;
    }

    /// <inheritdoc />
    public override async Task<RemoteChecksum?> PutAsync(string name, Stream content, long length, CancellationToken ct)
    {
        if (length <= SmallFile)
        {
            var body = new StreamContent(content);
            body.Headers.ContentLength = length;
            using var put = await SendAsync(() => new HttpRequestMessage(HttpMethod.Put, $"{Item(name)}:/content?@microsoft.graph.conflictBehavior=fail") { Content = body },
                "write to the folder", name, ct);
            return Sha256(await JsonAsync(put!, ct));
        }
        string session;
        using (var started = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{Item(name)}:/createUploadSession")
               {
                   Content = Json(new JsonObject { ["item"] = new JsonObject { ["@microsoft.graph.conflictBehavior"] = "fail" } }),
               }, "write to the folder", name, ct))
            session = (await JsonAsync(started!, ct))["uploadUrl"]!.GetValue<string>();
        var buffer = new byte[Piece];
        for (long sent = 0; ;)
        {
            var n = await content.ReadAtLeastAsync(buffer, (int)Math.Min(Piece, length - sent), throwOnEndOfStream: true, ct);
            var from = sent;
            var piece = new ByteArrayContent(buffer, 0, n);
            piece.Headers.ContentRange = new ContentRangeHeaderValue(from, from + n - 1, length);
            // The upload's own address carries its permission: the token is not sent to it.
            using var answer = await SendAsync(() => new HttpRequestMessage(HttpMethod.Put, session) { Content = piece }, "write to the folder", name, ct, bearer: false);
            sent += n;
            if (sent >= length) return Sha256(await JsonAsync(answer!, ct));
        }
    }

    /// <inheritdoc />
    public override async Task<RemoteChecksum?> ChecksumAsync(string name, CancellationToken ct)
    {
        using var answer = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{Item(name)}?$select=file"), "read the folder", name, ct);
        return Sha256(await JsonAsync(answer!, ct));
    }

    /// <inheritdoc />
    public override async Task<Stream> OpenReadAsync(string name, CancellationToken ct)
    {
        var answer = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{Item(name)}:/content"), "read the folder", name, ct, read: true);
        return await answer!.Content.ReadAsStreamAsync(ct);
    }

    /// <inheritdoc />
    public override async Task DeleteAsync(string name, CancellationToken ct)
    {
        using var _ = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, Item(name)), "delete in the folder", name, ct,
            missing: answer => Task.FromResult(answer.StatusCode == System.Net.HttpStatusCode.NotFound));
    }

    /// <inheritdoc />
    public override async Task<RemoteSpace?> SpaceAsync(CancellationToken ct)
    {
        using var answer = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me/drive?$select=quota"), "read the folder", null, ct);
        var quota = (await JsonAsync(answer!, ct))["quota"];
        return quota?["total"] is JsonValue total ? new RemoteSpace(total.GetValue<long>(), quota["used"]?.GetValue<long>() ?? 0) : null;
    }

    private static RemoteChecksum? Sha256(JsonNode item)
        => item["file"]?["hashes"]?["sha256Hash"] is JsonValue said ? new RemoteChecksum(ChecksumKind.Sha256, said.GetValue<string>().ToLowerInvariant()) : null;
}
