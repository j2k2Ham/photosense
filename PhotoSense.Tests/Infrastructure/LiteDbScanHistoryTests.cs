using LiteDB;
using PhotoSense.Domain.Entities;
using PhotoSense.Infrastructure.Persistence;

namespace PhotoSense.Tests.Infrastructure;

public class LiteDbScanHistoryTests
{
    [Test]
    public async Task Hands_Back_The_Latest_Scans_Newest_First()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var history = new LiteDbScanHistory(db);
        await Assert.That(await history.RecentAsync()).IsEmpty();

        var day = new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
        await history.AddAsync(new ScanRecord { StartedUtc = day.AddHours(10), Seconds = 780.5, Total = 6_940, Read = 6_940 });
        await history.AddAsync(new ScanRecord { StartedUtc = day.AddHours(21), Seconds = 4, Total = 6_940, Read = 1, Unchanged = 6_939 });
        await history.AddAsync(new ScanRecord { StartedUtc = day.AddHours(14), Seconds = 6, Total = 6_941, Read = 2, Unchanged = 6_939 });

        var recent = await history.RecentAsync();
        await Assert.That(recent.Select(s => s.Read)).IsEquivalentTo(new[] { 1, 2, 6_940 }, CollectionOrdering.Matching);
        await Assert.That((recent[2].Seconds, recent[2].Total, recent[1].Unchanged, recent[2].StartedUtc.ToUniversalTime())).IsEqualTo((780.5, 6_940, 6_939, day.AddHours(10)));
        await Assert.That((await history.RecentAsync(take: 2)).Select(s => s.Read)).IsEquivalentTo(new[] { 1, 2 }, CollectionOrdering.Matching);

        // Another store on the same database finds what the first one kept.
        await Assert.That((await new LiteDbScanHistory(db).RecentAsync()).Count).IsEqualTo(3);
    }
}
