using System.Text.Json.Serialization;

namespace PhotoSense.Contracts.Plans;

/// <summary>The plan the person has, how many removals are left today, and the plans for sale.</summary>
public sealed class PlanDto
{
    /// <summary>Which plan: "free", "trial", "pro" or "family".</summary>
    [JsonPropertyName("tier")] public string Tier { get; init; } = "free";
    /// <summary>How it is paid for: "yearly", "monthly", "lifetime" or "family"; null on the free plan.</summary>
    [JsonPropertyName("kind")] public string? Kind { get; init; }
    /// <summary>When the plan, or its trial, began (ISO 8601), when known.</summary>
    [JsonPropertyName("started")] public string? Started { get; init; }
    /// <summary>When it renews or a trial ends (ISO 8601); null when it never does.</summary>
    [JsonPropertyName("until")] public string? Until { get; init; }
    /// <summary>The phone that told this computer of the plan; null on the device that bought it, and on the free plan.</summary>
    [JsonPropertyName("sharedBy")] public string? SharedBy { get; init; }
    /// <summary>The store plans are bought in: "App Store" or "Google Play"; null on a computer, which has none.</summary>
    [JsonPropertyName("store")] public string? Store { get; init; }
    /// <summary>How many photos and videos the free plan removes each day.</summary>
    [JsonPropertyName("removalsPerDay")] public int RemovalsPerDay { get; init; }
    /// <summary>How many were removed today, while on the free plan.</summary>
    [JsonPropertyName("removedToday")] public int RemovedToday { get; init; }
    /// <summary>How many more can be removed today; null when the plan has no limit.</summary>
    [JsonPropertyName("leftToday")] public int? LeftToday { get; init; }
    /// <summary>The plans for sale, with the store's prices; empty on a computer, or when the store has not answered.</summary>
    [JsonPropertyName("offers")] public IReadOnlyList<PlanOfferDto> Offers { get; init; } = [];
}

/// <summary>A plan for sale, priced as the store says.</summary>
public sealed class PlanOfferDto
{
    /// <summary>The plan: "yearly", "monthly", "lifetime" or "family".</summary>
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    /// <summary>The price as the store writes it, such as "$19.99".</summary>
    [JsonPropertyName("price")] public string Price { get; init; } = string.Empty;
    /// <summary>The price as a number, in <see cref="Currency"/>.</summary>
    [JsonPropertyName("amount")] public decimal Amount { get; init; }
    /// <summary>The currency's ISO 4217 code, such as "USD".</summary>
    [JsonPropertyName("currency")] public string Currency { get; init; } = string.Empty;
}

/// <summary>A plan to buy.</summary>
public sealed class PlanBuyRequest
{
    /// <summary>The plan: "yearly", "monthly", "lifetime" or "family".</summary>
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
}

/// <summary>How buying a plan ended, and the plan then.</summary>
public sealed class PlanBuyResultDto
{
    /// <summary>"bought", "cancelled" (the store's sheet was put away) or "pending" (waiting for a parent to agree, or a payment to clear).</summary>
    [JsonPropertyName("outcome")] public string Outcome { get; init; } = string.Empty;
    /// <summary>The plan once the store has answered.</summary>
    [JsonPropertyName("plan")] public PlanDto Plan { get; init; } = new();
}
