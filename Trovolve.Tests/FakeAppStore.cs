using Microsoft.Extensions.Logging.Abstractions;
using Trovolve.Application.Plans;
using Trovolve.Domain.Entities;
using Trovolve.Domain.Services;
using Trovolve.Domain.ValueObjects;

namespace Trovolve.Tests;

/// <summary>A phone's store in memory: what it sells, what the person has bought, and what the next purchase comes to.</summary>
internal sealed class FakeAppStore : IAppStore
{
    /// <summary>What the store sells, at the prices the design shows.</summary>
    public List<StoreProduct> Products { get; } =
    [
        new(PlanProducts.Yearly, "$19.99", 19.99m, "USD"),
        new(PlanProducts.Monthly, "$2.99", 2.99m, "USD"),
        new(PlanProducts.Lifetime, "$39.00", 39m, "USD"),
        new(PlanProducts.Family, "$29.99", 29.99m, "USD"),
    ];

    /// <summary>What the person has bought.</summary>
    public List<StorePurchase> Purchases { get; } = [];

    /// <summary>How the next purchase ends; a purchase that ends bought is added to <see cref="Purchases"/>.</summary>
    public StoreAnswer Answer { get; set; } = StoreAnswer.Bought;

    /// <summary>What a restore finds, added to <see cref="Purchases"/>.</summary>
    public List<StorePurchase> Restorable { get; } = [];

    /// <summary>The store cannot be reached: every call fails as a store says so.</summary>
    public bool Unreachable { get; set; }

    /// <summary>The products asked to be bought, in order.</summary>
    public List<string> Bought { get; } = [];

    /// <summary>How many times the store's page for managing subscriptions was opened.</summary>
    public int Managed { get; private set; }

    /// <inheritdoc />
    public string Name => "App Store";

    private void Reach(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Unreachable) throw new StoreException("The App Store could not be reached. Try again.");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoreProduct>> ProductsAsync(IReadOnlyList<string> ids, CancellationToken ct)
    {
        Reach(ct);
        return Task.FromResult<IReadOnlyList<StoreProduct>>([.. Products.Where(p => ids.Contains(p.Id))]);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StorePurchase>> PurchasesAsync(CancellationToken ct)
    {
        Reach(ct);
        return Task.FromResult<IReadOnlyList<StorePurchase>>([.. Purchases]);
    }

    /// <inheritdoc />
    public Task<StoreAnswer> BuyAsync(string productId, CancellationToken ct)
    {
        Reach(ct);
        Bought.Add(productId);
        if (Answer == StoreAnswer.Bought) Purchases.Add(new StorePurchase(productId, TestClock.Day, productId == PlanProducts.Lifetime ? null : TestClock.Day.AddYears(1), Trial: false));
        return Task.FromResult(Answer);
    }

    /// <inheritdoc />
    public Task RestoreAsync(CancellationToken ct)
    {
        Reach(ct);
        Purchases.AddRange(Restorable);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ManageAsync(CancellationToken ct)
    {
        Reach(ct);
        Managed++;
        return Task.CompletedTask;
    }
}

/// <summary>Plans for tests that make their services themselves.</summary>
internal static class TestPlans
{
    /// <summary>The plan made of <paramref name="holder"/>: a computer's, with no store, when none is given.</summary>
    public static Subscription Subscription(PlanParts? holder = null) => new((holder ?? new PlanParts()).Store, holder?.Clock ?? new TestClock(), holder?.App, NullLogger<Subscription>.Instance, holder?.Dev);

    /// <summary>Whether the terms of use were accepted, kept in a store of its own that says they have not been, dated by the test's clock.</summary>
    public static TermsAcceptance Terms() => new(new MemoryPlanStore(), new TestClock());

    /// <summary>The free plan's daily removals, counted in <paramref name="holder"/>'s store, on its clock.</summary>
    public static DailyRemovals Removals(PlanParts? holder = null)
    {
        holder ??= new PlanParts();
        return new DailyRemovals(holder.Store, new CodedProductSettings(), Subscription(holder), holder.Clock);
    }

    /// <summary>Removals with no limit: a store that says Pro was bought once.</summary>
    public static DailyRemovals Unlimited() => Removals(ProParts());

    /// <summary>Pro, bought once for good.</summary>
    public static Subscription Pro() => Subscription(ProParts());

    // A phone's store that says Pro was bought once.
    private static PlanParts ProParts()
    {
        var app = new FakeAppStore();
        app.Purchases.Add(new StorePurchase(PlanProducts.Lifetime, TestClock.Day, null, Trial: false));
        return new PlanParts { App = app };
    }
}

/// <summary>What a test's plan is made of: its store of counts, its clock, and the phone's store, if any.</summary>
internal sealed class PlanParts
{
    /// <summary>Each day's count and the shared plan.</summary>
    public MemoryPlanStore Store { get; init; } = new();
    /// <summary>The clock.</summary>
    public TimeProvider Clock { get; init; } = new TestClock();
    /// <summary>The phone's store; null for a computer.</summary>
    public FakeAppStore? App { get; init; }
    /// <summary>Whether this is a development build, with plans for testing; off unless given.</summary>
    public DevMode? Dev { get; init; }
}
