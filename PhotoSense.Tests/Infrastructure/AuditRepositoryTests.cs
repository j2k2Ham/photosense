using LiteDB;
using PhotoSense.Domain.Entities;

namespace PhotoSense.Tests.Infrastructure;

/// <summary>The record of what was done to photos.</summary>
public sealed class AuditRepositoryTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"photosense-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        if (File.Exists(_db)) File.Delete(_db);
    }

    [Test]
    public async Task The_Audit_Trail_Keeps_What_Was_Done_And_When(CancellationToken ct)
    {
        // A day apart, by the clock the store stamps each entry with; a time the caller sets is not the store's.
        using (var written = new LiteDatabase(_db))
        {
            var repo = written.Audit(new TestClock(TestClock.Day, TimeSpan.FromDays(1)));
            await repo.AddAsync(new AuditEntry { Action = "Keep", PhotoId = "p1", UtcTimestamp = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) }, ct);
            await repo.AddAsync(new AuditEntry { Action = "Move", Details = "Trips" }, ct);
        }

        using var db = new LiteDatabase(_db);
        var kept = db.GetCollection<AuditEntry>("audit").FindAll().OrderBy(a => a.UtcTimestamp).Select(a => $"{a.Action} {a.PhotoId} {a.Details} {a.UtcTimestamp.ToUniversalTime():yyyy-MM-dd}").ToList();
        await Assert.That(kept).IsEquivalentTo(["Keep p1  2026-10-15", "Move  Trips 2026-10-16"], CollectionOrdering.Matching);
    }
}
