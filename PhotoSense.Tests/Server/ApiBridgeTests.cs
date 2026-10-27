using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using PhotoSense.Server;

namespace PhotoSense.Tests.Server;

public sealed class ApiBridgeTests : IDisposable
{
    private readonly ServerFixture _f = new();

    public void Dispose() => _f.Dispose();

    // A request as Kestrel hands it on, with somewhere to write the answer.
    private async Task<HttpContext> SendAsync(string method, string pathAndQuery, string? body = null, params (string Name, string Value)[] headers)
    {
        var context = new DefaultHttpContext { RequestServices = _f.Services };
        var query = pathAndQuery.IndexOf('?');
        context.Request.Method = method;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost", 7071);
        context.Request.Path = query < 0 ? pathAndQuery : pathAndQuery[..query];
        context.Request.QueryString = query < 0 ? QueryString.Empty : new QueryString(pathAndQuery[query..]);
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body ?? string.Empty));
        foreach (var (name, value) in headers) context.Request.Headers[name] = value;
        context.Response.Body = new MemoryStream();
        await ApiBridge.HandleAsync(context);
        return context;
    }

    private static string Body(HttpContext context) => Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

    [Test]
    public async Task An_Address_Below_Api_Is_Answered_With_The_Endpoints_Status_Headers_And_Body()
    {
        var answered = await SendAsync("GET", "/api/health?ignored=1");

        await Assert.That(answered.Response.StatusCode).IsEqualTo(200);
        await Assert.That(answered.Response.ContentType).IsEqualTo("application/json; charset=utf-8");
        await Assert.That(answered.Response.ContentLength).IsEqualTo(((MemoryStream)answered.Response.Body).Length);
        await Assert.That(JsonDocument.Parse(Body(answered)).RootElement.GetProperty("status").GetString()).IsEqualTo("ok");
    }

    [Test]
    public async Task The_Headers_And_The_Body_Of_A_Request_Reach_The_Endpoint()
    {
        var fromThePage = await SendAsync("POST", "/api/scan/start", "{}", ("x-photosense-client", "web"));
        var fromElsewhere = await SendAsync("POST", "/api/scan/start", "{}");

        await Assert.That(fromThePage.Response.StatusCode).IsEqualTo(400);
        await Assert.That(JsonDocument.Parse(Body(fromThePage)).RootElement.GetProperty("error").GetString()).IsEqualTo("A folder to scan is required.");
        // Refused with nothing to say: no length at all is sent.
        await Assert.That((fromElsewhere.Response.StatusCode, fromElsewhere.Response.ContentLength, Body(fromElsewhere))).IsEqualTo((401, (long?)null, ""));
    }

    [Test]
    [Arguments("GET", "/api/nothing-here")]
    [Arguments("POST", "/api/health")]
    [Arguments("GET", "/api/photos/not-a-guid/image")]
    public async Task An_Address_Nobody_Answers_Is_Not_Found(string method, string path)
        => await Assert.That((await SendAsync(method, path)).Response.StatusCode).IsEqualTo(404);
}
