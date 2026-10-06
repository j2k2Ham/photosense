using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Entities;

namespace PhotoSense.Application.Scanning.Services;

/// <summary>
/// Decides which of several copies of one picture is the best to keep, and says why.
/// Copies of one shot do not differ in focus or exposure, only in how much of the original survived
/// resizing and re-encoding, so that is what is ranked.
/// </summary>
public sealed class PhotoRanking
{
    private readonly FormatPreference _preference;

    public PhotoRanking(FormatPreference preference = FormatPreference.WidelyCompatible)
    {
        _preference = preference;
        BestFirst = Comparer<Photo>.Create(Compare);
    }

    public IComparer<Photo> BestFirst { get; }

    private int Compare(Photo a, Photo b)
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
    public string WhyKept(Photo keeper, Photo other)
    {
        if (keeper.PixelCount != other.PixelCount)
            return $"Higher resolution: {keeper.Width}×{keeper.Height} vs {other.Width}×{other.Height}";
        if (FormatRank(keeper) != FormatRank(other))
        {
            if (FormatRank(keeper) == Lossless) return $"Lossless format: {keeper.Format} rather than {other.Format}";
            return _preference == FormatPreference.CameraOriginal
                ? $"Camera original: {keeper.Format} rather than a {other.Format} conversion"
                : $"Opens everywhere: {keeper.Format} rather than {other.Format}";
        }
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

    private const int Lossless = 3;

    // Between copies of one shot at the same resolution a lossless file comes first. After that it is a
    // choice. A camera writes one format, so when a HEIC and a JPEG show the same shot the HEIC is what the
    // phone recorded and the JPEG is a conversion of it: the HEIC is the better and smaller file, the JPEG
    // the one that opens on any device.
    private int FormatRank(Photo p)
    {
        var format = p.Format?.ToUpperInvariant();
        if (format is "PNG" or "TIFF" or "TIF" or "BMP") return Lossless;
        var preferred = _preference == FormatPreference.CameraOriginal ? format is "HEIC" or "HEIF" : format is "JPEG" or "JPG";
        return preferred ? 2 : 1;
    }

    private static int SetRank(Photo p) => p.Set switch { PhotoSet.Primary => 0, PhotoSet.Secondary => 1, _ => 2 };

    private static string Megabytes(Photo p) => (p.FileSizeBytes / 1048576d).ToString("0.0");
}
