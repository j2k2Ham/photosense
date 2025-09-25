using Xunit;
using Moq;
using PhotoSense.Functions.Api;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Entities;
using PhotoSense.Infrastructure.Persistence;
using PhotoSense.Application.Scanning.Interfaces;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using System.Security.Claims;

namespace PhotoSense.Tests.Functions;

public class AuditEndpointIntegrationTests
{
    private class InMemoryAuditRepo : IAuditRepository
    {
        private readonly List<AuditEntry> _list = new();
        public Task AddAsync(AuditEntry entry, CancellationToken ct = default){ _list.Add(entry); return Task.CompletedTask; }
        public Task<IReadOnlyList<AuditEntry>> RecentAsync(int take = 200, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AuditEntry>>(_list.TakeLast(take).ToList());
        public IReadOnlyList<AuditEntry> Items => _list;
    }

    private class DummyRequest : HttpRequestData
    {
        public DummyRequest(FunctionContext ctx) : base(ctx) { }
        public override Stream Body { get; set; } = new MemoryStream();
        public override HttpHeadersCollection Headers { get; } = new();
        public override IReadOnlyCollection<IHttpCookie> Cookies => Array.Empty<IHttpCookie>();
        public override Uri Url { get; } = new Uri("http://localhost/");
        public override IEnumerable<ClaimsIdentity> Identities => Array.Empty<ClaimsIdentity>();
        public override string Method { get; } = "POST";
        public override HttpResponseData CreateResponse() => new DummyResponse(FunctionContext);
    }
    private class DummyResponse : HttpResponseData
    {
        public DummyResponse(FunctionContext ctx) : base(ctx) { }
        public override HttpStatusCode StatusCode { get; set; }
        public override HttpHeadersCollection Headers { get; } = new();
        public override Stream Body { get; set; } = new MemoryStream();
        public override HttpCookies Cookies { get; } = new();
    }

    private class DummyContext : FunctionContext
    {
        private readonly Dictionary<object, object> _items = new();
        public override string InvocationId => Guid.NewGuid().ToString();
        public override string FunctionId => Guid.NewGuid().ToString();
        public override TraceContext TraceContext => null!;
        public override BindingContext BindingContext => null!;
        public override IServiceProvider InstanceServices { get; set; } = null!;
        public override FunctionDefinition FunctionDefinition => null!;
        public override IInvocationFeatures Features => null!;
        public override IDictionary<object, object> Items => _items;
        public override CancellationToken CancellationToken => CancellationToken.None;
        public override RetryContext RetryContext => null!;
        public override IServiceProvider ServiceProvider => InstanceServices;
    }

    [Fact]
    public async Task KeepPhoto_Writes_Audit()
    {
        var photoRepo = new InMemoryPhotoRepository();
        var auditRepo = new InMemoryAuditRepo();
        var logSink = new Mock<IScanLogSink>();
        var functions = new PhotosFunctions();
        var photo = new Photo { SourcePath="a", FileName="a", FileSizeBytes=1, Set=PhotoSet.Primary, ContentHash="h" };
        await photoRepo.AddOrUpdateAsync(photo);
        var ctx = new DummyContext();
        var req = new DummyRequest(ctx);
        var resp = await functions.KeepPhotoAsync(req, photo.Id.Value.ToString(), photoRepo, logSink.Object, auditRepo);
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        Assert.Single(auditRepo.Items);
        Assert.Equal("Keep", auditRepo.Items.First().Action);
    }
}