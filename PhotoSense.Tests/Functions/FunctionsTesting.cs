using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PhotoSense.Functions.Api;

namespace PhotoSense.Tests.Functions;

/// <summary>A request as the Functions worker hands it to an HTTP function, built in memory.</summary>
internal sealed class FakeHttpRequest : HttpRequestData
{
    private static readonly FunctionContext Context = CreateContext();

    public FakeHttpRequest(string method, string pathAndQuery, string? body = null) : base(Context)
    {
        Method = method;
        Url = new Uri("http://localhost:7071/api/" + pathAndQuery.TrimStart('/'));
        Body = new MemoryStream(Encoding.UTF8.GetBytes(body ?? string.Empty));
    }

    public override Stream Body { get; }
    public override HttpHeadersCollection Headers { get; } = new();
    public override IReadOnlyCollection<IHttpCookie> Cookies => [];
    public override Uri Url { get; }
    public override IEnumerable<ClaimsIdentity> Identities => [];
    public override string Method { get; }
    public override HttpResponseData CreateResponse() => new FakeHttpResponse(FunctionContext);

    /// <summary>Marks the request as coming from the web client, as changes to photos must be.</summary>
    public FakeHttpRequest FromClient(string? apiKey = null)
    {
        Headers.Add(RequestGuard.ClientHeader, "test");
        if (apiKey is not null) Headers.Add(RequestGuard.ApiKeyHeader, apiKey);
        return this;
    }

    public FakeHttpRequest With(string header, string value)
    {
        Headers.Add(header, value);
        return this;
    }

    // Writing JSON to a response looks up the worker's serializer through the function context.
    private static FunctionContext CreateContext()
    {
        var services = new ServiceCollection();
        services.AddOptions<WorkerOptions>().Configure(o => o.Serializer = new JsonObjectSerializer());
        var context = new Mock<FunctionContext>();
        context.SetupProperty(c => c.InstanceServices, services.BuildServiceProvider());
        return context.Object;
    }
}

internal sealed class FakeHttpResponse : HttpResponseData
{
    public FakeHttpResponse(FunctionContext context) : base(context) { }
    public override HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
    public override HttpHeadersCollection Headers { get; set; } = new();
    public override Stream Body { get; set; } = new MemoryStream();
    public override HttpCookies Cookies => Mock.Of<HttpCookies>();
}

internal static class Http
{
    public static FakeHttpRequest Get(string pathAndQuery) => new("GET", pathAndQuery);
    public static FakeHttpRequest Post(string pathAndQuery, string? body = null) => new("POST", pathAndQuery, body);
    public static FakeHttpRequest Delete(string pathAndQuery) => new("DELETE", pathAndQuery);

    public static byte[] Bytes(this HttpResponseData response) => ((MemoryStream)response.Body).ToArray();
    public static string Text(this HttpResponseData response) => Encoding.UTF8.GetString(response.Bytes());
    public static JsonElement Json(this HttpResponseData response) => JsonDocument.Parse(response.Bytes()).RootElement;
    public static string? Header(this HttpResponseData response, string name)
        => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
}
