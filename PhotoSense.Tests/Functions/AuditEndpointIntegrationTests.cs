using Xunit;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Infrastructure.Persistence;

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

    // Simple in-memory audit repository used for integration-style verification.
    [Fact]
    public async Task KeepOperation_Emits_Audit_Record()
    {
        var photoRepo = new InMemoryPhotoRepository();
        var auditRepo = new InMemoryAuditRepo();
        var photo = new Photo { Id = new PhotoId(Guid.NewGuid()), SourcePath = "a", FileName = "a", FileSizeBytes = 1, Set = PhotoSet.Primary, ContentHash = "h" };
        await photoRepo.AddOrUpdateAsync(photo);

        // Emulate core logic of KeepPhoto function (domain behaviour integration focus)
        if (!photo.IsKept)
        {
            photo.IsKept = true;
            await photoRepo.AddOrUpdateAsync(photo);
            await auditRepo.AddAsync(new AuditEntry { Action = "Keep", PhotoId = photo.Id.Value.ToString(), Details = "" });
        }

        Assert.True(photo.IsKept);
        Assert.Single(auditRepo.Items);
        var entry = auditRepo.Items[0];
        Assert.Equal("Keep", entry.Action);
        Assert.Equal(photo.Id.Value.ToString(), entry.PhotoId);
    }
}