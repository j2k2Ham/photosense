using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Locations;

namespace PhotoSense.Tests.Infrastructure;

/// <summary>
/// PhotoSense's folder in Google Drive, OneDrive, Dropbox and Box, each against the service's own answers: made the first
/// time, listed a page at a time, a file put in one request or in pieces and never over a name, the service's checksum
/// said, read back, deleted, and how full it is; the tokens renewed before they run out; and every refusal in plain words.
/// </summary>
public class CloudFoldersTests
{
    private const string Drive = "https://www.googleapis.com/drive/v3";
    private const string Graph = "https://graph.microsoft.com/v1.0/me/drive/special/approot";
    private const string DropboxApi = "https://api.dropboxapi.com/2";
    private const string DropboxContent = "https://content.dropboxapi.com/2";
    private const string BoxApi = "https://api.box.com/2.0";
    private const string BoxUpload = "https://upload.box.com/api/2.0";

    private static readonly TestClock Clock = new();

    private static Dictionary<string, string> Fresh => new()
    {
        [CloudAccounts.Access] = "a", [CloudAccounts.Refresh] = "r",
        [CloudAccounts.Expires] = Clock.GetUtcNow().AddHours(1).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
    };

    private static readonly CloudAppOptions Apps = new() { GoogleClientId = "g.apps.googleusercontent.com", MicrosoftClientId = "m", DropboxClientId = "d", BoxClientId = "b", BoxClientSecret = "s" };

    private sealed record Made<T>(T Folder, FakeNetwork Network, List<IReadOnlyDictionary<string, string>> Renewed);

    private static Made<T> Make<T>(Func<HttpClient, CloudAccounts, Dictionary<string, string>, Func<IReadOnlyDictionary<string, string>, CancellationToken, Task>, T> make,
        Dictionary<string, string>? secrets = null)
    {
        var network = new FakeNetwork();
        var http = new HttpClient(network);
        var renewed = new List<IReadOnlyDictionary<string, string>>();
        var folder = make(http, new CloudAccounts(Apps, http, Clock), secrets ?? Fresh, (s, _) => { renewed.Add(s); return Task.CompletedTask; });
        return new Made<T>(folder, network, renewed);
    }

    private static string Query(Heard heard) => Uri.UnescapeDataString(heard.Url);

    private static async Task<string> MessageAsync(Func<Task> act) => (await Assert.ThrowsAsync<IOException>(act))!.Message;

    private static async Task<byte[]> AllAsync(Stream stream, CancellationToken ct)
    {
        var bytes = new MemoryStream();
        await stream.CopyToAsync(bytes, ct);
        return bytes.ToArray();
    }

    // ——— Google Drive ———

    [Test]
    public async Task Drive_Makes_Its_Folder_Once_And_Lists_It_A_Page_At_A_Time(CancellationToken ct)
    {
        var (folder, network, _) = Make((h, a, s, r) => new DriveFolder(h, a, s, r));
        network.On(HttpMethod.Get, $"{Drive}/files?q=", h => Query(h).Contains("mimeType=") ? FakeNetwork.Json("""{"files":[]}""")
                : Query(h).Contains("pageToken=p2") ? FakeNetwork.Json("""{"files":[{"name":"IMG_2.JPG"}]}""")
                : FakeNetwork.Json("""{"nextPageToken":"p2","files":[{"name":"IMG_1.JPG","size":"5"}]}"""))
            .On(HttpMethod.Post, $"{Drive}/files?fields=id", _ => FakeNetwork.Json("""{"id":"F"}"""));
        await Assert.That(folder.Where).IsEqualTo("My Drive/PhotoSense");
        await Assert.That(await folder.ListAsync(ct)).IsEquivalentTo([new RemoteFile("IMG_1.JPG", 5), new RemoteFile("IMG_2.JPG", 0)], CollectionOrdering.Matching);
        var made = network.Asked.Single(h => h.Method == HttpMethod.Post);
        await Assert.That(made.Body).Contains("\"mimeType\":\"application/vnd.google-apps.folder\"").And.Contains("\"parents\":[\"root\"]");
        await Assert.That(Query(network.Asked.Last())).Contains("'F' in parents");
        await folder.ListAsync(ct);
        await Assert.That(network.Asked.Count(h => h.Method == HttpMethod.Post)).IsEqualTo(1);
    }

    private static Made<DriveFolder> DriveWithFiles()
    {
        var made = Make((h, a, s, r) => new DriveFolder(h, a, s, r));
        made.Network.On(HttpMethod.Get, $"{Drive}/files?q=", h =>
                Query(h).Contains("mimeType=") ? FakeNetwork.Json("""{"files":[{"id":"F"}]}""")
                : Query(h).Contains("name='IMG_1.JPG'") ? FakeNetwork.Json("""{"files":[{"id":"X","sha256Checksum":"ABCD"}]}""")
                : Query(h).Contains("name='plain.JPG'") ? FakeNetwork.Json("""{"files":[{"id":"P"}]}""")
                : FakeNetwork.Json("""{"files":[]}"""))
            .On(HttpMethod.Post, "https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable", _ =>
            {
                var started = FakeNetwork.Status(HttpStatusCode.OK);
                started.Headers.Location = new Uri("https://upload.example/session?upload_id=1");
                return started;
            })
            .On(HttpMethod.Put, "https://upload.example/session?upload_id=1&fields=sha256Checksum", _ => FakeNetwork.Json("""{"sha256Checksum":"ABCD"}"""))
            .On(HttpMethod.Get, $"{Drive}/files/X?alt=media", _ => FakeNetwork.Bytes([7]))
            .On(HttpMethod.Delete, $"{Drive}/files/X", _ => FakeNetwork.Status(HttpStatusCode.NoContent))
            .On(HttpMethod.Get, $"{Drive}/about", _ => FakeNetwork.Json("""{"storageQuota":{"limit":"100","usage":"40"}}"""));
        return made;
    }

    [Test]
    public async Task Drive_Puts_A_File_In_Its_Folder_Says_Its_SHA256_Reads_It_Back_Deletes_It_And_Says_How_Full_It_Is(CancellationToken ct)
    {
        var (folder, network, _) = DriveWithFiles();
        await Assert.That(await folder.PutAsync("IMG_1.JPG", new MemoryStream([1, 2]), 2, ct)).IsEqualTo(new RemoteChecksum(ChecksumKind.Sha256, "abcd"));
        var started = network.Asked.Single(h => h.Url.Contains("uploadType=resumable"));
        await Assert.That(started.Body).Contains("\"name\":\"IMG_1.JPG\"").And.Contains("\"parents\":[\"F\"]");
        await Assert.That(network.Asked.Single(h => h.Method == HttpMethod.Put).Bytes).IsEquivalentTo(new byte[] { 1, 2 });
        await Assert.That(await folder.ChecksumAsync("IMG_1.JPG", ct)).IsEqualTo(new RemoteChecksum(ChecksumKind.Sha256, "abcd"));
        await Assert.That(await folder.ChecksumAsync("plain.JPG", ct)).IsNull();
        await Assert.ThrowsAsync<FileNotFoundException>(() => folder.ChecksumAsync("gone.JPG", ct));
        await Assert.That(await AllAsync(await folder.OpenReadAsync("IMG_1.JPG", ct), ct)).IsEquivalentTo(new byte[] { 7 });
        await folder.DeleteAsync("IMG_1.JPG", ct);
        await folder.DeleteAsync("gone.JPG", ct);
        await Assert.That(network.Asked.Count(h => h.Method == HttpMethod.Delete)).IsEqualTo(1);
        await Assert.That(await folder.SpaceAsync(ct)).IsEqualTo(new RemoteSpace(100, 40));
        network.On(HttpMethod.Get, $"{Drive}/about", _ => FakeNetwork.Json("""{"storageQuota":{"usage":"1"}}"""));
        await Assert.That(await folder.SpaceAsync(ct)).IsNull();
        network.On(HttpMethod.Get, $"{Drive}/about", _ => FakeNetwork.Json("null"));
        await Assert.That(await folder.SpaceAsync(ct)).IsNull();
        network.On(HttpMethod.Get, $"{Drive}/about", _ => FakeNetwork.Json("""{"storageQuota":{"limit":"5"}}"""));
        await Assert.That(await folder.SpaceAsync(ct)).IsNull();
        // A name with a quote in it is asked for whole.
        await Assert.ThrowsAsync<FileNotFoundException>(() => folder.ChecksumAsync("O'Brien\\1.JPG", ct));
        await Assert.That(Query(network.Asked.Last())).Contains("name='O\\'Brien\\\\1.JPG'");
    }

    [Test]
    public async Task A_Token_Running_Out_Is_Renewed_First_And_One_Turned_Away_Is_Renewed_Once_Then_The_Person_Is_Asked_To_Sign_In(CancellationToken ct)
    {
        var expired = Fresh;
        expired[CloudAccounts.Expires] = Clock.GetUtcNow().AddMinutes(-1).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var (folder, network, renewed) = Make((h, a, s, r) => new DriveFolder(h, a, s, r), expired);
        network.On("https://oauth2.googleapis.com/token", """{"access_token":"b","expires_in":3600}""")
            .On(HttpMethod.Get, $"{Drive}/about", h => h.Headers["Authorization"] == "Bearer b" ? FakeNetwork.Json("""{"storageQuota":{"limit":"2","usage":"1"}}""") : FakeNetwork.Status(HttpStatusCode.Unauthorized));
        await Assert.That(await folder.SpaceAsync(ct)).IsEqualTo(new RemoteSpace(2, 1));
        await Assert.That(renewed.Single()[CloudAccounts.Access]).IsEqualTo("b");

        // Turned away with a fresh token: renewed once, and asked again.
        var (again, retried, renewedAgain) = Make((h, a, s, r) => new DriveFolder(h, a, s, r));
        retried.On("https://oauth2.googleapis.com/token", """{"access_token":"b","expires_in":3600}""")
            .On(HttpMethod.Get, $"{Drive}/about", h => h.Headers["Authorization"] == "Bearer b" ? FakeNetwork.Json("""{"storageQuota":{"limit":"2","usage":"1"}}""") : FakeNetwork.Status(HttpStatusCode.Unauthorized));
        await Assert.That(await again.SpaceAsync(ct)).IsEqualTo(new RemoteSpace(2, 1));
        await Assert.That(renewedAgain).Count().IsEqualTo(1);
        // Turned away again: the person signs in again.
        retried.On(HttpMethod.Get, $"{Drive}/about", _ => FakeNetwork.Status(HttpStatusCode.Unauthorized));
        await Assert.That(await MessageAsync(() => again.SpaceAsync(ct))).IsEqualTo("Google Drive signed PhotoSense out. In Settings, choose Locations and turn it on again to sign in.");
        // A file's bytes are never sent twice.
        var (uploads, upNetwork, upRenewed) = DriveWithFiles();
        upNetwork.On(HttpMethod.Put, "https://upload.example/", _ => FakeNetwork.Status(HttpStatusCode.Unauthorized));
        await Assert.That(await MessageAsync(() => uploads.PutAsync("IMG_1.JPG", new MemoryStream([1]), 1, ct))).StartsWith("Google Drive signed PhotoSense out.");
        await Assert.That(upNetwork.Asked.Count(h => h.Method == HttpMethod.Put)).IsEqualTo(1);
        await Assert.That(upRenewed).IsEmpty();
    }

    [Test]
    public async Task Every_Refusal_Of_A_Cloud_Service_Is_Said_In_Plain_Words(CancellationToken ct)
    {
        var (folder, network, _) = DriveWithFiles();
        Task Space() => folder.SpaceAsync(ct);
        async Task<string> SaysAsync(HttpStatusCode status, string body)
        {
            network.On(HttpMethod.Get, $"{Drive}/about", _ => FakeNetwork.Json(body, status));
            return await MessageAsync(Space);
        }
        await Assert.That(await SaysAsync(HttpStatusCode.Forbidden, """{"error":{"message":"The quota is used up."}}""")).IsEqualTo("Google Drive did not let PhotoSense read the folder. The quota is used up.");
        await Assert.That(await SaysAsync(HttpStatusCode.Forbidden, """{"message":"No."}""")).IsEqualTo("Google Drive did not let PhotoSense read the folder. No.");
        await Assert.That(await SaysAsync(HttpStatusCode.Forbidden, """{"error_summary":"Not allowed."}""")).IsEqualTo("Google Drive did not let PhotoSense read the folder. Not allowed.");
        await Assert.That(await SaysAsync(HttpStatusCode.Forbidden, "{}")).IsEqualTo("Google Drive did not let PhotoSense read the folder.");
        await Assert.That(await SaysAsync(HttpStatusCode.Forbidden, "Forbidden")).IsEqualTo("Google Drive did not let PhotoSense read the folder.");
        await Assert.That(await SaysAsync(HttpStatusCode.Conflict, "{}")).IsEqualTo("A file named  is already there, and PhotoSense never replaces one.");
        await Assert.That(await SaysAsync(HttpStatusCode.PreconditionFailed, "{}")).StartsWith("A file named");
        await Assert.That(await SaysAsync(HttpStatusCode.InsufficientStorage, "{}")).IsEqualTo("Google Drive is full.");
        await Assert.That(await SaysAsync(HttpStatusCode.RequestEntityTooLarge, "{}")).IsEqualTo("Google Drive is full.");
        await Assert.That(await SaysAsync(HttpStatusCode.TooManyRequests, "{}")).IsEqualTo("Google Drive is busy. PhotoSense tries again at the next backup.");
        await Assert.That(await SaysAsync(HttpStatusCode.InternalServerError, "{}")).IsEqualTo("Google Drive answered 500 InternalServerError.");
        network.On(HttpMethod.Get, $"{Drive}/about", _ => FakeNetwork.Status(HttpStatusCode.NotFound));
        await Assert.ThrowsAsync<FileNotFoundException>(Space);
        network.Down = true;
        await Assert.That(await MessageAsync(Space)).IsEqualTo("PhotoSense could not reach Google Drive: No route to host.");
    }

    // ——— OneDrive ———

    [Test]
    public async Task OneDrive_Lists_Its_App_Folder_A_Page_At_A_Time_Files_Only(CancellationToken ct)
    {
        var (folder, network, _) = Make((h, a, s, r) => new OneDriveFolder(h, a, s, r));
        network.On(HttpMethod.Get, $"{Graph}/children", _ => FakeNetwork.Json("""{"value":[{"name":"Old","folder":{}},{"name":"IMG_1.JPG","size":5,"file":{}}],"@odata.nextLink":"https://graph.example/next"}"""))
            .On(HttpMethod.Get, "https://graph.example/next", _ => FakeNetwork.Json("""{"value":[{"name":"IMG_2.JPG","size":1,"file":{}}]}"""));
        await Assert.That(folder.Where).IsEqualTo("Apps/PhotoSense");
        await Assert.That(await folder.ListAsync(ct)).IsEquivalentTo([new RemoteFile("IMG_1.JPG", 5), new RemoteFile("IMG_2.JPG", 1)], CollectionOrdering.Matching);
    }

    [Test]
    public async Task OneDrive_Puts_A_Small_File_In_One_Request_And_A_Large_One_In_Pieces_Never_Over_A_Name(CancellationToken ct)
    {
        var (folder, network, _) = Make((h, a, s, r) => new OneDriveFolder(h, a, s, r));
        network.On(HttpMethod.Put, $"{Graph}:/IMG%201.JPG:/content", _ => FakeNetwork.Json("""{"file":{"hashes":{"sha256Hash":"AB"}}}"""))
            .On(HttpMethod.Post, $"{Graph}:/clip.MOV:/createUploadSession", _ => FakeNetwork.Json("""{"uploadUrl":"https://up.example/s"}"""))
            .On(HttpMethod.Put, "https://up.example/s", h => h.Headers["Content-Range"].EndsWith($"/{OneDriveFolder.Piece + 5}", StringComparison.Ordinal) && h.Headers["Content-Range"].StartsWith($"bytes {OneDriveFolder.Piece}", StringComparison.Ordinal)
                ? FakeNetwork.Json("""{"file":{"hashes":{"quickXorHash":"q"}}}""") : FakeNetwork.Json("""{"nextExpectedRanges":["x"]}""", HttpStatusCode.Accepted));
        await Assert.That(await folder.PutAsync("IMG 1.JPG", new MemoryStream([1]), 1, ct)).IsEqualTo(new RemoteChecksum(ChecksumKind.Sha256, "ab"));
        await Assert.That(network.Asked[0].Url).EndsWith("?@microsoft.graph.conflictBehavior=fail");

        var bytes = RandomNumberGenerator.GetBytes(OneDriveFolder.Piece + 5);
        await Assert.That(await folder.PutAsync("clip.MOV", new MemoryStream(bytes), bytes.Length, ct)).IsNull();
        await Assert.That(network.Asked.Single(h => h.Url.EndsWith("createUploadSession", StringComparison.Ordinal)).Body).Contains("\"@microsoft.graph.conflictBehavior\":\"fail\"");
        var pieces = network.Asked.Where(h => h.Url == "https://up.example/s").ToList();
        await Assert.That(pieces.Select(p => p.Headers["Content-Range"])).IsEquivalentTo([$"bytes 0-{OneDriveFolder.Piece - 1}/{bytes.Length}", $"bytes {OneDriveFolder.Piece}-{bytes.Length - 1}/{bytes.Length}"], CollectionOrdering.Matching);
        // The upload's own address carries its permission; the token is not sent to it.
        await Assert.That(pieces.All(p => !p.Headers.ContainsKey("Authorization"))).IsTrue();
        await Assert.That(pieces.SelectMany(p => p.Bytes).SequenceEqual(bytes)).IsTrue();
    }

    [Test]
    public async Task OneDrive_Says_A_Files_SHA256_Reads_It_Back_Deletes_It_And_Says_How_Full_It_Is(CancellationToken ct)
    {
        var (folder, network, _) = Make((h, a, s, r) => new OneDriveFolder(h, a, s, r));
        network.On(HttpMethod.Get, $"{Graph}:/IMG_1.JPG?", _ => FakeNetwork.Json("""{"file":{"hashes":{"sha256Hash":"CD"}}}"""))
            .On(HttpMethod.Get, $"{Graph}:/work.JPG?", _ => FakeNetwork.Json("""{"file":{"hashes":{"quickXorHash":"q"}}}"""))
            .On(HttpMethod.Get, $"{Graph}:/bare.JPG?", _ => FakeNetwork.Json("""{"file":{}}"""))
            .On(HttpMethod.Get, $"{Graph}:/odd.JPG?", _ => FakeNetwork.Json("{}"))
            .On(HttpMethod.Get, $"{Graph}:/IMG_1.JPG:/content", _ => FakeNetwork.Bytes([7]))
            .On(HttpMethod.Delete, $"{Graph}:/IMG_1.JPG", _ => FakeNetwork.Status(HttpStatusCode.NoContent))
            .On(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me/drive?", _ => FakeNetwork.Json("""{"quota":{"total":10,"used":4}}"""));
        await Assert.That(await folder.ChecksumAsync("IMG_1.JPG", ct)).IsEqualTo(new RemoteChecksum(ChecksumKind.Sha256, "cd"));
        await Assert.That(await folder.ChecksumAsync("work.JPG", ct)).IsNull();
        await Assert.That(await folder.ChecksumAsync("bare.JPG", ct)).IsNull();
        await Assert.That(await folder.ChecksumAsync("odd.JPG", ct)).IsNull();
        await Assert.ThrowsAsync<FileNotFoundException>(() => folder.ChecksumAsync("gone.JPG", ct));
        await Assert.That(await AllAsync(await folder.OpenReadAsync("IMG_1.JPG", ct), ct)).IsEquivalentTo(new byte[] { 7 });
        await folder.DeleteAsync("IMG_1.JPG", ct);
        await folder.DeleteAsync("gone.JPG", ct);
        await Assert.That(await folder.SpaceAsync(ct)).IsEqualTo(new RemoteSpace(10, 4));
        network.On(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me/drive?", _ => FakeNetwork.Json("""{"quota":{"total":10}}"""));
        await Assert.That(await folder.SpaceAsync(ct)).IsEqualTo(new RemoteSpace(10, 0));
        network.On(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me/drive?", _ => FakeNetwork.Json("{}"));
        await Assert.That(await folder.SpaceAsync(ct)).IsNull();
    }

    // ——— Dropbox ———

    [Test]
    public async Task Dropbox_Lists_Its_App_Folder_A_Page_At_A_Time_Files_Only(CancellationToken ct)
    {
        var (folder, network, _) = Make((h, a, s, r) => new DropboxFolder(h, a, s, r));
        network.On(HttpMethod.Post, $"{DropboxApi}/files/list_folder", h => h.Url.EndsWith("continue", StringComparison.Ordinal)
            ? FakeNetwork.Json(h.Body.Contains("c2") ? """{"entries":[]}""" : """{"entries":[{".tag":"file","name":"IMG_2.JPG","size":1}],"has_more":true,"cursor":"c2"}""")
            : FakeNetwork.Json("""{"entries":[{".tag":"folder","name":"Old"},{".tag":"file","name":"IMG_1.JPG","size":5}],"has_more":true,"cursor":"c1"}"""));
        await Assert.That(folder.Where).IsEqualTo("Apps/PhotoSense");
        await Assert.That(await folder.ListAsync(ct)).IsEquivalentTo([new RemoteFile("IMG_1.JPG", 5), new RemoteFile("IMG_2.JPG", 1)], CollectionOrdering.Matching);
        await Assert.That(network.Asked[1].Body).IsEqualTo("""{"cursor":"c1"}""");
        network.On(HttpMethod.Post, $"{DropboxApi}/files/list_folder", _ => FakeNetwork.Json("""{"entries":[],"has_more":false}"""));
        await Assert.That(await folder.ListAsync(ct)).IsEmpty();
    }

    [Test]
    public async Task Dropbox_Puts_A_Small_File_In_One_Request_And_A_Large_One_In_A_Session_Never_Over_A_Name(CancellationToken ct)
    {
        var (folder, network, _) = Make((h, a, s, r) => new DropboxFolder(h, a, s, r));
        network.On(HttpMethod.Post, $"{DropboxContent}/files/upload", _ => FakeNetwork.Json("""{"content_hash":"ab"}"""))
            .On(HttpMethod.Post, $"{DropboxContent}/files/upload_session/start", _ => FakeNetwork.Json("""{"session_id":"s1"}"""))
            .On(HttpMethod.Post, $"{DropboxContent}/files/upload_session/append_v2", _ => FakeNetwork.Json("null"))
            .On(HttpMethod.Post, $"{DropboxContent}/files/upload_session/finish", _ => FakeNetwork.Json("""{"name":"clip.MOV"}"""));
        await Assert.That(await folder.PutAsync("Café.JPG", new MemoryStream([1]), 1, ct)).IsEqualTo(new RemoteChecksum(ChecksumKind.DropboxContent, "ab"));
        var arg = network.Asked[0].Headers["Dropbox-API-Arg"];
        await Assert.That(arg).Contains("\\u00E9").And.Contains("\"mode\":\"add\"").And.Contains("\"autorename\":false").And.Contains("\"strict_conflict\":true");

        var bytes = RandomNumberGenerator.GetBytes(DropboxFolder.Piece * 2 + 5);
        await Assert.That(await folder.PutAsync("clip.MOV", new MemoryStream(bytes), bytes.Length, ct)).IsNull();
        var calls = network.Asked.Skip(1).ToList();
        await Assert.That(calls.Select(c => c.Url.Split('/')[^1])).IsEquivalentTo(["start", "append_v2", "finish"], CollectionOrdering.Matching);
        var offsets = calls.Skip(1).Select(c => JsonDocument.Parse(c.Headers["Dropbox-API-Arg"]).RootElement.GetProperty("cursor").GetProperty("offset").GetInt64());
        await Assert.That(offsets).IsEquivalentTo([(long)DropboxFolder.Piece, DropboxFolder.Piece * 2L], CollectionOrdering.Matching);
        await Assert.That(calls.SelectMany(c => c.Bytes).SequenceEqual(bytes)).IsTrue();
        await Assert.That(calls[^1].Headers["Dropbox-API-Arg"]).Contains("\"commit\":{\"path\":\"/clip.MOV\"");
    }

    [Test]
    public async Task Dropbox_Says_A_Files_Checksum_Reads_It_Back_Deletes_It_And_Says_How_Full_It_Is(CancellationToken ct)
    {
        var (folder, network, _) = Make((h, a, s, r) => new DropboxFolder(h, a, s, r));
        var notFound = """{"error_summary":"path/not_found/"}""";
        network.On(HttpMethod.Post, $"{DropboxApi}/files/get_metadata", h => h.Body.Contains("gone") ? FakeNetwork.Json(notFound, HttpStatusCode.Conflict)
                : h.Body.Contains("plain") ? FakeNetwork.Json("{}") : FakeNetwork.Json("""{"content_hash":"cd"}"""))
            .On(HttpMethod.Post, $"{DropboxContent}/files/download", h => h.Headers["Dropbox-API-Arg"].Contains("gone") ? FakeNetwork.Json(notFound, HttpStatusCode.Conflict) : FakeNetwork.Bytes([7]))
            .On(HttpMethod.Post, $"{DropboxApi}/files/delete_v2", h => h.Body.Contains("gone") ? FakeNetwork.Json("""{"error_summary":"path_lookup/not_found/"}""", HttpStatusCode.Conflict) : FakeNetwork.Json("{}"))
            .On(HttpMethod.Post, $"{DropboxApi}/users/get_space_usage", _ => FakeNetwork.Json("""{"used":4,"allocation":{"allocated":10}}"""));
        await Assert.That(await folder.ChecksumAsync("IMG_1.JPG", ct)).IsEqualTo(new RemoteChecksum(ChecksumKind.DropboxContent, "cd"));
        await Assert.That(await folder.ChecksumAsync("plain.JPG", ct)).IsNull();
        await Assert.ThrowsAsync<FileNotFoundException>(() => folder.ChecksumAsync("gone.JPG", ct));
        await Assert.That(await AllAsync(await folder.OpenReadAsync("IMG_1.JPG", ct), ct)).IsEquivalentTo(new byte[] { 7 });
        await Assert.ThrowsAsync<FileNotFoundException>(() => folder.OpenReadAsync("gone.JPG", ct));
        await folder.DeleteAsync("IMG_1.JPG", ct);
        await folder.DeleteAsync("gone.JPG", ct);
        await Assert.That(await folder.SpaceAsync(ct)).IsEqualTo(new RemoteSpace(10, 4));
        await Assert.That(network.Asked.Last().Body).IsEqualTo("");
        network.On(HttpMethod.Post, $"{DropboxApi}/users/get_space_usage", _ => FakeNetwork.Json("""{"used":4,"allocation":{".tag":"team"}}"""));
        await Assert.That(await folder.SpaceAsync(ct)).IsNull();
        network.On(HttpMethod.Post, $"{DropboxApi}/users/get_space_usage", _ => FakeNetwork.Json("""{"used":4}"""));
        await Assert.That(await folder.SpaceAsync(ct)).IsNull();
        // A conflict that is not a missing path: a name taken.
        network.On(HttpMethod.Post, $"{DropboxApi}/files/get_metadata", _ => FakeNetwork.Json("""{"error_summary":"path/conflict/file/"}""", HttpStatusCode.Conflict));
        await Assert.That(await MessageAsync(() => folder.ChecksumAsync("IMG_1.JPG", ct))).StartsWith("A file named IMG_1.JPG is already there");
    }

    // ——— Box ———

    private static Made<BoxFolder> BoxWith(bool hasFolder, List<TimeSpan>? waited = null)
    {
        var made = Make((h, a, s, r) => new BoxFolder(h, a, s, r, (wait, _) => { waited?.Add(wait); return Task.CompletedTask; }));
        made.Network.On(HttpMethod.Get, $"{BoxApi}/folders/0/items", h => h.Url.Contains("offset=0")
                ? FakeNetwork.Json(hasFolder
                    ? """{"total_count":3,"entries":[{"type":"file","name":"PhotoSense","id":"1"},{"type":"folder","name":"Other","id":"2"}]}"""
                    : """{"total_count":1,"entries":[{"type":"folder","name":"Other","id":"2"}]}""")
                : FakeNetwork.Json("""{"total_count":3,"entries":[{"type":"folder","name":"PhotoSense","id":"F"}]}"""))
            .On(HttpMethod.Post, $"{BoxApi}/folders", _ => FakeNetwork.Json("""{"id":"F"}"""))
            .On(HttpMethod.Get, $"{BoxApi}/folders/F/items", h => h.Url.Contains("offset=0")
                ? FakeNetwork.Json("""{"total_count":5,"entries":[{"type":"folder","name":"Old","id":"9"},{"type":"file","name":"IMG_1.JPG","size":5,"id":"X","sha1":"AB"}]}""")
                : FakeNetwork.Json("""{"total_count":5,"entries":[]}"""));
        return made;
    }

    [Test]
    public async Task Box_Makes_Its_Folder_Once_And_Lists_It_A_Page_At_A_Time_Files_Only(CancellationToken ct)
    {
        var (folder, network, _) = BoxWith(hasFolder: false);
        await Assert.That(folder.Where).IsEqualTo("PhotoSense");
        await Assert.That(await folder.ListAsync(ct)).IsEquivalentTo([new RemoteFile("IMG_1.JPG", 5)]);
        await Assert.That(network.Asked.Single(h => h.Method == HttpMethod.Post).Body).IsEqualTo("""{"name":"PhotoSense","parent":{"id":"0"}}""");
        await folder.ListAsync(ct);
        await Assert.That(network.Asked.Count(h => h.Method == HttpMethod.Post)).IsEqualTo(1);
        // Found on a later page of the account's top folder.
        var (found, foundNetwork, _) = BoxWith(hasFolder: true);
        await found.ListAsync(ct);
        await Assert.That(foundNetwork.Asked.Any(h => h.Method == HttpMethod.Post)).IsFalse();
    }

    [Test]
    public async Task Box_Puts_A_File_Of_Up_To_50_MB_In_One_Request_Says_Its_SHA1_Reads_It_Back_Deletes_It_And_Says_How_Full_It_Is(CancellationToken ct)
    {
        var (folder, network, _) = BoxWith(hasFolder: true);
        network.On(HttpMethod.Post, $"{BoxUpload}/files/content", _ => FakeNetwork.Json("""{"entries":[{"sha1":"EF"}]}"""))
            .On(HttpMethod.Get, $"{BoxApi}/files/X/content", _ => FakeNetwork.Bytes([7]))
            .On(HttpMethod.Delete, $"{BoxApi}/files/X", _ => FakeNetwork.Status(HttpStatusCode.NoContent))
            .On(HttpMethod.Get, $"{BoxApi}/users/me", _ => FakeNetwork.Json("""{"space_amount":10,"space_used":4}"""));
        await Assert.That(await folder.PutAsync("IMG_2.JPG", new MemoryStream([1]), 1, ct)).IsEqualTo(new RemoteChecksum(ChecksumKind.Sha1, "ef"));
        var form = network.Asked.Single(h => h.Url.EndsWith("/files/content", StringComparison.Ordinal)).Body;
        await Assert.That(form.IndexOf("name=attributes", StringComparison.Ordinal)).IsLessThan(form.IndexOf("name=file", StringComparison.Ordinal));
        await Assert.That(form).Contains("""{"name":"IMG_2.JPG","parent":{"id":"F"}}""");
        await Assert.That(await folder.ChecksumAsync("IMG_1.JPG", ct)).IsEqualTo(new RemoteChecksum(ChecksumKind.Sha1, "ab"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => folder.ChecksumAsync("gone.JPG", ct));
        await Assert.That(await AllAsync(await folder.OpenReadAsync("IMG_1.JPG", ct), ct)).IsEquivalentTo(new byte[] { 7 });
        await folder.DeleteAsync("IMG_1.JPG", ct);
        await folder.DeleteAsync("gone.JPG", ct);
        await Assert.That(network.Asked.Count(h => h.Method == HttpMethod.Delete)).IsEqualTo(1);
        await Assert.That(await folder.SpaceAsync(ct)).IsEqualTo(new RemoteSpace(10, 4));
        network.On(HttpMethod.Get, $"{BoxApi}/folders/F/items", _ => FakeNetwork.Json("""{"total_count":1,"entries":[{"type":"file","name":"plain.JPG","id":"P"}]}"""));
        await Assert.That(await folder.ChecksumAsync("plain.JPG", ct)).IsNull();
    }

    /// <summary>A Box account that takes a large file in an upload session, asking to be asked again as it puts it together.</summary>
    private static void BoxSessions(FakeNetwork network, int asksAgain, string? retryAfter)
    {
        network.On(HttpMethod.Post, $"{BoxUpload}/files/upload_sessions", h => h.Url.EndsWith("/commit", StringComparison.Ordinal)
                ? asksAgain-- > 0 ? Accepted(retryAfter) : FakeNetwork.Json("""{"entries":[{"sha1":"AA"}]}""", HttpStatusCode.Created)
                : FakeNetwork.Json("""{"id":"S","part_size":31457280}""", HttpStatusCode.Created))
            .On(HttpMethod.Put, $"{BoxUpload}/files/upload_sessions/S", h => FakeNetwork.Json($$$"""{"part":{"part_id":"{{{h.Headers["Content-Range"].Split(' ', '-')[1]}}}"}}"""));
    }

    private static HttpResponseMessage Accepted(string? retryAfter)
    {
        var answer = FakeNetwork.Status(HttpStatusCode.Accepted);
        if (retryAfter is not null) answer.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        return answer;
    }

    [Test]
    public async Task Box_Puts_A_Larger_File_In_An_Upload_Session_Each_Piece_And_The_Whole_Checked_By_SHA1(CancellationToken ct)
    {
        var waited = new List<TimeSpan>();
        var (folder, network, _) = BoxWith(hasFolder: true, waited);
        BoxSessions(network, asksAgain: 2, retryAfter: null);
        var bytes = RandomNumberGenerator.GetBytes((int)BoxFolder.OneRequest + 10);
        await Assert.That(await folder.PutAsync("clip.MOV", new MemoryStream(bytes), bytes.Length, ct)).IsEqualTo(new RemoteChecksum(ChecksumKind.Sha1, "aa"));
        var started = network.Asked.First(h => h.Url == $"{BoxUpload}/files/upload_sessions");
        await Assert.That(started.Body).IsEqualTo($$"""{"folder_id":"F","file_size":{{bytes.Length}},"file_name":"clip.MOV"}""");
        var parts = network.Asked.Where(h => h.Method == HttpMethod.Put).ToList();
        await Assert.That(parts).Count().IsEqualTo(2);
        await Assert.That(parts[0].Headers["Digest"]).IsEqualTo($"sha={Convert.ToBase64String(SHA1.HashData(bytes.AsSpan(0, 31457280)))}");
        await Assert.That(parts.SelectMany(p => p.Bytes).SequenceEqual(bytes)).IsTrue();
        var commit = network.Asked.Last();
        await Assert.That(commit.Headers["Digest"]).IsEqualTo($"sha={Convert.ToBase64String(SHA1.HashData(bytes))}");
        await Assert.That(commit.Body).IsEqualTo("""{"parts":[{"part_id":"0"},{"part_id":"31457280"}]}""");
        // Asked to ask again, without saying when: a second each time.
        await Assert.That(waited).IsEquivalentTo([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)]);
    }

    // ——— Reaching each kind ———

    private static BackupLocation At(LocationKind kind, Dictionary<string, string>? settings = null)
        => new("id", kind, "x", true, null, settings ?? [], null, null, null, null);

    [Test]
    public async Task Each_Kind_Of_Location_Is_Reached_As_It_Was_Connected(CancellationToken ct)
    {
        var network = new FakeNetwork();
        var http = new HttpClient(network);
        var folders = new RemoteFolders(new CloudAccounts(Apps, http, Clock), http, TimeProvider.System);
        Task Renewed(IReadOnlyDictionary<string, string> s, CancellationToken token) => Task.CompletedTask;
        IRemoteFolder Open(LocationKind kind, Dictionary<string, string>? settings = null, Dictionary<string, string>? secrets = null) => folders.Open(At(kind, settings), secrets ?? Fresh, Renewed);
        await Assert.That(Open(LocationKind.GoogleDrive)).IsTypeOf<DriveFolder>();
        await Assert.That(Open(LocationKind.OneDrive)).IsTypeOf<OneDriveFolder>();
        await Assert.That(Open(LocationKind.Dropbox)).IsTypeOf<DropboxFolder>();
        await Assert.That(Open(LocationKind.Box)).IsTypeOf<BoxFolder>();
        await Assert.That(Open(LocationKind.AmazonS3, new() { ["bucket"] = "photos", ["region"] = "us-west-2 (Oregon)", ["prefix"] = "/Mine/" }).Where).IsEqualTo("photos/Mine");
        await Assert.That(Open(LocationKind.AmazonS3, new() { ["bucket"] = "photos" }).Where).IsEqualTo("photos/PhotoSense");
        await Assert.That(Open(LocationKind.S3Compatible, new() { ["bucket"] = "b", ["endpoint"] = "https://s3.example.com" }).Where).IsEqualTo("b/PhotoSense");
        await Assert.That(Open(LocationKind.AzureBlob, new() { ["account"] = "acct", ["container"] = "photos" }, new() { ["key"] = "?sv=1&sig=x" }).Where).IsEqualTo("photos/PhotoSense");
        await Assert.That(Open(LocationKind.AzureBlob, new() { ["account"] = "acct", ["container"] = "photos", ["auth"] = "Account key", ["tier"] = "Hot" },
            new() { ["key"] = Convert.ToBase64String(new byte[32]) }).Where).IsEqualTo("photos/PhotoSense");
        await Assert.That(Open(LocationKind.WebDav, new() { ["url"] = "https://nas/dav", ["user"] = "a" }).Where).IsEqualTo("/PhotoSense");
        await Assert.That(Open(LocationKind.WebDav, new() { ["url"] = "https://nas/dav", ["folder"] = "/Photos" }).Where).IsEqualTo("/Photos");
        using (var sftp = (SftpFolder)Open(LocationKind.Sftp, new() { ["host"] = "nas", ["port"] = "2222", ["user"] = "a" }, new() { ["pass"] = "p" }))
            await Assert.That(sftp.Where).IsEqualTo("PhotoSense");
        using var rsa = RSA.Create(2048);
        using (var keyed = (SftpFolder)Open(LocationKind.Sftp, new() { ["host"] = "nas", ["user"] = "a", ["auth"] = "SSH key", ["folder"] = "/srv/photos" }, new() { ["pass"] = rsa.ExportRSAPrivateKeyPem() }))
            await Assert.That(keyed.Where).IsEqualTo("/srv/photos");
        await Assert.That(() => Open(LocationKind.Computer)).Throws<ArgumentException>();

        // Box, reached this way, waits as long as it asks before asking again.
        var box = (BoxFolder)Open(LocationKind.Box);
        network.On(HttpMethod.Get, $"{BoxApi}/folders/0/items", _ => FakeNetwork.Json("""{"total_count":1,"entries":[{"type":"folder","name":"PhotoSense","id":"F"}]}"""));
        BoxSessions(network, asksAgain: 1, retryAfter: "0");
        var bytes = new byte[BoxFolder.OneRequest + 1];
        await Assert.That(await box.PutAsync("clip.MOV", new MemoryStream(bytes), bytes.Length, ct)).IsEqualTo(new RemoteChecksum(ChecksumKind.Sha1, "aa"));
    }
}
