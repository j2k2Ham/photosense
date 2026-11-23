using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Trovolve.Domain.Services;

namespace Trovolve.Infrastructure.Locations;

/// <summary>
/// Trovolve's folder in Google Drive, My Drive/Trovolve, made by Trovolve itself, as its access reaches only the
/// files it made. Drive keeps each file's SHA-256. Drive allows two files of one name, so the caller's free name is all
/// that keeps one from looking like another.
/// </summary>
public sealed class DriveFolder : CloudFolder
{
    private const string Api = "https://www.googleapis.com/drive/v3";
    private const string Upload = "https://www.googleapis.com/upload/drive/v3";
    private const string FolderType = "application/vnd.google-apps.folder";
    private string? _folderId;

    /// <summary>The folder in the Drive the tokens reach.</summary>
    /// <param name="http">Sends the requests.</param>
    /// <param name="accounts">Renews the tokens.</param>
    /// <param name="secrets">The tokens.</param>
    /// <param name="renewed">Keeps tokens renewed.</param>
    public DriveFolder(HttpClient http, CloudAccounts accounts, IReadOnlyDictionary<string, string> secrets, Func<IReadOnlyDictionary<string, string>, CancellationToken, Task> renewed)
        : base(http, accounts, LocationKind.GoogleDrive, secrets, renewed) { }

    /// <inheritdoc />
    public override string Where => "My Drive/Trovolve";

    /// <inheritdoc />
    public override async Task<IReadOnlyList<RemoteFile>> ListAsync(CancellationToken ct)
    {
        var folder = await FolderIdAsync(ct);
        var files = new List<RemoteFile>();
        string? next = null;
        do
        {
            var page = await FindAsync($"'{folder}' in parents and trashed=false", "nextPageToken,files(name,size)", next, ct);
            foreach (var file in page["files"]!.AsArray())
                files.Add(new RemoteFile(file!["name"]!.GetValue<string>(), long.TryParse(file["size"]?.GetValue<string>(), out var size) ? size : 0));
            next = page["nextPageToken"]?.GetValue<string>();
        }
        while (next is not null);
        return files;
    }

    /// <inheritdoc />
    public override async Task<RemoteChecksum?> PutAsync(string name, Stream content, long length, CancellationToken ct)
    {
        var folder = await FolderIdAsync(ct);
        Uri session;
        using (var started = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{Upload}/files?uploadType=resumable")
               {
                   Content = Json(new JsonObject { ["name"] = name, ["parents"] = new JsonArray(folder) }),
               }, "write to the folder", name, ct))
            session = started!.Headers.Location!;
        var body = new StreamContent(content);
        body.Headers.ContentLength = length;
        using var done = await SendAsync(() => new HttpRequestMessage(HttpMethod.Put, new Uri($"{session}&fields=sha256Checksum")) { Content = body }, "write to the folder", name, ct);
        return Sha256((await JsonAsync(done!, ct))["sha256Checksum"]);
    }

    /// <inheritdoc />
    public override async Task<RemoteChecksum?> ChecksumAsync(string name, CancellationToken ct) => Sha256((await FileAsync(name, ct))["sha256Checksum"]);

    /// <inheritdoc />
    public override async Task<Stream> OpenReadAsync(string name, CancellationToken ct)
    {
        var id = (await FileAsync(name, ct))["id"]!.GetValue<string>();
        var answer = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{Api}/files/{id}?alt=media"), "read the folder", name, ct, read: true);
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
        using var answer = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{Api}/about?fields=storageQuota(limit,usage)"), "read the folder", null, ct);
        var quota = (await JsonAsync(answer!, ct))["storageQuota"];
        // A Drive without a limit says none.
        return long.TryParse(quota?["limit"]?.GetValue<string>(), out var limit) && long.TryParse(quota!["usage"]?.GetValue<string>(), out var used) ? new RemoteSpace(limit, used) : null;
    }

    private static RemoteChecksum? Sha256(JsonNode? value) => value is JsonValue said ? new RemoteChecksum(ChecksumKind.Sha256, said.GetValue<string>().ToLowerInvariant()) : null;

    private static string Quoted(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");

    private async Task<JsonNode> FindAsync(string query, string fields, string? next, CancellationToken ct)
    {
        var url = $"{Api}/files?q={Uri.EscapeDataString(query)}&fields={Uri.EscapeDataString(fields)}&pageSize=1000{(next is null ? "" : $"&pageToken={Uri.EscapeDataString(next)}")}";
        using var answer = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), "list the folder", null, ct);
        return await JsonAsync(answer!, ct);
    }

    private async Task<JsonNode> FileAsync(string name, CancellationToken ct)
    {
        var folder = await FolderIdAsync(ct);
        var found = await FindAsync($"name='{Quoted(name)}' and '{folder}' in parents and trashed=false", "files(id,sha256Checksum)", null, ct);
        return found["files"]!.AsArray().FirstOrDefault() ?? throw new FileNotFoundException($"{name} is not in {Where}.", name);
    }

    // Trovolve's folder in My Drive: found, or made the first time.
    private async Task<string> FolderIdAsync(CancellationToken ct)
    {
        if (_folderId is not null) return _folderId;
        var found = await FindAsync($"name='Trovolve' and mimeType='{FolderType}' and 'root' in parents and trashed=false", "files(id)", null, ct);
        if (found["files"]!.AsArray().FirstOrDefault() is { } folder) return _folderId = folder["id"]!.GetValue<string>();
        using var made = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{Api}/files?fields=id")
        {
            Content = Json(new JsonObject { ["name"] = "Trovolve", ["mimeType"] = FolderType, ["parents"] = new JsonArray("root") }),
        }, "make the folder", null, ct);
        return _folderId = (await JsonAsync(made!, ct))["id"]!.GetValue<string>();
    }
}
