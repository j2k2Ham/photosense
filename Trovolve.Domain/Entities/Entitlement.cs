namespace Trovolve.Domain.Entities;

/// <summary>Which of Trovolve's plans someone has.</summary>
public enum PlanTier
{
    /// <summary>The free plan: everything but the Pro features, with a limit on removals each day.</summary>
    Free,
    /// <summary>Pro for the days of a free trial, which then renews as the yearly plan unless cancelled.</summary>
    Trial,
    /// <summary>Pro, bought.</summary>
    Pro,
    /// <summary>Pro for up to six people.</summary>
    Family,
}

/// <summary>What was bought: how Pro is paid for.</summary>
public enum PlanKind
{
    /// <summary>Nothing was bought.</summary>
    None,
    /// <summary>Pro, paid every year; the first seven days are a free trial.</summary>
    Yearly,
    /// <summary>Pro, paid every month.</summary>
    Monthly,
    /// <summary>Pro, paid once.</summary>
    Lifetime,
    /// <summary>Pro for up to six people, paid every year.</summary>
    Family,
}

/// <summary>The plan someone has, and until when.</summary>
/// <param name="Tier">Which plan.</param>
/// <param name="Kind">How it is paid for; <see cref="PlanKind.None"/> on the free plan.</param>
/// <param name="Started">When the plan, or its trial, began, when the store says.</param>
/// <param name="Until">When it renews, or a trial ends; null when it never does (the free plan, or Pro paid once).</param>
/// <param name="SharedBy">The phone that told this computer of the plan; null on the device that bought it.</param>
public sealed record Entitlement(PlanTier Tier, PlanKind Kind, DateTimeOffset? Started = null, DateTimeOffset? Until = null, string? SharedBy = null)
{
    /// <summary>The free plan.</summary>
    public static Entitlement Free { get; } = new(PlanTier.Free, PlanKind.None);

    /// <summary>The plan has the Pro features: a trial, Pro or Family.</summary>
    public bool Pro => Tier != PlanTier.Free;

    /// <summary>The plan still stands at <paramref name="now"/>: it is free, or it has no end, or its end has not come.</summary>
    public bool StandsAt(DateTimeOffset now) => Until is not { } end || end > now;
}
