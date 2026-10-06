using Microsoft.Azure.Functions.Worker.Http;
using PhotoSense.Functions.Api;
using Xunit;

namespace PhotoSense.Tests.Functions;

public class RequestGuardTests
{
    private static HttpHeadersCollection Headers(params (string Name, string Value)[] headers)
    {
        var collection = new HttpHeadersCollection();
        foreach (var (name, value) in headers) collection.Add(name, value);
        return collection;
    }

    [Fact]
    public void A_Plain_Request_As_Another_Web_Site_Could_Send_Is_Refused()
    {
        Assert.False(RequestGuard.Allows(Headers(), configuredKey: null));
        Assert.False(RequestGuard.Allows(Headers(("Content-Type", "text/plain")), configuredKey: ""));
    }

    [Fact]
    public void The_Web_Client_Is_Allowed_When_No_Key_Is_Configured()
        => Assert.True(RequestGuard.Allows(Headers((RequestGuard.ClientHeader, "web")), configuredKey: null));

    [Fact]
    public void A_Configured_Key_Must_Also_Match()
    {
        Assert.False(RequestGuard.Allows(Headers((RequestGuard.ClientHeader, "web")), "secret"));
        Assert.False(RequestGuard.Allows(Headers((RequestGuard.ClientHeader, "web"), (RequestGuard.ApiKeyHeader, "wrong")), "secret"));
        Assert.False(RequestGuard.Allows(Headers((RequestGuard.ApiKeyHeader, "secret")), "secret"));
        Assert.True(RequestGuard.Allows(Headers((RequestGuard.ClientHeader, "web"), (RequestGuard.ApiKeyHeader, "secret")), "secret"));
    }
}
