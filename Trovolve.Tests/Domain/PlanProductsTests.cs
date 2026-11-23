using Trovolve.Domain.Entities;
using Trovolve.Domain.Services;
using Trovolve.Domain.ValueObjects;

namespace Trovolve.Tests.Domain;

/// <summary>What Trovolve sells, and the plan someone has from what they bought.</summary>
public class PlanProductsTests
{
    private static readonly DateTimeOffset Now = TestClock.Day;

    [Test]
    [Arguments(PlanKind.Yearly, PlanProducts.Yearly)]
    [Arguments(PlanKind.Monthly, PlanProducts.Monthly)]
    [Arguments(PlanKind.Lifetime, PlanProducts.Lifetime)]
    [Arguments(PlanKind.Family, PlanProducts.Family)]
    public async Task Each_Plan_Has_The_Stores_Id_And_The_Id_Names_The_Plan(PlanKind kind, string id, CancellationToken ct)
    {
        await Assert.That(PlanProducts.IdOf(kind)).IsEqualTo(id);
        await Assert.That(PlanProducts.KindOf(id)).IsEqualTo(kind);
    }

    [Test]
    public async Task Nothing_Is_Sold_For_No_Plan_And_Another_Apps_Id_Is_No_Plan(CancellationToken ct)
    {
        await Assert.That(PlanProducts.IdOf(PlanKind.None)).IsNull();
        await Assert.That(PlanProducts.KindOf("com.example.coins")).IsEqualTo(PlanKind.None);
        await Assert.That(PlanProducts.All).IsEquivalentTo([PlanProducts.Yearly, PlanProducts.Monthly, PlanProducts.Lifetime, PlanProducts.Family], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Nothing_Bought_Is_The_Free_Plan(CancellationToken ct)
    {
        var plan = PlanProducts.PlanFrom([], Now);

        await Assert.That(plan).IsEqualTo(Entitlement.Free);
        await Assert.That(plan.Pro).IsFalse();
    }

    [Test]
    public async Task A_Yearly_Plan_In_Its_Trial_Is_The_Trial_Until_It_Renews(CancellationToken ct)
    {
        var plan = PlanProducts.PlanFrom([new StorePurchase(PlanProducts.Yearly, Now.AddDays(-2), Now.AddDays(5), Trial: true)], Now);

        await Assert.That(plan).IsEqualTo(new Entitlement(PlanTier.Trial, PlanKind.Yearly, Now.AddDays(-2), Now.AddDays(5)));
        await Assert.That(plan.Pro).IsTrue();
    }

    [Test]
    public async Task The_Widest_Plan_Bought_Wins_Family_Then_Lifetime_Then_Yearly_Then_Monthly(CancellationToken ct)
    {
        var monthly = new StorePurchase(PlanProducts.Monthly, Now, Now.AddMonths(1), Trial: false);
        var yearly = new StorePurchase(PlanProducts.Yearly, Now, Now.AddYears(1), Trial: false);
        var lifetime = new StorePurchase(PlanProducts.Lifetime, Now, null, Trial: false);
        var family = new StorePurchase(PlanProducts.Family, Now, Now.AddYears(1), Trial: false);

        await Assert.That(PlanProducts.PlanFrom([monthly, yearly], Now).Kind).IsEqualTo(PlanKind.Yearly);
        await Assert.That(PlanProducts.PlanFrom([yearly, lifetime, monthly], Now)).IsEqualTo(new Entitlement(PlanTier.Pro, PlanKind.Lifetime, Now));
        await Assert.That(PlanProducts.PlanFrom([lifetime, family], Now).Tier).IsEqualTo(PlanTier.Family);
        await Assert.That(PlanProducts.PlanFrom([monthly], Now).Tier).IsEqualTo(PlanTier.Pro);
    }

    [Test]
    public async Task A_Plan_That_Has_Run_Out_Or_Another_Apps_Purchase_Counts_For_Nothing(CancellationToken ct)
    {
        var ended = new StorePurchase(PlanProducts.Yearly, Now.AddYears(-1), Now, Trial: false);
        var other = new StorePurchase("com.example.coins", Now, null, Trial: false);

        await Assert.That(PlanProducts.PlanFrom([ended, other], Now)).IsEqualTo(Entitlement.Free);
    }

    [Test]
    public async Task A_Plan_Stands_Until_Its_End_And_One_With_No_End_For_Good(CancellationToken ct)
    {
        await Assert.That(Entitlement.Free.StandsAt(Now)).IsTrue();
        await Assert.That(new Entitlement(PlanTier.Pro, PlanKind.Monthly, Until: Now.AddSeconds(1)).StandsAt(Now)).IsTrue();
        await Assert.That(new Entitlement(PlanTier.Pro, PlanKind.Monthly, Until: Now).StandsAt(Now)).IsFalse();
    }
}
