using System.Net;
using System.Net.Http.Headers;
using System.Xml.Linq;
using Trovolve.Domain.Services;

namespace Trovolve.Infrastructure.Locations;

/// <summary>
/// A location's folder on a WebDAV server, as Nextcloud, ownCloud and most NAS boxes offer. WebDAV keeps no checksum,
/// so what arrives is read back; a name already taken is refused by the server itself (If-None-Match), so nothing there
/// is ever replaced. The folder is made the first time it is listed.
/// </summary>
public sealed class WebDavFolder : IRemoteFolder
{
    private static readonly XNamespace Dav = "DAV:";
    private static readonly HttpMethod Propfind = new("PROPFIND");
    private static readonly HttpMethod Mkcol = new("MKCOL");

    private readonly HttpClient _http;
    private readonly Uri _server;
    private readonly string _folder;
    private readonly AuthenticationHeaderValue _auth;

    /// <summary>The folder <paramref name="folder"/> on the server at <paramref name="server"/>, signed in to as <paramref name="user"/>.</summary>
    /// <param name="http">Sends the requests.</param>
    /// <param name="server">The server's address, as the person typed it: "https://cloud.example.com/dav".</param>
    /// <param name="folder">The folder on it: "/Photos/Trovolve".</param>
    /// <param name="user">The user name.</param>
    /// <param name="password">The password.</param>
    public WebDavFolder(HttpClient http, Uri server, string folder, string user, string password)
    {
        _http = http;
        _server = new Uri(server.AbsoluteUri.TrimEnd('/') + "/");
        _folder = folder.Trim('/');
        _auth = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{user}:{password}")));
    }

    /// <inheritdoc />
    public string Where => $"/{_folder}";

    private Uri Folder => new(_server, string.Join('/', _folder.Split('/').Select(Uri.EscapeDataString)) + "/");

    private Uri File(string name) => new(Folder, Uri.EscapeDataString(name));

    /// <inheritdoc />
    public async Task<IReadOnlyList<RemoteFile>> ListAsync(CancellationToken ct)
    {
        using var answer = await SendAsync(() => Request(Propfind, Folder, "1", Body("<d:prop><d:getcontentlength/><d:resourcetype/></d:prop>")), "list the folder", null, ct, allowMissing: true);
        if (answer.StatusCode == HttpStatusCode.NotFound)
        {
            await MakeFolderAsync(ct);
            return [];
        }
        var listed = XDocument.Parse(await answer.Content.ReadAsStringAsync(ct));
        return [.. listed.Descendants(Dav + "response")
            .Where(r => r.Descendants(Dav + "collection").FirstOrDefault() is null)
            .Select(r => new RemoteFile(Uri.UnescapeDataString(r.Element(Dav + "href")!.Value.TrimEnd('/').Split('/')[^1]),
                long.TryParse(r.Descendants(Dav + "getcontentlength").FirstOrDefault()?.Value, out var size) ? size : 0))];
    }

    /// <inheritdoc />
    public async Task<RemoteChecksum?> PutAsync(string name, Stream content, long length, CancellationToken ct)
    {
        var body = new StreamContent(content);
        body.Headers.ContentLength = length;
        using var _ = await SendAsync(() =>
        {
            var put = Request(HttpMethod.Put, File(name), null, body);
            // Refused when the name is taken: nothing there is ever replaced.
            put.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Any);
            return put;
        }, "write to the folder", name, ct);
        return null;
    }

    /// <inheritdoc />
    public async Task<RemoteChecksum?> ChecksumAsync(string name, CancellationToken ct)
    {
        using var _ = await SendAsync(() => Request(HttpMethod.Head, File(name), null, null), "read the folder", name, ct);
        return null;
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadAsync(string name, CancellationToken ct)
    {
        var answer = await SendAsync(() => Request(HttpMethod.Get, File(name), null, null), "read the folder", name, ct, read: true);
        return await answer.Content.ReadAsStreamAsync(ct);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string name, CancellationToken ct)
    {
        using var _ = await SendAsync(() => Request(HttpMethod.Delete, File(name), null, null), "delete in the folder", name, ct, allowMissing: true);
    }

    /// <inheritdoc />
    public async Task<RemoteSpace?> SpaceAsync(CancellationToken ct)
    {
        using var answer = await SendAsync(() => Request(Propfind, Folder, "0", Body("<d:prop><d:quota-available-bytes/><d:quota-used-bytes/></d:prop>")), "read the folder", null, ct);
        var said = XDocument.Parse(await answer.Content.ReadAsStringAsync(ct));
        return long.TryParse(said.Descendants(Dav + "quota-available-bytes").FirstOrDefault()?.Value, out var free)
            && long.TryParse(said.Descendants(Dav + "quota-used-bytes").FirstOrDefault()?.Value, out var used)
            ? new RemoteSpace(free + used, used) : null;
    }

    // Makes the folder, and each folder it is in that is not there yet.
    private async Task MakeFolderAsync(CancellationToken ct)
    {
        var path = "";
        foreach (var part in _folder.Split('/'))
        {
            path += Uri.EscapeDataString(part) + "/";
            var at = new Uri(_server, path);
            using var made = await SendAsync(() => Request(Mkcol, at, null, null), "make the folder", null, ct, allowTaken: true);
        }
    }

    private static StringContent Body(string prop)
        => new($"<?xml version=\"1.0\"?><d:propfind xmlns:d=\"DAV:\">{prop}</d:propfind>", System.Text.Encoding.UTF8, "application/xml");

    private HttpRequestMessage Request(HttpMethod method, Uri at, string? depth, HttpContent? content)
    {
        var request = new HttpRequestMessage(method, at) { Content = content };
        request.Headers.Authorization = _auth;
        if (depth is not null) request.Headers.Add("Depth", depth);
        return request;
    }

    // Sends a request, saying in plain words what went wrong and what to change.
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> request, string doing, string? name, CancellationToken ct,
        bool allowMissing = false, bool allowTaken = false, bool read = false)
    {
        HttpResponseMessage answer;
        try
        {
            using var asked = request();
            answer = await _http.SendAsync(asked, read ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new IOException($"Trovolve could not reach {_server.Host}: {ex.Message}", ex);
        }
        if (answer.IsSuccessStatusCode || (allowMissing && answer.StatusCode == HttpStatusCode.NotFound) || (allowTaken && answer.StatusCode == HttpStatusCode.MethodNotAllowed))
            return answer;
        using (answer)
        {
            throw answer.StatusCode switch
            {
                HttpStatusCode.NotFound => new FileNotFoundException($"{name} is not in {Where}.", name),
                HttpStatusCode.Unauthorized => new IOException("The user name or password was not accepted. Check them and try again."),
                HttpStatusCode.Forbidden => new IOException($"This account is not allowed to {doing}, {Where}. Check its permissions and try again."),
                HttpStatusCode.PreconditionFailed => new IOException($"A file named {name} is already there, and Trovolve never replaces one."),
                HttpStatusCode.InsufficientStorage => new IOException($"{_server.Host} is full."),
                _ => new IOException($"{_server.Host} answered {(int)answer.StatusCode} {answer.ReasonPhrase}."),
            };
        }
    }
}
