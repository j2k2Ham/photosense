using Microsoft.Azure.Functions.Worker.Http;
using PhotoSense.Functions.Api;

namespace PhotoSense.Tests.Functions;

public class RequestGuardTests
{
    private static HttpHeadersCollection Headers(params (string Name, string Value)[] headers)
    {
        var collection = new HttpHeadersCollection();
        foreach (var (name, value) in headers) collection.Add(name, value);
        return collection;
    }

    [Test]
    public async Task A_Plain_Request_As_Another_Web_Site_Could_Send_Is_Refused()
    {
        await Assert.That(RequestGuard.Allows(Headers(), configuredKey: null)).IsFalse();
        await Assert.That(RequestGuard.Allows(Headers(("Content-Type", "text/plain")), configuredKey: "")).IsFalse();
    }

    [Test]
    public async Task The_Web_Client_Is_Allowed_When_No_Key_Is_Configured()
        => await Assert.That(RequestGuard.Allows(Headers((RequestGuard.ClientHeader, "web")), configuredKey: null)).IsTrue();

    [Test]
    public async Task A_Configured_Key_Must_Also_Match()
    {
        await Assert.That(RequestGuard.Allows(Headers((RequestGuard.ClientHeader, "web")), "secret")).IsFalse();
        await Assert.That(RequestGuard.Allows(Headers((RequestGuard.ClientHeader, "web"), (RequestGuard.ApiKeyHeader, "wrong")), "secret")).IsFalse();
        await Assert.That(RequestGuard.Allows(Headers((RequestGuard.ApiKeyHeader, "secret")), "secret")).IsFalse();
        await Assert.That(RequestGuard.Allows(Headers((RequestGuard.ClientHeader, "web"), (RequestGuard.ApiKeyHeader, "secret")), "secret")).IsTrue();
    }
}
