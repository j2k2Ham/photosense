using LiteDB;
using Trovolve.Domain.Entities;

namespace Trovolve.Tests.Infrastructure;

/// <summary>Each day's count of removals and the plan a paired phone shared, kept in the database.</summary>
public class LiteDbPlanStoreTests
{
    private static readonly DateOnly Day = new(2026, 11, 13);

    [Test]
    public async Task Each_Days_Removals_Are_Counted_On_Their_Own(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var plan = db.Plan();
        await Assert.That(await plan.RemovedOnAsync(Day, ct)).IsEqualTo(0);

        await plan.AddRemovedAsync(Day, 12, ct);
        await plan.AddRemovedAsync(Day, 26, ct);
        await plan.AddRemovedAsync(Day.AddDays(1), 3, ct);

        await Assert.That(await db.Plan().RemovedOnAsync(Day, ct)).IsEqualTo(38);
        await Assert.That(await db.Plan().RemovedOnAsync(Day.AddDays(1), ct)).IsEqualTo(3);
        await Assert.That(db.GetCollection("removals_by_day").FindById("2026-11-13")["Count"].AsInt32).IsEqualTo(38);

        // Given back, down to none and no further.
        await plan.AddRemovedAsync(Day, -30, ct);
        await Assert.That(await plan.RemovedOnAsync(Day, ct)).IsEqualTo(8);
        await plan.AddRemovedAsync(Day, -30, ct);
        await Assert.That(await plan.RemovedOnAsync(Day, ct)).IsEqualTo(0);
    }

    [Test]
    public async Task A_Shared_Plan_Is_Kept_In_Place_Of_The_One_Before_And_Can_Be_Forgotten(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var plan = db.Plan();
        var trial = new Entitlement(PlanTier.Trial, PlanKind.Yearly, TestClock.Day, TestClock.Day.AddDays(7), "Jamie's iPhone");
        var lifetime = new Entitlement(PlanTier.Pro, PlanKind.Lifetime, SharedBy: "Sam's Pixel");
        await Assert.That(await plan.SharedAsync(ct)).IsNull();

        await plan.KeepSharedAsync(trial, ct);
        await Assert.That(await db.Plan().SharedAsync(ct)).IsEqualTo(trial);
        await plan.KeepSharedAsync(lifetime, ct);
        await Assert.That(await db.Plan().SharedAsync(ct)).IsEqualTo(lifetime);

        await plan.KeepSharedAsync(null, ct);
        await Assert.That(await plan.SharedAsync(ct)).IsNull();
    }

    [Test]
    public async Task The_Day_The_Terms_Were_Accepted_Is_Kept_And_Read_Back(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var plan = db.Plan();
        await Assert.That(await plan.AcceptedAsync(ct)).IsNull();

        await plan.KeepAcceptedAsync(Day, ct);

        await Assert.That(await db.Plan().AcceptedAsync(ct)).IsEqualTo(Day);
        await Assert.That(db.GetCollection("terms_accepted").FindById("accepted")["Day"].AsString).IsEqualTo("2026-11-13");
        var gone = new CancellationToken(canceled: true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => plan.AcceptedAsync(gone));
        await Assert.ThrowsAsync<OperationCanceledException>(() => plan.KeepAcceptedAsync(Day.AddDays(1), gone));
        await Assert.That(await plan.AcceptedAsync(ct)).IsEqualTo(Day);
    }

    [Test]
    public async Task The_Plan_Chosen_For_Testing_Is_Kept_Until_Another_Is(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        await Assert.That(await db.Plan().TestPlanAsync(ct)).IsEqualTo(TestPlan.AsBought);

        await db.Plan().KeepTestPlanAsync(TestPlan.Trial, ct);
        await Assert.That(await db.Plan().TestPlanAsync(ct)).IsEqualTo(TestPlan.Trial);
        await db.Plan().KeepTestPlanAsync(TestPlan.AsBought, ct);
        await Assert.That(await db.Plan().TestPlanAsync(ct)).IsEqualTo(TestPlan.AsBought);
        await Assert.That(db.GetCollection("test_plan").Count()).IsEqualTo(1);
    }

    [Test]
    public async Task Nothing_Is_Answered_Or_Kept_For_A_Caller_That_Has_Gone(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var plan = db.Plan();
        var gone = new CancellationToken(canceled: true);

        await Assert.ThrowsAsync<OperationCanceledException>(() => plan.RemovedOnAsync(Day, gone));
        await Assert.ThrowsAsync<OperationCanceledException>(() => plan.AddRemovedAsync(Day, 1, gone));
        await Assert.ThrowsAsync<OperationCanceledException>(() => plan.SharedAsync(gone));
        await Assert.ThrowsAsync<OperationCanceledException>(() => plan.KeepSharedAsync(null, gone));
        await Assert.ThrowsAsync<OperationCanceledException>(() => plan.TestPlanAsync(gone));
        await Assert.ThrowsAsync<OperationCanceledException>(() => plan.KeepTestPlanAsync(TestPlan.Free, gone));
        await Assert.That(await plan.RemovedOnAsync(Day, ct)).IsEqualTo(0);
    }
}
