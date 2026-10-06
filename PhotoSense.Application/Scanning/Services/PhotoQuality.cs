using PhotoSense.Domain.Entities;

namespace PhotoSense.Application.Scanning.Services;

/// <summary>
/// Decides which of several copies of one picture is the best to keep, and says why.
/// Copies of one shot do not differ in focus or exposure, only in how much of the original survived
/// resizing and re-encoding, so that is what is ranked.
/// </summary>
public static class PhotoQuality
{
    public static IComparer<Photo> BestFirst { get; } = Comparer<Photo>.Create(Compare);

    private static int Compare(Photo a, Photo b)
    {
        int c;
        if ((c = b.PixelCount.CompareTo(a.PixelCount)) != 0) return c;
        if ((c = FormatRank(b).CompareTo(FormatRank(a))) != 0) return c;
        if ((c = b.TakenOn.HasValue.CompareTo(a.TakenOn.HasValue)) != 0) return c;
        // A missing quality counts as lowest, so that any three photos rank consistently.
        if ((c = (b.EncodedQuality ?? 0).CompareTo(a.EncodedQuality ?? 0)) != 0) return c;
        if ((c = b.FileSizeBytes.CompareTo(a.FileSizeBytes)) != 0) return c;
        // From here the copies are equally good; prefer the primary folder and the plainest name.
        if ((c = SetRank(a).CompareTo(SetRank(b))) != 0) return c;
        if ((c = a.FileName.Length.CompareTo(b.FileName.Length)) != 0) return c;
        return string.CompareOrdinal(a.SourcePath, b.SourcePath);
    }

    /// <summary>Why <paramref name="keeper"/> ranks above <paramref name="other"/>, in words for the person reviewing.</summary>
    public static string WhyKept(Photo keeper, Photo other)
    {
        if (keeper.PixelCount != other.PixelCount)
            return $"Higher resolution: {keeper.Width}×{keeper.Height} vs {other.Width}×{other.Height}";
        if (FormatRank(keeper) != FormatRank(other))
            return FormatRank(keeper) == 3
                ? $"Lossless format: {keeper.Format} rather than {other.Format}"
                : $"Opens everywhere: {keeper.Format} rather than {other.Format}";
        if (keeper.TakenOn.HasValue != other.TakenOn.HasValue) return "Still has its capture date and details";
        if ((keeper.EncodedQuality ?? 0) != (other.EncodedQuality ?? 0))
            return keeper.EncodedQuality is { } qk && other.EncodedQuality is { } qo
                ? $"Less compressed: JPEG quality {qk} vs {qo}"
                : "Better-preserved copy at the same resolution";
        if (keeper.FileSizeBytes != other.FileSizeBytes)
            return $"Larger file: {Megabytes(keeper)} MB vs {Megabytes(other)} MB";
        if (SetRank(keeper) != SetRank(other)) return "Same quality; this one is in the primary folder";
        return "Same quality; kept the one with the plainer name";
    }

    // Between copies of one shot at the same resolution: a lossless file first, then the format that opens
    // everywhere (JPEG) ahead of ones that need extra support (HEIC, WebP).
    private static int FormatRank(Photo p) => p.Format?.ToUpperInvariant() switch
    {
        "PNG" or "TIFF" or "TIF" or "BMP" => 3,
        "JPEG" or "JPG" => 2,
        _ => 1
    };

    private static int SetRank(Photo p) => p.Set switch { PhotoSet.Primary => 0, PhotoSet.Secondary => 1, _ => 2 };

    private static string Megabytes(Photo p) => (p.FileSizeBytes / 1048576d).ToString("0.0");
}
