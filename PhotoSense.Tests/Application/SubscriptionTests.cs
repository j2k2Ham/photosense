using PhotoSense.Application.Plans;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Tests.Application;

/// <summary>The plan, bought through a phone's store or shared by a paired phone, and the free plan's removals each day.</summary>
public class SubscriptionTests
{
    private readonly FakeAppStore _app = new();
    private readonly TestClock _clock = new();
    private readonly PlanParts _phone;
    private readonly PlanParts _computer;

    public SubscriptionTests()
    {
        _phone = new PlanParts { App = _app, Clock = _clock };
        _computer = new PlanParts { Clock = _clock };
    }

    private static Entitlement Pro(DateTimeOffset? until) => new(PlanTier.Pro, PlanKind.Yearly, TestClock.Day, until);

    // ---- the plan

    [Test]
    public async Task A_Phone_Has_The_Plan_Its_Store_Says_Was_Bought(CancellationToken ct)
    {
        var subscription = TestPlans.Subscription(_phone);
        await Assert.That(await subscription.CurrentAsync(ct)).IsEqualTo(Entitlement.Free);

        _app.Purchases.Add(new StorePurchase(PlanProducts.Monthly, TestClock.Day, TestClock.Day.AddMonths(1), Trial: false));

        await Assert.That(await subscription.CurrentAsync(ct)).IsEqualTo(new Entitlement(PlanTier.Pro, PlanKind.Monthly, TestClock.Day, TestClock.Day.AddMonths(1)));
        await Assert.That(subscription.StoreName).IsEqualTo("App Store");
    }

    [Test]
    public async Task A_Computer_Has_No_Store_And_Has_The_Plan_A_Phone_Shared_Until_It_Ends(CancellationToken ct)
    {
        var subscription = TestPlans.Subscription(_computer);
        await subscription.ShareFromAsync("Jamie's iPhone", Pro(TestClock.Day.AddDays(1)), ct);

        await Assert.That(subscription.StoreName).IsNull();
        await Assert.That(await subscription.CurrentAsync(ct)).IsEqualTo(Pro(TestClock.Day.AddDays(1)) with { SharedBy = "Jamie's iPhone" });

        _clock.Now = TestClock.Day.AddDays(1);
        await Assert.That(await subscription.CurrentAsync(ct)).IsEqualTo(Entitlement.Free);
    }

    [Test]
    public async Task A_Phone_With_Nothing_Bought_Still_Has_A_Plan_Another_Phone_Shared(CancellationToken ct)
    {
        await _phone.Store.KeepSharedAsync(Pro(null) with { SharedBy = "Sam's Pixel" }, ct);

        await Assert.That((await TestPlans.Subscription(_phone).CurrentAsync(ct)).SharedBy).IsEqualTo("Sam's Pixel");
    }

    [Test]
    public async Task While_The_Store_Cannot_Be_Reached_What_It_Last_Said_Stands_Until_Its_End(CancellationToken ct)
    {
        var subscription = TestPlans.Subscription(_phone);
        _app.Unreachable = true;
        await Assert.That(await subscription.CurrentAsync(ct)).IsEqualTo(Entitlement.Free);

        _app.Unreachable = false;
        _app.Purchases.Add(new StorePurchase(PlanProducts.Yearly, TestClock.Day, TestClock.Day.AddDays(3), Trial: true));
        await Assert.That((await subscription.CurrentAsync(ct)).Tier).IsEqualTo(PlanTier.Trial);

        _app.Unreachable = true;
        await Assert.That((await subscription.CurrentAsync(ct)).Tier).IsEqualTo(PlanTier.Trial);
        _clock.Now = TestClock.Day.AddDays(3);
        await Assert.That(await subscription.CurrentAsync(ct)).IsEqualTo(Entitlement.Free);
    }

    // ---- sharing with a computer

    [Test]
    public async Task A_Phone_On_The_Free_Plan_Takes_Back_Only_The_Plan_It_Shared(CancellationToken ct)
    {
        var subscription = TestPlans.Subscription(_computer);
        await subscription.ShareFromAsync("Sam's Pixel", Entitlement.Free, ct);
        await Assert.That(await _computer.Store.SharedAsync(ct)).IsNull();

        await subscription.ShareFromAsync("Jamie's iPhone", Pro(null), ct);
        await subscription.ShareFromAsync("Sam's Pixel", Entitlement.Free, ct);
        await Assert.That((await _computer.Store.SharedAsync(ct))!.SharedBy).IsEqualTo("Jamie's iPhone");

        await subscription.ShareFromAsync("Jamie's iPhone", Entitlement.Free, ct);
        await Assert.That(await _computer.Store.SharedAsync(ct)).IsNull();
    }

    // ---- what is for sale, buying, restoring and managing

    [Test]
    public async Task The_Plans_For_Sale_Are_What_The_Store_Sells_Of_Photosenses_At_Its_Prices(CancellationToken ct)
    {
        _app.Products.Add(new StoreProduct("com.example.coins", "$0.99", 0.99m, "USD"));
        _app.Products.RemoveAll(p => p.Id == PlanProducts.Family);
        var offers = await TestPlans.Subscription(_phone).OffersAsync(ct);

        await Assert.That(offers.Select(o => $"{o.Kind} {o.Product.Price}")).IsEquivalentTo(["Yearly $19.99", "Monthly $2.99", "Lifetime $39.00"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Nothing_Is_Offered_On_A_Computer_Or_While_The_Store_Cannot_Be_Reached(CancellationToken ct)
    {
        await Assert.That(await TestPlans.Subscription(_computer).OffersAsync(ct)).IsEmpty();
        _app.Unreachable = true;
        await Assert.That(await TestPlans.Subscription(_phone).OffersAsync(ct)).IsEmpty();
    }

    [Test]
    public async Task A_Plan_Is_Bought_Through_The_Store_By_Its_Id(CancellationToken ct)
    {
        var subscription = TestPlans.Subscription(_phone);

        await Assert.That(await subscription.BuyAsync(PlanKind.Lifetime, ct)).IsEqualTo(StoreAnswer.Bought);
        await Assert.That(_app.Bought).IsEquivalentTo([PlanProducts.Lifetime]);
        await Assert.That((await subscription.CurrentAsync(ct)).Kind).IsEqualTo(PlanKind.Lifetime);
    }

    [Test]
    public async Task No_Plan_Is_Bought_For_Nothing_Or_On_A_Computer(CancellationToken ct)
    {
        var none = await Assert.ThrowsAsync<InvalidOperationException>(() => TestPlans.Subscription(_phone).BuyAsync(PlanKind.None, ct));
        var computer = await Assert.ThrowsAsync<InvalidOperationException>(() => TestPlans.Subscription(_computer).BuyAsync(PlanKind.Yearly, ct));

        await Assert.That(none!.Message).IsEqualTo("That plan is not for sale.");
        await Assert.That(computer!.Message).IsEqualTo("Plans are bought in PhotoSense on a phone or tablet.");
    }

    [Test]
    public async Task Restoring_Asks_The_Store_Again_And_Answers_With_The_Plan_Then(CancellationToken ct)
    {
        _app.Restorable.Add(new StorePurchase(PlanProducts.Family, TestClock.Day, TestClock.Day.AddYears(1), Trial: false));

        await Assert.That((await TestPlans.Subscription(_phone).RestoreAsync(ct)).Tier).IsEqualTo(PlanTier.Family);
    }

    [Test]
    public async Task Managing_Opens_The_Stores_Page(CancellationToken ct)
    {
        await TestPlans.Subscription(_phone).ManageAsync(ct);

        await Assert.That(_app.Managed).IsEqualTo(1);
    }

    [Test]
    public async Task A_Computer_Neither_Restores_Nor_Manages_A_Plan(CancellationToken ct)
    {
        var subscription = TestPlans.Subscription(_computer);

        await Assert.That((await Assert.ThrowsAsync<InvalidOperationException>(() => subscription.RestoreAsync(ct)))!.Message).IsEqualTo("Purchases are restored in PhotoSense on a phone or tablet.");
        await Assert.That((await Assert.ThrowsAsync<InvalidOperationException>(() => subscription.ManageAsync(ct)))!.Message).IsEqualTo("Subscriptions are managed in PhotoSense on a phone or tablet.");
    }

    // ---- the free plan's removals each day

    [Test]
    public async Task The_Free_Plan_Removes_Fifty_A_Day_As_The_Code_Sets_It(CancellationToken ct)
    {
        await Assert.That((await new CodedProductSettings().GetAsync(ct)).FreeRemovalsPerDay).IsEqualTo(50);
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await new CodedProductSettings().GetAsync(new CancellationToken(canceled: true)));
    }

    [Test]
    public async Task Removals_Count_Down_From_Fifty_And_Start_Again_The_Next_Day(CancellationToken ct)
    {
        var removals = TestPlans.Removals(_computer);
        var today = await removals.TodayAsync(ct);
        await Assert.That((today.PerDay, today.Used, today.Left, today.Unlimited)).IsEqualTo((50, 0, (int?)50, false));

        await removals.RecordAsync(38, today, ct);
        today = await removals.TodayAsync(ct);
        await Assert.That(today.Left).IsEqualTo(12);
        await Assert.That(today.Allows(12)).IsTrue();
        await Assert.That(today.Allows(13)).IsFalse();

        await removals.RecordAsync(20, today, ct);
        await Assert.That((await removals.TodayAsync(ct)).Left).IsEqualTo(0);

        _clock.Now = TestClock.Day.AddDays(1);
        await Assert.That((await removals.TodayAsync(ct)).Left).IsEqualTo(50);
    }

    [Test]
    public async Task Pro_Has_No_Limit_And_Its_Removals_Leave_The_Free_Ones_Whole(CancellationToken ct)
    {
        _app.Purchases.Add(new StorePurchase(PlanProducts.Lifetime, TestClock.Day, null, Trial: false));
        var removals = TestPlans.Removals(_phone);
        var today = await removals.TodayAsync(ct);

        await Assert.That((today.Left, today.Unlimited, today.Allows(100_000))).IsEqualTo(((int?)null, true, true));
        await removals.RecordAsync(500, today, ct);
        await Assert.That(await _phone.Store.RemovedOnAsync(DateOnly.FromDateTime(_clock.GetLocalNow().DateTime), ct)).IsEqualTo(0);
    }

    [Test]
    public async Task Nothing_Removed_Counts_For_Nothing(CancellationToken ct)
    {
        var removals = TestPlans.Removals(_computer);
        await removals.RecordAsync(0, await removals.TodayAsync(ct), ct);

        await Assert.That((await removals.TodayAsync(ct)).Used).IsEqualTo(0);
    }

    [Test]
    public async Task A_Refusal_Says_How_Many_Were_Chosen_And_How_Many_Are_Left_Or_That_None_Are(CancellationToken ct)
    {
        await Assert.That(DailyRemovals.Refusal(new RemovalAllowance(50, 12, false), 5998))
            .IsEqualTo("You chose 5,998 items. The free plan deletes 50 items a day, and you have 38 left today. Nothing was removed.");
        await Assert.That(DailyRemovals.Refusal(new RemovalAllowance(50, 50, false), 3))
            .IsEqualTo("You have used today's 50 free removals. More are available tomorrow. Nothing was removed.");
    }
}
