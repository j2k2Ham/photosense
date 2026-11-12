namespace PhotoSense.Domain.Services;

/// <summary>What one decode of an image yields for duplicate detection and review.</summary>
/// <param name="Width">Width in pixels as displayed (orientation applied).</param>
/// <param name="Height">Height in pixels as displayed (orientation applied).</param>
/// <param name="Format">The format as the decoder names it ("JPEG", "HEIC").</param>
/// <param name="EncodedQuality">Encoder quality (1-100) when the format records one (JPEG).</param>
/// <param name="PerceptualHash">The 64-bit hash of <see cref="ImageFingerprint.PerceptualHash"/>.</param>
/// <param name="Signature">The picture reduced to a grid of <see cref="ImageFingerprint.SignatureEdge"/> by <see cref="ImageFingerprint.SignatureEdge"/> colours.</param>
/// <param name="ThumbnailJpeg">A JPEG preview no larger than <see cref="ImageFingerprint.ThumbnailEdge"/> on its long side.</param>
public sealed record ImageAnalysis(
    int Width,
    int Height,
    string Format,
    int? EncodedQuality,
    ulong PerceptualHash,
    byte[] Signature,
    byte[] ThumbnailJpeg);

/// <summary>Decodes pictures: to compare them, and to show them where a browser cannot.</summary>
/// <remarks>
/// Each decoder throws its own kinds of error. Every implementation turns a picture it cannot decode into an
/// <see cref="UnreadableImageException"/>, so that callers catch that alone and never the decoder's own types.
/// </remarks>
public interface IImageAnalyzer
{
    /// <summary>Decodes the image once and measures it.</summary>
    /// <exception cref="UnreadableImageException">The image could not be decoded.</exception>
    Task<ImageAnalysis> AnalyzeAsync(Stream imageStream, CancellationToken ct);

    /// <summary>Renders the image as an upright JPEG no larger than <paramref name="maxEdge"/> on its long side.</summary>
    /// <param name="imageStream">The image's bytes.</param>
    /// <param name="maxEdge">The longest side, in pixels.</param>
    /// <param name="ct">Stops the work.</param>
    /// <exception cref="UnreadableImageException">The image could not be decoded.</exception>
    Task<byte[]> RenderJpegAsync(Stream imageStream, int maxEdge, CancellationToken ct);
}
