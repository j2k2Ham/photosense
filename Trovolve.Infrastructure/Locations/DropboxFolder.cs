using System.Net;
using System.Text.Json.Nodes;
using Trovolve.Domain.Services;

namespace Trovolve.Infrastructure.Locations;

/// <summary>
/// Trovolve's folder in Dropbox, its app folder, Apps/Trovolve, the only folder its access reaches. Dropbox keeps a
/// checksum of its own of each file (the SHA-256 of the SHA-256s of each 4 MB). A name already taken is refused by
/// Dropbox itself, so nothing there is ever replaced.
/// </summary>
public sealed class DropboxFolder : CloudFolder
{
    /// <summary>The largest file sent in one request; larger ones go in pieces of this size.</summary>
    public const int Piece = 8 * 1024 * 1024;

    private const string Api = "https://api.dropboxapi.com/2";
    private const string Content = "https://content.dropboxapi.com/2";

    /// <summary>The app folder of the Dropbox the tokens reach.</summary>
    /// <param name="http">Sends the requests.</param>
    /// <param name="accounts">Renews the tokens.</param>
    /// <param name="secrets">The tokens.</param>
    /// <param name="renewed">Keeps tokens renewed.</param>
    public DropboxFolder(HttpClient http, CloudAccounts accounts, IReadOnlyDictionary<string, string> secrets, Func<IReadOnlyDictionary<string, string>, CancellationToken, Task> renewed)
        : base(http, accounts, LocationKind.Dropbox, secrets, renewed) { }

    /// <inheritdoc />
    public override string Where => "Apps/Trovolve";

    /// <inheritdoc />
    public override async Task<IReadOnlyList<RemoteFile>> ListAsync(CancellationToken ct)
    {
        var files = new List<RemoteFile>();
        var page = await RpcAsync("files/list_folder", new JsonObject { ["path"] = "" }, "list the folder", null, ct);
        while (true)
        {
            foreach (var entry in page!["entries"]!.AsArray().Where(e => e![".tag"]!.GetValue<string>() == "file"))
                files.Add(new RemoteFile(entry!["name"]!.GetValue<string>(), entry["size"]!.GetValue<long>()));
            if (page["has_more"]?.GetValue<bool>() != true) return files;
            page = await RpcAsync("files/list_folder/continue", new JsonObject { ["cursor"] = page["cursor"]!.GetValue<string>() }, "list the folder", null, ct);
        }
    }

    /// <inheritdoc />
    public override async Task<RemoteChecksum?> PutAsync(string name, Stream content, long length, CancellationToken ct)
    {
        var commit = new JsonObject { ["path"] = $"/{name}", ["mode"] = "add", ["autorename"] = false, ["strict_conflict"] = true };
        if (length <= Piece)
        {
            var body = new StreamContent(content);
            return Checksum(await UploadAsync("files/upload", commit, body, name, ct));
        }
        // Larger than one piece: the first piece starts a session, and the last ends it.
        var buffer = new byte[Piece];
        string? session = null;
        for (long sent = 0; ; sent += Piece)
        {
            var n = await content.ReadAtLeastAsync(buffer, (int)Math.Min(Piece, length - sent), throwOnEndOfStream: true, ct);
            var piece = new ByteArrayContent(buffer, 0, n);
            if (session is null)
                session = (await UploadAsync("files/upload_session/start", new JsonObject { ["close"] = false }, piece, name, ct))["session_id"]!.GetValue<string>();
            else if (sent + n < length)
                await UploadAsync("files/upload_session/append_v2", new JsonObject { ["cursor"] = Cursor(session, sent) }, piece, name, ct);
            else
                return Checksum(await UploadAsync("files/upload_session/finish", new JsonObject { ["cursor"] = Cursor(session, sent), ["commit"] = commit }, piece, name, ct));
        }
    }

    /// <inheritdoc />
    public override async Task<RemoteChecksum?> ChecksumAsync(string name, CancellationToken ct)
        => Checksum(await RpcAsync("files/get_metadata", new JsonObject { ["path"] = $"/{name}" }, "read the folder", name, ct)
            ?? throw new FileNotFoundException($"{name} is not in {Where}.", name));

    /// <inheritdoc />
    public override async Task<Stream> OpenReadAsync(string name, CancellationToken ct)
    {
        var answer = await SendAsync(() => Request($"{Content}/files/download", new JsonObject { ["path"] = $"/{name}" }, null), "read the folder", name, ct, NotFound, read: true)
            ?? throw new FileNotFoundException($"{name} is not in {Where}.", name);
        return await answer.Content.ReadAsStreamAsync(ct);
    }

    /// <inheritdoc />
    public override Task DeleteAsync(string name, CancellationToken ct) => RpcAsync("files/delete_v2", new JsonObject { ["path"] = $"/{name}" }, "delete in the folder", name, ct);

    /// <inheritdoc />
    public override async Task<RemoteSpace?> SpaceAsync(CancellationToken ct)
    {
        var usage = await RpcAsync("users/get_space_usage", null, "read the folder", null, ct);
        return usage!["allocation"]?["allocated"] is JsonValue allocated ? new RemoteSpace(allocated.GetValue<long>(), usage["used"]!.GetValue<long>()) : null;
    }

    private static JsonObject Cursor(string session, long offset) => new() { ["session_id"] = session, ["offset"] = offset };

    private static RemoteChecksum? Checksum(JsonNode file) => file["content_hash"] is JsonValue said ? new RemoteChecksum(ChecksumKind.DropboxContent, said.GetValue<string>()) : null;

    // Dropbox says a path is not there with 409 and "path/not_found" or "path_lookup/not_found".
    private static async Task<bool> NotFound(HttpResponseMessage answer)
        => answer.StatusCode == HttpStatusCode.Conflict && (await answer.Content.ReadAsStringAsync()).Contains("not_found", StringComparison.Ordinal);

    private async Task<JsonNode?> RpcAsync(string call, JsonObject? args, string doing, string? name, CancellationToken ct)
    {
        using var answer = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{Api}/{call}") { Content = args is null ? null : Json(args) }, doing, name, ct, NotFound);
        return answer is null ? null : await JsonAsync(answer, ct);
    }

    private async Task<JsonNode> UploadAsync(string call, JsonObject args, HttpContent body, string name, CancellationToken ct)
    {
        using var answer = await SendAsync(() => Request($"{Content}/{call}", args, body), "write to the folder", name, ct);
        return await JsonAsync(answer!, ct);
    }

    // A content request: its arguments in a header, as JSON with every character beyond ASCII escaped, as Dropbox asks.
    private static HttpRequestMessage Request(string url, JsonObject args, HttpContent? body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = body ?? new ByteArrayContent([]) };
        request.Headers.TryAddWithoutValidation("Dropbox-API-Arg", args.ToJsonString());
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        return request;
    }
}
