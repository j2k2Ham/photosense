using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Services;

namespace PhotoSense.Domain.ValueObjects;

/// <summary>What PhotoSense sells in the App Store and Google Play, by the stores' ids, and the plan bought with each.</summary>
public static class PlanProducts
{
    /// <summary>Pro, paid every year, with a seven-day free trial.</summary>
    public const string Yearly = "photosense.pro.yearly";

    /// <summary>Pro, paid every month.</summary>
    public const string Monthly = "photosense.pro.monthly";

    /// <summary>Pro, paid once.</summary>
    public const string Lifetime = "photosense.pro.lifetime";

    /// <summary>Pro for up to six people, paid every year.</summary>
    public const string Family = "photosense.family.yearly";

    /// <summary>Every one of them, in the order they are offered.</summary>
    public static IReadOnlyList<string> All { get; } = [Yearly, Monthly, Lifetime, Family];

    /// <summary>The store's id for a kind of plan; null for <see cref="PlanKind.None"/>, which is not sold.</summary>
    public static string? IdOf(PlanKind kind) => kind switch
    {
        PlanKind.Yearly => Yearly,
        PlanKind.Monthly => Monthly,
        PlanKind.Lifetime => Lifetime,
        PlanKind.Family => Family,
        _ => null,
    };

    /// <summary>The kind of plan a store's id buys; <see cref="PlanKind.None"/> for an id that is not PhotoSense's.</summary>
    public static PlanKind KindOf(string productId) => productId switch
    {
        Yearly => PlanKind.Yearly,
        Monthly => PlanKind.Monthly,
        Lifetime => PlanKind.Lifetime,
        Family => PlanKind.Family,
        _ => PlanKind.None,
    };

    /// <summary>
    /// The plan someone has from what they bought: the widest of those still standing at <paramref name="now"/> (Family,
    /// then Pro paid once, yearly, monthly), or the free plan when none is.
    /// </summary>
    public static Entitlement PlanFrom(IEnumerable<StorePurchase> purchases, DateTimeOffset now)
    {
        var best = purchases
            .Select(p => (Purchase: p, Kind: KindOf(p.ProductId)))
            .Where(p => p.Kind != PlanKind.None && (p.Purchase.Expires is null || p.Purchase.Expires > now))
            .OrderByDescending(p => Rank(p.Kind))
            .FirstOrDefault();
        if (best.Purchase is null) return Entitlement.Free;
        var tier = best.Kind == PlanKind.Family ? PlanTier.Family : best.Purchase.Trial ? PlanTier.Trial : PlanTier.Pro;
        return new Entitlement(tier, best.Kind, best.Purchase.Bought, best.Purchase.Expires);
    }

    // Family covers the most, then Pro that never runs out, then the longest subscription.
    private static int Rank(PlanKind kind) => kind switch
    {
        PlanKind.Family => 4,
        PlanKind.Lifetime => 3,
        PlanKind.Yearly => 2,
        _ => 1,
    };
}
