using System.Net;
using Microsoft.AspNetCore.Builder;
using PhotoSense.Server;

namespace PhotoSense.Tests.Server;

/// <summary>The service as one program: the page, the addresses below /api, and who it answers.</summary>
public sealed class PhotoSenseServerTests : IDisposable
{
    // One client for every test, as HttpClient is meant to be used: each asks its own program by its whole address.
    private static readonly HttpClient Client = new();

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("photosense-program-");

    /// <summary>An address of the program a test started.</summary>
    private static Uri At(WebApplication app, string path) => new(new Uri(app.Urls.Single()), path);

    public void Dispose() => TestFiles.Remove(_root);

    [Test]
    public async Task The_Program_Serves_The_Page_And_The_Service_And_Lets_Only_The_Page_Send_Its_Header(CancellationToken ct)
    {
        var site = Directory.CreateDirectory(Path.Combine(_root.FullName, "site", "wwwroot"));
        await File.WriteAllTextAsync(Path.Combine(site.FullName, "index.html"), "<p>PhotoSense</p>", ct);
        await using var app = PhotoSenseServer.Build(
        [
            "--urls=http://127.0.0.1:0",
            $"--PhotoStorage:DatabasePath={Path.Combine(_root.FullName, "data", "photosense.db")}",
            $"--PhotoStorage:ThumbnailPath={Path.Combine(_root.FullName, "data", "thumbnails")}",
            "--Cors:Origins:0=http://localhost:3000",
        ], Path.Combine(_root.FullName, "site"), s => s.AddStandInStores());
        await app.StartAsync(ct);

        await Assert.That(await Client.GetStringAsync(At(app, "/"), ct)).IsEqualTo("<p>PhotoSense</p>");
        await Assert.That((await Client.GetAsync(At(app, "/api/health"), ct)).StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await Client.GetAsync(At(app, "/api/scan/groups"), ct)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

        // A browser asks first whether a page may send the client header, and only the page's own address may.
        async Task<string?> Allowed(string origin)
        {
            using var asking = new HttpRequestMessage(HttpMethod.Options, At(app, "/api/scan/groups"));
            asking.Headers.Add("Origin", origin);
            asking.Headers.Add("Access-Control-Request-Method", "GET");
            asking.Headers.Add("Access-Control-Request-Headers", "x-photosense-client");
            var answer = await Client.SendAsync(asking, ct);
            return answer.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) ? values.Single() : null;
        }
        await Assert.That(await Allowed("http://localhost:3000")).IsEqualTo("http://localhost:3000");
        await Assert.That(await Allowed("https://example.com")).IsNull();

        await app.StopAsync(ct);
    }

    [Test]
    [Arguments(null, "localhost", HttpStatusCode.OK)]
    [Arguments(null, "127.0.0.1", HttpStatusCode.OK)]
    [Arguments(null, "[::1]", HttpStatusCode.OK)]
    [Arguments(null, "photos.example", HttpStatusCode.BadRequest)]
    [Arguments("photos.example", "photos.example", HttpStatusCode.OK)]
    [Arguments("photos.example", "localhost", HttpStatusCode.BadRequest)]
    public async Task A_Page_Under_Another_Name_Is_Not_Answered_Unless_The_Settings_Name_It(string? allowed, string name, HttpStatusCode expected, CancellationToken ct)
    {
        await using var app = PhotoSenseServer.Build(
        [
            "--urls=http://127.0.0.1:0",
            $"--PhotoStorage:DatabasePath={Path.Combine(_root.FullName, "data", "photosense.db")}",
            $"--PhotoStorage:ThumbnailPath={Path.Combine(_root.FullName, "data", "thumbnails")}",
            .. allowed is null ? Array.Empty<string>() : [$"--AllowedHosts={allowed}"],
        ], _root.FullName, s => s.AddStandInStores());
        await app.StartAsync(ct);

        // As a page whose own name had been pointed at this machine would ask, client header and all.
        using var asking = new HttpRequestMessage(HttpMethod.Get, At(app, "/api/folders"));
        asking.Headers.Host = $"{name}:{new Uri(app.Urls.Single()).Port}";
        asking.Headers.Add("x-photosense-client", "1");
        await Assert.That((await Client.SendAsync(asking, ct)).StatusCode).IsEqualTo(expected);

        await app.StopAsync(ct);
    }

    [Test]
    public async Task Told_Nothing_It_Looks_For_Its_Settings_And_Page_Beside_The_Program(CancellationToken ct)
    {
        await using var app = PhotoSenseServer.Build([]);
        await Assert.That(app.Environment.ContentRootPath.TrimEnd(Path.DirectorySeparatorChar)).IsEqualTo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Test]
    public async Task Without_A_Page_Built_In_The_Service_Still_Answers(CancellationToken ct)
    {
        await using var app = PhotoSenseServer.Build(
        [
            "--urls=http://127.0.0.1:0",
            $"--PhotoStorage:DatabasePath={Path.Combine(_root.FullName, "data", "photosense.db")}",
            $"--PhotoStorage:ThumbnailPath={Path.Combine(_root.FullName, "data", "thumbnails")}",
        ], _root.FullName, s => s.AddStandInStores());
        await app.StartAsync(ct);

        await Assert.That((await Client.GetAsync(At(app, "/"), ct)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await Client.GetAsync(At(app, "/api/health"), ct)).StatusCode).IsEqualTo(HttpStatusCode.OK);

        await app.StopAsync(ct);
    }
}
