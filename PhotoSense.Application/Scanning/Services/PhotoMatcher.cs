using System.Numerics;
using PhotoSense.Domain.DTOs;
using PhotoSense.Domain.Entities;

namespace PhotoSense.Application.Scanning.Services;

/// <summary>
/// Decides whether two photos that are not identical files are the same shot, merely alike, or unrelated.
/// A perceptual hash only nominates a pair; the pair is then checked directly.
/// </summary>
public static class PhotoMatcher
{
    // The limits below were measured on a 5,578-photo iPhone library (2,782 HEIC, 2,736 JPEG):
    //   - 255 pairs known to be one shot saved twice (HEIC original + JPEG conversion) differed by
    //     0-2 hash bits and at most 0.13 grey levels; a resized, re-compressed copy by 0.31.
    //   - Burst frames (different shots tens of milliseconds apart) often had identical hashes and
    //     differed by as little as 0.22 grey levels, so pixels alone cannot tell a burst frame from a copy.
    //     Capture time can: frames were at least 44 ms apart, copies of one shot at most 1 ms.

    /// <summary>Perceptual hashes further apart than this (of 64 bits) are not compared at all.</summary>
    public const int CandidateBits = 8;

    /// <summary>Largest relative difference in width:height for one picture to be a copy of the other.</summary>
    public const double AspectTolerance = 0.02;

    /// <summary>Signature difference (grey levels) up to which two photos are shown together as look-alikes.</summary>
    public const double SimilarDifference = 2.0;

    /// <summary>Signature difference up to which two photos taken at the same instant are the same picture.</summary>
    public const double SameDifference = 0.5;

    /// <summary>
    /// Signature difference up to which two photos are the same picture when neither records a capture time.
    /// Below the closest pair of burst frames measured, because the pixels are then the only evidence.
    /// </summary>
    public const double UnconfirmedSameDifference = 0.2;

    public sealed record Verdict(MatchKind Kind, int HashDistance, double Difference);

    public static int HashDistance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);

    public static bool TryParseHash(string? hex, out ulong hash)
        => ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out hash)
           && hex!.Length == 16;

    /// <summary>Null when the two photos are unrelated or cannot be compared.</summary>
    public static Verdict? Compare(Photo a, Photo b)
    {
        if (a.Signature is null || b.Signature is null || a.Signature.Length != b.Signature.Length) return null;
        if (!TryParseHash(a.PerceptualHash, out var ha) || !TryParseHash(b.PerceptualHash, out var hb)) return null;
        var bits = HashDistance(ha, hb);
        if (bits > CandidateBits) return null;
        // A different shape means a crop or another frame, not a copy.
        if (a.Width <= 0 || a.Height <= 0 || b.Width <= 0 || b.Height <= 0) return null;
        double ra = (double)a.Width / a.Height, rb = (double)b.Width / b.Height;
        if (Math.Abs(ra - rb) / Math.Max(ra, rb) > AspectTolerance) return null;

        var difference = SignatureDifference(a.Signature, b.Signature);
        if (difference > SimilarDifference) return null;

        return new Verdict(IsSameShot(a, b, difference) ? MatchKind.SamePicture : MatchKind.Similar, bits, difference);
    }

    private static bool IsSameShot(Photo a, Photo b, double difference)
    {
        // An edited version is the owner's work, not a redundant copy, however alike it looks.
        if (PhotoNaming.IsEditedRender(a.FileName) != PhotoNaming.IsEditedRender(b.FileName)) return false;

        // Burst frames look the same at this scale but were taken at different moments.
        if (a.TakenOn is { } ta && b.TakenOn is { } tb)
            return (ta - tb).Duration() <= CaptureTolerance(ta, tb) && difference <= SameDifference;

        // One copy lost its capture time, as happens when a picture is shared or re-saved: nothing contradicts the match.
        if (a.TakenOn.HasValue != b.TakenOn.HasValue) return difference <= SameDifference;

        return difference <= UnconfirmedSameDifference;
    }

    // Sub-second times are compared closely (an edit can shift the stamp by a millisecond; burst frames
    // are tens of milliseconds apart). A copy that lost its sub-second part is allowed the whole second.
    private static TimeSpan CaptureTolerance(DateTime a, DateTime b)
        => a.Millisecond != 0 && b.Millisecond != 0 ? TimeSpan.FromMilliseconds(10) : TimeSpan.FromSeconds(1);

    /// <summary>Mean absolute difference between two signatures, in grey levels.</summary>
    public static double SignatureDifference(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        long total = 0;
        for (int i = 0; i < a.Length; i++) total += Math.Abs(a[i] - b[i]);
        return (double)total / a.Length;
    }
}
