using PhotoSense.Api.Endpoints;
using PhotoSense.Api.Http;

namespace PhotoSense.Tests.Endpoints;

/// <summary>Telling the page's own requests from those any other web page could make.</summary>
public class RequestGuardTests
{
    private static ApiHeaders Headers(params (string Name, string Value)[] headers)
    {
        var collection = new ApiHeaders();
        foreach (var (name, value) in headers) collection.Add(name, value);
        return collection;
    }

    [Test]
    public async Task A_Plain_Request_As_Another_Web_Site_Could_Send_Is_Refused(CancellationToken ct)
    {
        await Assert.That(RequestGuard.Allows(Headers(), configuredKey: null)).IsFalse();
        await Assert.That(RequestGuard.Allows(Headers(("Content-Type", "text/plain")), configuredKey: "")).IsFalse();
    }

    [Test]
    public async Task The_Web_Client_Is_Allowed_When_No_Key_Is_Configured(CancellationToken ct)
        => await Assert.That(RequestGuard.Allows(Headers((RequestGuard.ClientHeader, "web")), configuredKey: null)).IsTrue();

    [Test]
    public async Task A_Configured_Key_Must_Also_Match(CancellationToken ct)
    {
        await Assert.That(RequestGuard.Allows(Headers((RequestGuard.ClientHeader, "web")), "secret")).IsFalse();
        await Assert.That(RequestGuard.Allows(Headers((RequestGuard.ClientHeader, "web"), (RequestGuard.ApiKeyHeader, "wrong")), "secret")).IsFalse();
        await Assert.That(RequestGuard.Allows(Headers((RequestGuard.ApiKeyHeader, "secret")), "secret")).IsFalse();
        await Assert.That(RequestGuard.Allows(Headers((RequestGuard.ClientHeader, "web"), (RequestGuard.ApiKeyHeader, "secret")), "secret")).IsTrue();
    }
}
