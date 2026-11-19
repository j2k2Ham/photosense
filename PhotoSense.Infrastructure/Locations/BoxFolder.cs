using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Locations;

/// <summary>
/// PhotoSense's folder in Box, a folder named PhotoSense, made by PhotoSense itself. Box keeps each file's SHA-1, and
/// checks each piece of a large file by its own as it arrives. A name already taken is refused by Box itself, so
/// nothing there is ever replaced.
/// </summary>
public sealed class BoxFolder : CloudFolder
{
    /// <summary>The largest file Box takes in one request; larger ones go in an upload session, in pieces of the size Box says.</summary>
    public const long OneRequest = 50L * 1024 * 1024;

    private const string Api = "https://api.box.com/2.0";
    private const string Upload = "https://upload.box.com/api/2.0";
    private readonly Func<TimeSpan, CancellationToken, Task> _wait;
    private string? _folderId;

    /// <summary>The PhotoSense folder of the Box account the tokens reach.</summary>
    /// <param name="http">Sends the requests.</param>
    /// <param name="accounts">Renews the tokens.</param>
    /// <param name="secrets">The tokens.</param>
    /// <param name="renewed">Keeps tokens renewed.</param>
    /// <param name="wait">Waits as long as Box asks before it has put a large file together.</param>
    public BoxFolder(HttpClient http, CloudAccounts accounts, IReadOnlyDictionary<string, string> secrets, Func<IReadOnlyDictionary<string, string>, CancellationToken, Task> renewed,
        Func<TimeSpan, CancellationToken, Task> wait)
        : base(http, accounts, LocationKind.Box, secrets, renewed) => _wait = wait;

    /// <inheritdoc />
    public override string Where => "PhotoSense";

    /// <inheritdoc />
    public override async Task<IReadOnlyList<RemoteFile>> ListAsync(CancellationToken ct)
        => [.. (await ItemsAsync(await FolderIdAsync(ct), "name,size,type", ct)).Where(i => i["type"]!.GetValue<string>() == "file")
            .Select(i => new RemoteFile(i["name"]!.GetValue<string>(), i["size"]!.GetValue<long>()))];

    /// <inheritdoc />
    public override async Task<RemoteChecksum?> PutAsync(string name, Stream content, long length, CancellationToken ct)
    {
        var folder = await FolderIdAsync(ct);
        if (length <= OneRequest)
        {
            var form = new MultipartFormDataContent
            {
                // Box reads the attributes before the file.
                { Json(new JsonObject { ["name"] = name, ["parent"] = new JsonObject { ["id"] = folder } }), "attributes" },
                { new StreamContent(content), "file", name },
            };
            using var put = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{Upload}/files/content") { Content = form }, "write to the folder", name, ct);
            return Sha1((await JsonAsync(put!, ct))["entries"]![0]!);
        }
        string id;
        long size;
        using (var started = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{Upload}/files/upload_sessions")
               {
                   Content = Json(new JsonObject { ["folder_id"] = folder, ["file_size"] = length, ["file_name"] = name }),
               }, "write to the folder", name, ct))
        {
            var session = await JsonAsync(started!, ct);
            id = session["id"]!.GetValue<string>();
            size = session["part_size"]!.GetValue<long>();
        }
        using var whole = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var parts = new JsonArray();
        var buffer = new byte[size];
        for (long sent = 0; sent < length; sent += size)
        {
            var n = await content.ReadAtLeastAsync(buffer, (int)Math.Min(size, length - sent), throwOnEndOfStream: true, ct);
            whole.AppendData(buffer, 0, n);
            var piece = new ByteArrayContent(buffer, 0, n);
            piece.Headers.ContentRange = new ContentRangeHeaderValue(sent, sent + n - 1, length);
            var digest = Convert.ToBase64String(SHA1.HashData(buffer.AsSpan(0, n)));
            using var answer = await SendAsync(() => Digested(new HttpRequestMessage(HttpMethod.Put, $"{Upload}/files/upload_sessions/{id}") { Content = piece }, digest),
                "write to the folder", name, ct);
            parts.Add((await JsonAsync(answer!, ct))["part"]!.DeepClone());
        }
        var all = Convert.ToBase64String(whole.GetHashAndReset());
        // Box may ask to be asked again while it puts the pieces together.
        while (true)
        {
            using var committed = await SendAsync(() => Digested(new HttpRequestMessage(HttpMethod.Post, $"{Upload}/files/upload_sessions/{id}/commit")
            {
                Content = Json(new JsonObject { ["parts"] = parts.DeepClone() }),
            }, all), "write to the folder", name, ct);
            if (committed!.StatusCode != HttpStatusCode.Accepted) return Sha1((await JsonAsync(committed, ct))["entries"]![0]!);
            await _wait(committed.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1), ct);
        }
    }

    /// <inheritdoc />
    public override async Task<RemoteChecksum?> ChecksumAsync(string name, CancellationToken ct) => Sha1(await FileAsync(name, ct));

    /// <inheritdoc />
    public override async Task<Stream> OpenReadAsync(string name, CancellationToken ct)
    {
        var id = (await FileAsync(name, ct))["id"]!.GetValue<string>();
        var answer = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{Api}/files/{id}/content"), "read the folder", name, ct, read: true);
        return await answer!.Content.ReadAsStreamAsync(ct);
    }

    /// <inheritdoc />
    public override async Task DeleteAsync(string name, CancellationToken ct)
    {
        JsonNode file;
        try
        {
            file = await FileAsync(name, ct);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        using var _ = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, $"{Api}/files/{file["id"]!.GetValue<string>()}"), "delete in the folder", name, ct);
    }

    /// <inheritdoc />
    public override async Task<RemoteSpace?> SpaceAsync(CancellationToken ct)
    {
        using var answer = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{Api}/users/me?fields=space_amount,space_used"), "read the folder", null, ct);
        var me = await JsonAsync(answer!, ct);
        return new RemoteSpace(me["space_amount"]!.GetValue<long>(), me["space_used"]!.GetValue<long>());
    }

    private static HttpRequestMessage Digested(HttpRequestMessage request, string sha1)
    {
        request.Headers.TryAddWithoutValidation("Digest", $"sha={sha1}");
        return request;
    }

    private static RemoteChecksum? Sha1(JsonNode file) => file["sha1"] is JsonValue said ? new RemoteChecksum(ChecksumKind.Sha1, said.GetValue<string>().ToLowerInvariant()) : null;

    private async Task<JsonNode> FileAsync(string name, CancellationToken ct)
        => (await ItemsAsync(await FolderIdAsync(ct), "name,type,sha1", ct)).FirstOrDefault(i => i["type"]!.GetValue<string>() == "file" && i["name"]!.GetValue<string>() == name)
            ?? throw new FileNotFoundException($"{name} is not in {Where}.", name);

    // Every item in a folder, a page at a time.
    private async Task<List<JsonNode>> ItemsAsync(string folder, string fields, CancellationToken ct)
    {
        var items = new List<JsonNode>();
        for (var offset = 0; ;)
        {
            var url = $"{Api}/folders/{folder}/items?fields={fields}&limit=1000&offset={offset}";
            using var answer = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), "list the folder", null, ct);
            var page = await JsonAsync(answer!, ct);
            var entries = page["entries"]!.AsArray();
            items.AddRange(entries.Select(e => e!));
            offset += entries.Count;
            if (entries.Count == 0 || offset >= page["total_count"]!.GetValue<int>()) return items;
        }
    }

    // PhotoSense's folder at the top of the account: found, or made the first time.
    private async Task<string> FolderIdAsync(CancellationToken ct)
    {
        if (_folderId is not null) return _folderId;
        if ((await ItemsAsync("0", "name,type", ct)).FirstOrDefault(i => i["type"]!.GetValue<string>() == "folder" && i["name"]!.GetValue<string>() == "PhotoSense") is { } found)
            return _folderId = found["id"]!.GetValue<string>();
        using var made = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{Api}/folders")
        {
            Content = Json(new JsonObject { ["name"] = "PhotoSense", ["parent"] = new JsonObject { ["id"] = "0" } }),
        }, "make the folder", null, ct);
        return _folderId = (await JsonAsync(made!, ct))["id"]!.GetValue<string>();
    }
}
