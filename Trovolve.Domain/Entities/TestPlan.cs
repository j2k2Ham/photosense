namespace Trovolve.Domain.Entities;

/// <summary>
/// A plan chosen in a development build to try Trovolve as someone on that plan would see it, in place of what was
/// bought (Jamie, 2026-11-24). A build for others has none: <see cref="DevMode"/> is off there.
/// </summary>
public enum TestPlan
{
    /// <summary>No test plan: the plan is what was bought, or what a paired phone shared.</summary>
    AsBought,
    /// <summary>The free plan, with its limit on removals each day.</summary>
    Free,
    /// <summary>Pro in its free trial, five days from its end.</summary>
    Trial,
    /// <summary>Pro, paid every month.</summary>
    Monthly,
    /// <summary>Pro, paid every year.</summary>
    Yearly,
    /// <summary>Pro, paid once.</summary>
    Lifetime,
}

/// <summary>Whether this is a development build, in which a plan can be chosen for testing.</summary>
/// <param name="On">A development build: plans for testing are offered. Off in every build made for others.</param>
public sealed record DevMode(bool On)
{
    /// <summary>A build for others: no plans for testing.</summary>
    public static DevMode Off { get; } = new(false);
}

/// <summary>The plan each <see cref="TestPlan"/> stands for, dated as a real one would be.</summary>
public static class TestPlanEntitlements
{
    /// <summary>The plan <paramref name="plan"/> stands for at <paramref name="now"/>; null for <see cref="TestPlan.AsBought"/>.</summary>
    /// <param name="plan">The plan chosen for testing.</param>
    /// <param name="now">When it is asked for: a trial ends, and a subscription renews, from then.</param>
    public static Entitlement? At(TestPlan plan, DateTimeOffset now) => plan switch
    {
        TestPlan.Free => Entitlement.Free,
        TestPlan.Trial => new Entitlement(PlanTier.Trial, PlanKind.Yearly, now.AddDays(-2), now.AddDays(5)),
        TestPlan.Monthly => new Entitlement(PlanTier.Pro, PlanKind.Monthly, now.AddDays(-10), now.AddDays(20)),
        TestPlan.Yearly => new Entitlement(PlanTier.Pro, PlanKind.Yearly, now.AddDays(-30), now.AddDays(335)),
        TestPlan.Lifetime => new Entitlement(PlanTier.Pro, PlanKind.Lifetime, now.AddDays(-30)),
        _ => null,
    };
}
