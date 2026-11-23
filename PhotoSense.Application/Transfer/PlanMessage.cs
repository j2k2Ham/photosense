using System.Globalization;
using System.Text.Json.Nodes;
using PhotoSense.Domain.Entities;

namespace PhotoSense.Application.Transfer;

/// <summary>
/// The plan a phone tells a computer of, inside its offer, so that one purchase covers the computer too. A computer
/// that does not know the field passes over it; a phone that does not send it leaves the computer's plan as it was.
/// </summary>
public static class PlanMessage
{
    /// <summary>The plan as the offer carries it: its tier, its kind, and when it began and ends.</summary>
    public static JsonObject Write(Entitlement plan) => new()
    {
        ["tier"] = plan.Tier.ToString().ToLowerInvariant(),
        ["kind"] = plan.Kind.ToString().ToLowerInvariant(),
        ["started"] = plan.Started?.ToString("O", CultureInfo.InvariantCulture),
        ["until"] = plan.Until?.ToString("O", CultureInfo.InvariantCulture),
    };

    /// <summary>The plan an offer carries, or null when it carries none, or none that can be read.</summary>
    public static Entitlement? Read(JsonNode? node)
    {
        if (node is not JsonObject plan || Text(plan, "tier") is not { } tierText || Text(plan, "kind") is not { } kindText) return null;
        // Only the names written above are a plan: a number, or names joined by commas, would parse too.
        if (!Named(tierText, out PlanTier tier) || !Named(kindText, out PlanKind kind)) return null;
        if (!Time(plan, "started", out var started) || !Time(plan, "until", out var until)) return null;
        return new Entitlement(tier, kind, started, until);
    }

    private static bool Named<T>(string text, out T value) where T : struct, Enum
        => Enum.TryParse(text, ignoreCase: true, out value) && string.Equals(value.ToString(), text, StringComparison.OrdinalIgnoreCase);

    private static string? Text(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    // An absent time is none; one that is there must be a time.
    private static bool Time(JsonObject o, string key, out DateTimeOffset? at)
    {
        at = null;
        if (o[key] is null) return true;
        if (Text(o, key) is not { } text || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)) return false;
        at = parsed;
        return true;
    }
}
