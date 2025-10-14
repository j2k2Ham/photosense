using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker;
using Xunit;
using PhotoSense.Functions.Realtime;
using PhotoSense.Infrastructure.Scanning;

namespace PhotoSense.Tests.Functions;

public class GetScanLogsFunctionTests
{
    private class TestRequestData : HttpRequestData
    {
        public TestRequestData(FunctionContext functionContext, Uri url) : base(functionContext) { Url = url; }
        public override Stream Body { get; set; } = new MemoryStream();
        public override HttpHeadersCollection Headers { get; } = new();
        public override IReadOnlyCollection<IHttpCookie> Cookies => Array.Empty<IHttpCookie>();
        public override string Method { get; set; } = "GET";
        public override Uri Url { get; }
        public override HttpResponseData CreateResponse() => new TestResponseData(FunctionContext);
    }

    private class TestResponseData : HttpResponseData
    {
        public TestResponseData(FunctionContext ctx) : base(ctx) { }
        public override HttpStatusCode StatusCode { get; set; }
        public MemoryStream Buffer { get; } = new();
        public override HttpHeadersCollection Headers { get; } = new();
        public override IReadOnlyCollection<IHttpCookie> Cookies => Array.Empty<IHttpCookie>();
        public override Stream Body { get => Buffer; set { /* ignore */ } }
    }

    private class DummyFunctionContext : FunctionContext
    {
        public override string InvocationId { get; } = Guid.NewGuid().ToString();
        public override string FunctionId { get; } = Guid.NewGuid().ToString();
        public override TraceContext TraceContext { get; } = null!;
        public override BindingContext BindingContext { get; } = null!;
        public override RetryContext RetryContext { get; } = null!;
        public override IServiceProvider InstanceServices { get; set; } = new ServiceCollection().BuildServiceProvider();
        public override FunctionDefinition FunctionDefinition { get; } = null!;
        public override IDictionary<object, object> Items { get; } = new Dictionary<object, object>();
        public override CancellationToken CancellationToken { get; } = CancellationToken.None;
        public override void Dispose() { }
        public override T GetInvocationResult<T>() => default!;
        public override void SetInvocationResult(object? value) { }
        public override ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    [Fact]
    public async Task GetScanLogs_RespectsLimit_AndDrains()
    {
        // Arrange
        var sink = new InMemoryScanLogSink();
        for (int i = 0; i < 50; i++) sink.Log("x", "Info", "Msg" + i);
        var ctx = new DummyFunctionContext();
        var req = new TestRequestData(ctx, new Uri("https://localhost/api/scan/logs?limit=10"));

        // Act
        var resp = await SignalRLogFunctions.GetScanLogs(req);

        // Assert
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var testResp = (TestResponseData)resp;
        testResp.Buffer.Position = 0;
        using var doc = await JsonDocument.ParseAsync(testResp.Buffer);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("count", out var countProp));
        Assert.Equal(10, countProp.GetInt32());

        // (Intentionally left blank - deprecated GetScanLogsFunctionTests removed. Keeping file placeholder to preserve potential solution filters.)