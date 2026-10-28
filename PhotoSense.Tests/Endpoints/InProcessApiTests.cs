using System.Net;
using System.Text;
using PhotoSense.Api.Hosting;
using PhotoSense.Api.Http;

namespace PhotoSense.Tests.Endpoints;

/// <summary>The page's requests answered inside the program that shows it, with no web server between.</summary>
public sealed class InProcessApiTests
{
    private static readonly Uri Base = new("app://photosense/api/");

    // Answers with what it was asked, so a test can see what reached it.
    private static readonly ApiRouter Router = new ApiRouter()
        .Post("echo/{name}", async (request, values) =>
        {
            var answer = request.CreateResponse(HttpStatusCode.Created);
            answer.Headers.Add("X-Name", values["name"]);
            answer.Headers.Add("X-Asked", [request.Method, request.Headers.GetValues("x-photosense-client").Single()]);
            await answer.WriteStringAsync(await new StreamReader(request.Body).ReadToEndAsync());
            return answer;
        })
        .Get("empty", (request, _) => Task.FromResult(request.CreateResponse(HttpStatusCode.NoContent)))
        .Get("broken", (_, _) => throw new InvalidOperationException("The database could not be opened."))
        .Get("abandoned", (request, _) => Task.FromCanceled<ApiResponse>(request.Aborted));

    [Test]
    public async Task A_Request_Reaches_Its_Endpoint_With_Its_Headers_And_Body_And_The_Whole_Answer_Comes_Back()
    {
        var answer = await Router.AnswerAsync("POST", new Uri(Base, "echo/scan"), [new("x-photosense-client", "phone")], Encoding.UTF8.GetBytes("""{"recursive":true}"""));

        await Assert.That(answer.Status).IsEqualTo(201);
        await Assert.That((answer.Headers["x-name"], answer.Headers["X-Asked"], answer.Headers["Content-Type"])).IsEqualTo(("scan", "POST, phone", "text/plain; charset=utf-8"));
        await Assert.That(Encoding.UTF8.GetString(answer.Body)).IsEqualTo("""{"recursive":true}""");

        var empty = await Router.AnswerAsync("GET", new Uri(Base, "empty"), [], body: null);
        await Assert.That((empty.Status, empty.Headers.Count, empty.Body.Length)).IsEqualTo((204, 0, 0));
    }

    [Test]
    public async Task An_Address_Nothing_Answers_And_An_Endpoint_That_Fails_Are_Answered_With_What_Went_Wrong()
    {
        var missing = await Router.AnswerAsync("GET", new Uri(Base, "nothing"), [], null);
        await Assert.That((missing.Status, Encoding.UTF8.GetString(missing.Body))).IsEqualTo((404, "Nothing answers at /api/nothing."));

        var broken = await Router.AnswerAsync("GET", new Uri(Base, "broken"), [], null);
        await Assert.That((broken.Status, broken.Headers["content-type"], Encoding.UTF8.GetString(broken.Body)))
            .IsEqualTo((500, "text/plain; charset=utf-8", "The database could not be opened."));
    }

    [Test]
    public async Task A_Request_Whose_Web_View_Has_Gone_Away_Is_Given_Up()
        => await Assert.ThrowsAsync<OperationCanceledException>(() => Router.AnswerAsync("GET", new Uri(Base, "abandoned"), [], null, new CancellationToken(true)));

    [Test]
    [Arguments("/", "index.html")]
    [Arguments("", "index.html")]
    [Arguments("/_next/static/chunks/main.js", "_next/static/chunks/main.js")]
    [Arguments("/404", "404.html")]
    [Arguments("/guide/", "guide/index.html")]
    [Arguments("/My%20Photos.png", "My Photos.png")]
    public async Task An_Address_Is_Answered_By_The_File_Or_The_Page_Of_That_Name(string path, string expected)
    {
        string[] built = ["index.html", "404.html", "_next/static/chunks/main.js", "guide/index.html", "My Photos.png"];
        await Assert.That(PageFiles.For(path, built.Contains)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("/missing.js")]
    [Arguments("/../photosense.db")]
    [Arguments("/_next/%2E%2E/%2E%2E/photosense.db")]
    [Arguments("/./index.html")]
    [Arguments("/_next\\..\\photosense.db")]
    public async Task Nothing_Outside_The_Page_And_Nothing_It_Lacks_Is_Handed_Out(string path)
        => await Assert.That(PageFiles.For(path, file => !file.Contains("missing"))).IsNull();

    [Test]
    [Arguments("index.html", "text/html; charset=utf-8")]
    [Arguments("_next/static/chunks/MAIN.JS", "text/javascript; charset=utf-8")]
    [Arguments("_next/static/media/font.woff2", "font/woff2")]
    [Arguments("data.bin", "application/octet-stream")]
    public async Task Each_File_Of_The_Page_Is_Said_To_Be_What_It_Is(string file, string expected)
        => await Assert.That(PageFiles.ContentType(file)).IsEqualTo(expected);
}
