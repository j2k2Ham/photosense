using System.Globalization;

namespace Trovolve.Domain.ValueObjects;

/// <summary>An amount of bytes as Trovolve writes it everywhere, the page included: "2.47 GB", "212.0 MB", "38 KB".</summary>
public static class Sizes
{
    /// <summary>
    /// The amount as the page's <c>formatBytes</c> writes it, so that a number a notification gives is the one the
    /// screen it opens shows: gigabytes to two places, megabytes to one, kilobytes whole and never fewer than one.
    /// </summary>
    public static string Of(long bytes)
    {
        const double Mega = 1024 * 1024, Giga = Mega * 1024;
        if (bytes >= Giga) return (bytes / Giga).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
        if (bytes >= Mega) return (bytes / Mega).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        return Math.Max(1, Math.Round(bytes / 1024d, MidpointRounding.AwayFromZero)).ToString("0", CultureInfo.InvariantCulture) + " KB";
    }
}
