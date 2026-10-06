namespace PhotoSense.Domain.Services;

/// <summary>What one decode of an image yields for duplicate detection and review.</summary>
public sealed record ImageAnalysis(
    int Width,
    int Height,
    string Format,
    int? EncodedQuality,
    ulong PerceptualHash,
    byte[] Signature,
    byte[] ThumbnailJpeg);

public interface IImageAnalyzer
{
    /// <summary>Decodes the image once and measures it. Throws when the image cannot be decoded.</summary>
    Task<ImageAnalysis> AnalyzeAsync(Stream imageStream, CancellationToken ct = default);

    /// <summary>Renders the image as an upright JPEG no larger than <paramref name="maxEdge"/> on its long side.</summary>
    Task<byte[]> RenderJpegAsync(Stream imageStream, int maxEdge, CancellationToken ct = default);
}
