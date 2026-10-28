using System.Net;
using PhotoSense.Server;

namespace PhotoSense.Tests.Server;

public sealed class PhotoSenseServerTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("photosense-program-");

    public void Dispose() => TestFiles.Remove(_root);

    [Test]
    public async Task The_Program_Serves_The_Page_And_The_Service_And_Lets_Only_The_Page_Send_Its_Header()
    {
        var site = Directory.CreateDirectory(Path.Combine(_root.FullName, "site", "wwwroot"));
        await File.WriteAllTextAsync(Path.Combine(site.FullName, "index.html"), "<p>PhotoSense</p>");
        await using var app = PhotoSenseServer.Build(
        [
            "--urls=http://127.0.0.1:0",
            $"--PhotoStorage:DatabasePath={Path.Combine(_root.FullName, "data", "photosense.db")}",
            $"--PhotoStorage:ThumbnailPath={Path.Combine(_root.FullName, "data", "thumbnails")}",
            "--Cors:Origins:0=http://localhost:3000",
        ], Path.Combine(_root.FullName, "site"));
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        await Assert.That(await http.GetStringAsync("/")).IsEqualTo("<p>PhotoSense</p>");
        await Assert.That((await http.GetAsync("/api/health")).StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await http.GetAsync("/api/scan/groups")).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

        // A browser asks first whether a page may send the client header, and only the page's own address may.
        async Task<string?> Allowed(string origin)
        {
            using var asking = new HttpRequestMessage(HttpMethod.Options, "/api/scan/groups");
            asking.Headers.Add("Origin", origin);
            asking.Headers.Add("Access-Control-Request-Method", "GET");
            asking.Headers.Add("Access-Control-Request-Headers", "x-photosense-client");
            var answer = await http.SendAsync(asking);
            return answer.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) ? values.Single() : null;
        }
        await Assert.That(await Allowed("http://localhost:3000")).IsEqualTo("http://localhost:3000");
        await Assert.That(await Allowed("https://example.com")).IsNull();

        await app.StopAsync();
    }

    [Test]
    [Arguments(null, "localhost", HttpStatusCode.OK)]
    [Arguments(null, "127.0.0.1", HttpStatusCode.OK)]
    [Arguments(null, "[::1]", HttpStatusCode.OK)]
    [Arguments(null, "photos.example", HttpStatusCode.BadRequest)]
    [Arguments("photos.example", "photos.example", HttpStatusCode.OK)]
    [Arguments("photos.example", "localhost", HttpStatusCode.BadRequest)]
    public async Task A_Page_Under_Another_Name_Is_Not_Answered_Unless_The_Settings_Name_It(string? allowed, string name, HttpStatusCode expected)
    {
        await using var app = PhotoSenseServer.Build(
        [
            "--urls=http://127.0.0.1:0",
            $"--PhotoStorage:DatabasePath={Path.Combine(_root.FullName, "data", "photosense.db")}",
            $"--PhotoStorage:ThumbnailPath={Path.Combine(_root.FullName, "data", "thumbnails")}",
            .. allowed is null ? Array.Empty<string>() : [$"--AllowedHosts={allowed}"],
        ], _root.FullName);
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        // As a page whose own name had been pointed at this machine would ask, client header and all.
        using var asking = new HttpRequestMessage(HttpMethod.Get, "/api/folders");
        asking.Headers.Host = $"{name}:{new Uri(app.Urls.Single()).Port}";
        asking.Headers.Add("x-photosense-client", "1");
        await Assert.That((await http.SendAsync(asking)).StatusCode).IsEqualTo(expected);

        await app.StopAsync();
    }

    [Test]
    public async Task Told_Nothing_It_Looks_For_Its_Settings_And_Page_Beside_The_Program()
    {
        await using var app = PhotoSenseServer.Build([]);
        await Assert.That(app.Environment.ContentRootPath.TrimEnd(Path.DirectorySeparatorChar)).IsEqualTo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Test]
    public async Task Without_A_Page_Built_In_The_Service_Still_Answers()
    {
        await using var app = PhotoSenseServer.Build(
        [
            "--urls=http://127.0.0.1:0",
            $"--PhotoStorage:DatabasePath={Path.Combine(_root.FullName, "data", "photosense.db")}",
            $"--PhotoStorage:ThumbnailPath={Path.Combine(_root.FullName, "data", "thumbnails")}",
        ], _root.FullName);
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        await Assert.That((await http.GetAsync("/")).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await http.GetAsync("/api/health")).StatusCode).IsEqualTo(HttpStatusCode.OK);

        await app.StopAsync();
    }
}
