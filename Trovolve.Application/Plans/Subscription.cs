using Trovolve.Domain.Entities;
using Trovolve.Domain.Repositories;
using Trovolve.Domain.Services;
using Trovolve.Domain.ValueObjects;

namespace Trovolve.Application.Plans;

/// <summary>A plan as offered: what it is, and its price as the store says.</summary>
/// <param name="Kind">The plan.</param>
/// <param name="Product">The store's product, with its price.</param>
public sealed record PlanOffer(PlanKind Kind, StoreProduct Product);

/// <summary>
/// The plan the person has, and buying one. On a phone the plan is what its store says was bought; a computer, which has
/// no store, has the plan a paired phone last told it of, until that plan's end.
/// </summary>
public sealed class Subscription
{
    private readonly IPlanStore _store;
    private readonly TimeProvider _time;
    private readonly IAppStore? _app;
    private readonly DevMode _dev;
    // What the store last said was bought, for when it cannot be reached: a store gone quiet does not take Pro away.
    private Entitlement _lastBought = Entitlement.Free;

    /// <summary>The plan kept in <paramref name="store"/>, and bought through <paramref name="app"/> where the device has a store.</summary>
    /// <param name="store">What this device keeps of the plan.</param>
    /// <param name="time">The clock a plan's end is compared with.</param>
    /// <param name="app">The phone's own store; null on a computer.</param>
    /// <param name="dev">Whether this is a development build, in which a plan can be chosen for testing; off when not given.</param>
    public Subscription(IPlanStore store, TimeProvider time, IAppStore? app, DevMode? dev = null)
    {
        _store = store;
        _time = time;
        _app = app;
        _dev = dev ?? DevMode.Off;
    }

    /// <summary>
    /// The plan chosen for testing, in a development build; null in any other, where there is none to choose.
    /// </summary>
    public async Task<TestPlan?> TestPlanAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return _dev.On ? await _store.TestPlanAsync(ct) : null;
    }

    /// <summary>Chooses the plan to test as, until another is chosen; <see cref="TestPlan.AsBought"/> goes back to what was bought.</summary>
    /// <exception cref="InvalidOperationException">This is not a development build.</exception>
    public async Task ChooseTestPlanAsync(TestPlan plan, CancellationToken ct)
    {
        if (!_dev.On) throw new InvalidOperationException("Plans for testing are only in a development build.");
        await _store.KeepTestPlanAsync(plan, ct);
    }

    /// <summary>What the store is called, or null on a computer, which has none.</summary>
    public string? StoreName => _app?.Name;

    /// <summary>
    /// The plan now: in a development build, the plan chosen for testing, when one is; otherwise what was bought through
    /// this device's store when that is Pro, otherwise the plan a paired phone shared, while it stands; otherwise the
    /// free plan. When the store cannot be reached, what it last said stands.
    /// </summary>
    public async Task<Entitlement> CurrentAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        if (_dev.On && TestPlanEntitlements.At(await _store.TestPlanAsync(ct), now) is { } test) return test;
        if (await BoughtAsync(now, ct) is { Pro: true } bought) return bought;
        return await _store.SharedAsync(ct) is { } shared && shared.StandsAt(now) ? shared : Entitlement.Free;
    }

    private async Task<Entitlement> BoughtAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (_app is null) return Entitlement.Free;
        try
        {
            return _lastBought = PlanProducts.PlanFrom(await _app.PurchasesAsync(ct), now);
        }
        catch (StoreException)
        {
            return _lastBought.StandsAt(now) ? _lastBought : Entitlement.Free;
        }
    }

    /// <summary>The plans for sale, with the store's prices; none on a computer, or while the store cannot be reached.</summary>
    public async Task<IReadOnlyList<PlanOffer>> OffersAsync(CancellationToken ct)
    {
        if (_app is null) return [];
        IReadOnlyList<StoreProduct> products;
        try { products = await _app.ProductsAsync(PlanProducts.All, ct); }
        // No price is shown that the store has not given: without one, nothing is offered.
        catch (StoreException) { return []; }
        return [.. products.Select(p => new PlanOffer(PlanProducts.KindOf(p.Id), p)).Where(o => o.Kind != PlanKind.None)];
    }

    /// <summary>Buys a plan through the store, answering how it ended; a computer cannot buy one.</summary>
    /// <exception cref="InvalidOperationException">There is no store here, or the plan is not sold.</exception>
    public Task<StoreAnswer> BuyAsync(PlanKind kind, CancellationToken ct)
    {
        var app = _app ?? throw new InvalidOperationException("Plans are bought in Trovolve on a phone or tablet.");
        var id = PlanProducts.IdOf(kind) ?? throw new InvalidOperationException("That plan is not for sale.");
        return app.BuyAsync(id, ct);
    }

    /// <summary>Asks the store again for what was bought with the same account, and answers with the plan then.</summary>
    /// <exception cref="InvalidOperationException">There is no store here.</exception>
    public async Task<Entitlement> RestoreAsync(CancellationToken ct)
    {
        var app = _app ?? throw new InvalidOperationException("Purchases are restored in Trovolve on a phone or tablet.");
        await app.RestoreAsync(ct);
        return await CurrentAsync(ct);
    }

    /// <summary>Opens the store's page for changing or cancelling the subscription.</summary>
    /// <exception cref="InvalidOperationException">There is no store here.</exception>
    public Task ManageAsync(CancellationToken ct)
        => (_app ?? throw new InvalidOperationException("Subscriptions are managed in Trovolve on a phone or tablet.")).ManageAsync(ct);

    /// <summary>
    /// Keeps the plan a paired phone says it has. A Pro plan is kept, named by the phone, until its end; a phone that says
    /// it is on the free plan takes back only a plan it shared itself, so that another phone's plan still stands.
    /// </summary>
    /// <param name="phone">What the phone is called.</param>
    /// <param name="plan">The plan it says it has.</param>
    /// <param name="ct">Stops it before anything is kept.</param>
    public async Task ShareFromAsync(string phone, Entitlement plan, CancellationToken ct)
    {
        if (plan.Pro) await _store.KeepSharedAsync(plan with { SharedBy = phone }, ct);
        else if (await _store.SharedAsync(ct) is { } shared && shared.SharedBy == phone) await _store.KeepSharedAsync(null, ct);
    }

    /// <summary>
    /// Takes back the plan a phone shared once this computer has forgotten it: the computer goes back to the free plan
    /// until another paired phone on Pro next tells it of its plan.
    /// </summary>
    /// <param name="phone">What the phone is called, as it named itself when sharing.</param>
    /// <param name="ct">Stops it before anything changes.</param>
    public async Task ForgetPhoneAsync(string phone, CancellationToken ct)
    {
        if (await _store.SharedAsync(ct) is { } shared && shared.SharedBy == phone) await _store.KeepSharedAsync(null, ct);
    }
}
