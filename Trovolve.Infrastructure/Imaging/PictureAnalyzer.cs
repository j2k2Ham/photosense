using Trovolve.Domain.Services;
using Trovolve.Domain.ValueObjects;

namespace Trovolve.Infrastructure.Imaging;

/// <summary>
/// Pictures decoded by the system (<see cref="IPictureDecoder"/>: Windows Imaging Component, or ImageIO on a Mac) and
/// measured here, the same way whichever system decoded them: resized (<see cref="Pixels"/>), fingerprinted
/// (<see cref="ImageFingerprint"/>), and written as previews (<see cref="JpegWriter"/>).
/// </summary>
/// <param name="decoder">The system's decoder.</param>
public sealed class PictureAnalyzer(IPictureDecoder decoder) : IImageAnalyzer
{
    /// <summary>The quality a preview is written at.</summary>
    public const int PreviewQuality = 82;

    /// <summary>The quality a picture a browser cannot show is written at, to be looked at whole.</summary>
    public const int ViewQuality = 88;

    /// <inheritdoc />
    public async Task<ImageAnalysis> AnalyzeAsync(Stream imageStream, CancellationToken ct)
    {
        var (bytes, format) = await ReadAsync(imageStream, ct);
        // Decoded once, at the size sharpness is measured at (ImageFingerprint.SharpnessEdge is WorkEdge); the preview and
        // the signature are made from it.
        var work = await decoder.DecodeAsync(bytes, format, ImageFingerprint.WorkEdge, ct);
        var (across, down) = DecodedPicture.Fit(work.Width, work.Height, ImageFingerprint.ThumbnailEdge);
        var thumbnail = JpegWriter.Write(Pixels.Resize(work.Rgb, work.Width, work.Height, across, down), across, down, PreviewQuality);
        var sharpness = ImageFingerprint.Sharpness(work.Rgb, work.Width, work.Height);
        // Stretched to a square on purpose: aspect ratio is checked separately, and a fixed grid lets two signatures be compared cell by cell.
        var signature = Pixels.Resize(work.Rgb, work.Width, work.Height, ImageFingerprint.SignatureEdge, ImageFingerprint.SignatureEdge);
        return new ImageAnalysis(work.FullWidth, work.FullHeight, format.ToString().ToUpperInvariant(), format == PictureFormat.Jpeg ? JpegQuality.Estimate(bytes) : null,
            ImageFingerprint.PerceptualHash(signature), signature, thumbnail, sharpness);
    }

    /// <inheritdoc />
    public async Task<byte[]> RenderJpegAsync(Stream imageStream, int maxEdge, CancellationToken ct)
    {
        var (bytes, format) = await ReadAsync(imageStream, ct);
        var picture = await decoder.DecodeAsync(bytes, format, maxEdge, ct);
        return JpegWriter.Write(picture.Rgb, picture.Width, picture.Height, ViewQuality);
    }

    // The picture's bytes, and what its first bytes say it is: it is decoded as that and nothing else, so that a script,
    // a vector drawing or PostScript named .jpg is never handed to a reader of theirs.
    private static async Task<(byte[] Bytes, PictureFormat Format)> ReadAsync(Stream imageStream, CancellationToken ct)
    {
        imageStream.Position = 0;
        var copy = new MemoryStream();
        await imageStream.CopyToAsync(copy, ct);
        var bytes = copy.ToArray();
        // An empty file is a transfer that never finished.
        if (bytes.Length == 0) throw new UnreadableImageException("The file is empty.");
        var format = PictureSignature.Of(bytes.AsSpan(0, Math.Min(bytes.Length, PictureSignature.Needed)))
            ?? throw new UnreadableImageException("It is not a picture Trovolve reads: JPEG, PNG, GIF, BMP, TIFF, WebP or HEIC.");
        return (bytes, format);
    }
}
