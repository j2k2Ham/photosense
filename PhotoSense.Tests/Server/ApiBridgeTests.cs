using System.Net;
using System.Text;
using System.Text.Json;

namespace PhotoSense.Tests.Server;

/// <summary>Handing a web request to the service's addresses, and sending back their answer.</summary>
public sealed class ApiBridgeTests
{
    // One client for every test, as HttpClient is meant to be used: each asks its own program by its whole address.
    private static readonly HttpClient Client = new();

    private static async Task<(HttpStatusCode Status, string? Type, long? Length, string Body)> SendAsync(string method, string pathAndQuery, CancellationToken ct,
        string? body = null, params (string Name, string Value)[] headers)
    {
        using var f = await ServerFixture.StartedAsync(ct);
        using var asking = new HttpRequestMessage(new HttpMethod(method), f.At(pathAndQuery)) { Content = new StringContent(body ?? string.Empty, Encoding.UTF8) };
        foreach (var (name, value) in headers) asking.Headers.Add(name, value);
        using var answer = await Client.SendAsync(asking, ct);
        var text = await answer.Content.ReadAsStringAsync(ct);
        await f.Program.StopAsync(ct);
        return (answer.StatusCode, answer.Content.Headers.ContentType?.ToString(), answer.Content.Headers.ContentLength, text);
    }

    [Test]
    public async Task An_Address_Below_Api_Is_Answered_With_The_Endpoints_Status_Headers_And_Body(CancellationToken ct)
    {
        var answered = await SendAsync("GET", "/api/health?ignored=1", ct);

        await Assert.That(answered.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(answered.Type).IsEqualTo("application/json; charset=utf-8");
        await Assert.That(answered.Length).IsEqualTo(Encoding.UTF8.GetByteCount(answered.Body));
        await Assert.That(JsonDocument.Parse(answered.Body).RootElement.GetProperty("status").GetString()).IsEqualTo("ok");
    }

    [Test]
    public async Task The_Headers_And_The_Body_Of_A_Request_Reach_The_Endpoint(CancellationToken ct)
    {
        var fromThePage = await SendAsync("POST", "/api/scan/start", ct, "{}", ("x-photosense-client", "web"));
        var fromElsewhere = await SendAsync("POST", "/api/scan/start", ct, "{}");

        await Assert.That(fromThePage.Status).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(JsonDocument.Parse(fromThePage.Body).RootElement.GetProperty("error").GetString()).IsEqualTo("A folder to scan is required.");
        // Refused with nothing to say: an empty answer.
        await Assert.That((fromElsewhere.Status, fromElsewhere.Length, fromElsewhere.Body)).IsEqualTo((HttpStatusCode.Unauthorized, (long?)0, ""));
    }

    [Test]
    [Arguments("GET", "/api/nothing-here")]
    [Arguments("POST", "/api/health")]
    [Arguments("GET", "/api/photos/not-a-guid/image")]
    public async Task An_Address_Nobody_Answers_Is_Not_Found(string method, string path, CancellationToken ct)
        => await Assert.That((await SendAsync(method, path, ct)).Status).IsEqualTo(HttpStatusCode.NotFound);
}
