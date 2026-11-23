using LiteDB;
using Trovolve.Domain.Repositories;

namespace Trovolve.Tests.Infrastructure;

/// <summary>Swipe to clean's record on a phone, kept in the database.</summary>
public class LiteDbSwipeStoreTests
{
    private static readonly DateOnly Day = new(2026, 11, 17);

    [Test]
    public async Task The_Day_Is_Kept_With_Its_Set_Its_Decisions_In_Order_What_Was_Spared_And_What_Was_Freed(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var swipes = db.Swipes();
        await Assert.That(await swipes.DayAsync(ct)).IsNull();

        await swipes.SaveDayAsync(new SwipeDay(Day, ["a", "b", "c"], [new SwipeDecision("a", true), new SwipeDecision("b", false)], ["a"], null), ct);
        var kept = await db.Swipes().DayAsync(ct);
        await Assert.That((kept!.Date, string.Join(",", kept.Set), string.Join(",", kept.Spared), kept.Freed)).IsEqualTo((Day, "a,b,c", "a", (SwipeFreed?)null));
        await Assert.That(kept.Decisions).IsEquivalentTo([new SwipeDecision("a", true), new SwipeDecision("b", false)], CollectionOrdering.Matching);

        // Only ever one day: the next takes its place.
        await swipes.SaveDayAsync(new SwipeDay(Day.AddDays(1), ["d"], [], [], new SwipeFreed(3, 4_000, 2)), ct);
        var next = await swipes.DayAsync(ct);
        await Assert.That((next!.Date, next.Set.Count, next.Decisions.Count, next.Freed)).IsEqualTo((Day.AddDays(1), 1, 0, (SwipeFreed?)new SwipeFreed(3, 4_000, 2)));
    }

    [Test]
    public async Task What_Was_Swiped_What_Is_Marked_And_The_Days_Swiped_Are_Each_Kept_And_Taken_Back(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var swipes = db.Swipes();
        await Assert.That(((await swipes.SwipedAsync(ct)).Count, (await swipes.MarkedAsync(ct)).Count, (await swipes.DaysAsync(ct)).Count)).IsEqualTo((0, 0, 0));

        await swipes.SetSwipedAsync(["a", "b", "c"], true, ct);
        await swipes.SetSwipedAsync(["b"], false, ct);
        await Assert.That(await db.Swipes().SwipedAsync(ct)).IsEquivalentTo(["a", "c"]);

        await swipes.SaveMarkedAsync(["c", "a"], ct);
        await Assert.That(await db.Swipes().MarkedAsync(ct)).IsEquivalentTo(["c", "a"], CollectionOrdering.Matching);
        await swipes.SaveMarkedAsync([], ct);
        await Assert.That(await swipes.MarkedAsync(ct)).IsEmpty();

        await swipes.SetDayAsync(Day, true, ct);
        await swipes.SetDayAsync(Day.AddDays(-1), true, ct);
        await swipes.SetDayAsync(Day.AddDays(-1), false, ct);
        await Assert.That(await db.Swipes().DaysAsync(ct)).IsEquivalentTo([Day]);
    }
}
