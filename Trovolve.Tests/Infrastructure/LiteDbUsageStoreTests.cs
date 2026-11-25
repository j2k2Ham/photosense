using LiteDB;
using Trovolve.Domain.Entities;

namespace Trovolve.Tests.Infrastructure;

/// <summary>The uses of Trovolve's features waiting to be sent, kept in the database.</summary>
public class LiteDbUsageStoreTests
{
    private static readonly Guid Session = Guid.Parse("6d1f8a4e-0000-4000-8000-000000000001");

    [Test]
    public async Task Uses_Are_Numbered_In_Order_And_Keep_Their_Numbers_After_Those_Before_Are_Sent(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var usage = db.Usage();
        var install = await usage.InstallAsync(ct);
        await Assert.That(install).IsNotEqualTo(Guid.Empty);

        var first = await usage.AddAsync(TestClock.Day, "POST scan/start", 200, Session, 100, ct);
        await usage.AddAsync(TestClock.Day.AddSeconds(1), "GET trips", 200, Session, 100, ct);
        await usage.AddAsync(TestClock.Day.AddSeconds(2), "POST organize/apply", 402, Session, 100, ct);
        await Assert.That(first).IsEqualTo(new FeatureUse(1, TestClock.Day, "POST scan/start", 200, Session));

        // Read again from the database: oldest first, up to the most asked for.
        var oldest = await db.Usage().OldestAsync(2, ct);
        await Assert.That(oldest.Select(u => (u.Sequence, u.Feature, u.Status, u.At))).IsEquivalentTo(
            [(1L, "POST scan/start", 200, TestClock.Day), (2L, "GET trips", 200, TestClock.Day.AddSeconds(1))], CollectionOrdering.Matching);

        // Sent and forgotten, the numbers run on: the server can tell the order of every use.
        await usage.ForgetThroughAsync(2, ct);
        await Assert.That(await usage.CountAsync(ct)).IsEqualTo(1);
        await usage.ForgetThroughAsync(3, ct);
        var next = await usage.AddAsync(TestClock.Day.AddSeconds(3), "GET year", 200, Session, 100, ct);
        await Assert.That((next.Sequence, await db.Usage().InstallAsync(ct))).IsEqualTo((4L, install));
    }

    [Test]
    public async Task Past_The_Most_Kept_The_Oldest_Uses_Are_Forgotten(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var usage = db.Usage();
        for (var i = 0; i < 5; i++) await usage.AddAsync(TestClock.Day.AddSeconds(i), $"GET feature/{i}", 200, Session, 3, ct);

        await Assert.That((await usage.OldestAsync(10, ct)).Select(u => u.Sequence)).IsEquivalentTo([3L, 4L, 5L], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Each_Way_In_Checks_The_Token_First()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var usage = db.Usage();
        var gone = new CancellationToken(canceled: true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => usage.InstallAsync(gone));
        await Assert.ThrowsAsync<OperationCanceledException>(() => usage.AddAsync(TestClock.Day, "GET trips", 200, Session, 10, gone));
        await Assert.ThrowsAsync<OperationCanceledException>(() => usage.OldestAsync(1, gone));
        await Assert.ThrowsAsync<OperationCanceledException>(() => usage.CountAsync(gone));
        await Assert.ThrowsAsync<OperationCanceledException>(() => usage.ForgetThroughAsync(1, gone));
        await Assert.That(await usage.CountAsync(CancellationToken.None)).IsEqualTo(0);
    }
}
