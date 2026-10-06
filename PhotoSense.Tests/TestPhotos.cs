using PhotoSense.Domain.Entities;

namespace PhotoSense.Tests;

/// <summary>Builders for photo records and signatures used across tests.</summary>
internal static class TestPhotos
{
    public const int SignatureLength = 32 * 32 * 3;
    public static readonly DateTime Shot = new(2024, 4, 7, 17, 35, 59, 648, DateTimeKind.Unspecified);

    public static Photo Make(
        string name = "IMG_0001.HEIC",
        string? folder = null,
        string? contentHash = null,
        ulong hash = 0xF0F0F0F0F0F0F0F0,
        byte[]? signature = null,
        DateTime? taken = null,
        int width = 4032,
        int height = 3024,
        string format = "HEIC",
        int? quality = null,
        long size = 1_000_000,
        PhotoSet set = PhotoSet.Primary,
        bool kept = false)
        => new()
        {
            SourcePath = Path.Combine(folder ?? Path.Combine(Path.GetTempPath(), "photos"), name),
            FileName = name,
            FileSizeBytes = size,
            ContentHash = contentHash ?? name,
            PerceptualHash = hash.ToString("X16"),
            Signature = signature ?? Signature(),
            Width = width,
            Height = height,
            Format = format,
            EncodedQuality = quality,
            TakenOn = taken,
            Set = set,
            IsKept = kept
        };

    /// <summary>A video record as a scan leaves it: never decoded, and hashed only when its size is shared.</summary>
    public static Photo Video(string name, string? folder = null, string? contentHash = null, string? livePhotoId = null, long size = 5_000_000, PhotoSet set = PhotoSet.Primary)
        => new()
        {
            SourcePath = Path.Combine(folder ?? Path.Combine(Path.GetTempPath(), "photos"), name),
            FileName = name,
            FileSizeBytes = size,
            ContentHash = contentHash,
            Format = Path.GetExtension(name).TrimStart('.').ToUpperInvariant(),
            LivePhotoId = livePhotoId,
            Set = set
        };

    /// <summary>A flat signature that differs from the default one by <paramref name="difference"/> grey levels on average.</summary>
    public static byte[] Signature(double difference = 0)
    {
        var bytes = Enumerable.Repeat((byte)100, SignatureLength).ToArray();
        var changed = (int)Math.Round(SignatureLength * difference / 10);
        for (int i = 0; i < changed; i++) bytes[i] = 110;
        return bytes;
    }
}
