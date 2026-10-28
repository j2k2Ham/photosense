using LiteDB;
using PhotoSense.Domain.Entities;
using PhotoSense.Infrastructure.Persistence;

namespace PhotoSense.Tests.Infrastructure;

public sealed class AuditRepositoryTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"photosense-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        foreach (var file in new[] { _db, Path.ChangeExtension(_db, ".own.db") })
            if (File.Exists(file)) File.Delete(file);
    }

    [Test]
    public async Task The_Audit_Trail_Keeps_What_Was_Done_And_When()
    {
        using (var repo = new LiteDbAuditRepository(_db))
        {
            await repo.AddAsync(new AuditEntry { Action = "Keep", PhotoId = "p1", UtcTimestamp = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
            await repo.AddAsync(new AuditEntry { Action = "Move", Details = "Trips", UtcTimestamp = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc) });
        }

        using var db = new LiteDatabase(_db);
        var kept = db.GetCollection<AuditEntry>("audit").FindAll().OrderBy(a => a.UtcTimestamp).Select(a => $"{a.Action} {a.PhotoId} {a.Details}").ToList();
        await Assert.That(kept).IsEquivalentTo(["Keep p1 ", "Move  Trips"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task An_Audit_Trail_On_A_Shared_Database_Leaves_It_Open_For_Its_Other_Users()
    {
        using var shared = new LiteDatabase(_db);
        var repo = new LiteDbAuditRepository(shared);
        await repo.AddAsync(new AuditEntry { Action = "Keep" });
        repo.Dispose();
        repo.Dispose(); // a second time does nothing

        await Assert.That(shared.GetCollection<AuditEntry>("audit").Count()).IsEqualTo(1);

        var own = new LiteDbAuditRepository(Path.ChangeExtension(_db, ".own.db"));
        own.Dispose();
        own.Dispose();
    }
}
