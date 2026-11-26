using Trovolve.Domain.ValueObjects;

namespace Trovolve.Domain.Services;

/// <summary>
/// A picture as a decoder hands it back: the right way up, in standard colours (sRGB), three bytes a pixel (red, green,
/// blue), row by row from the top, at the size it was asked for.
/// </summary>
/// <param name="Width">Its width as decoded, in pixels.</param>
/// <param name="Height">Its height as decoded.</param>
/// <param name="Rgb">Its pixels: <paramref name="Width"/> × <paramref name="Height"/> × 3 bytes.</param>
/// <param name="FullWidth">Its width at full size, the right way up.</param>
/// <param name="FullHeight">Its height at full size, the right way up.</param>
public sealed record DecodedPicture(int Width, int Height, byte[] Rgb, int FullWidth, int FullHeight)
{
    /// <summary>The widest or tallest picture decoded, in pixels: wider than any phone's panorama. A larger one is unreadable.</summary>
    public const int MaxEdge = 32_768;

    /// <summary>
    /// The size every decoder decodes a picture of <paramref name="width"/> × <paramref name="height"/> at, asked for no more
    /// than <paramref name="maxEdge"/> on its long side: that long side, or the picture's own when smaller, and the short
    /// side in proportion, at least one pixel. Every decoder uses this one rule, so that they all measure the same pixels.
    /// </summary>
    public static (int Width, int Height) Fit(int width, int height, int maxEdge)
    {
        var longest = Math.Max(width, height);
        if (longest <= maxEdge) return (width, height);
        var scale = (double)maxEdge / longest;
        return width >= height
            ? (maxEdge, Math.Max(1, (int)Math.Round(height * scale)))
            : (Math.Max(1, (int)Math.Round(width * scale)), maxEdge);
    }
}

/// <summary>
/// Decodes pictures with what the system itself has: Windows Imaging Component on Windows, ImageIO on a Mac. A decoder
/// only turns a picture's bytes into pixels; measuring them, and writing previews, is the same for every decoder
/// (<c>PictureAnalyzer</c>), so that every computer measures a picture alike.
/// </summary>
public interface IPictureDecoder
{
    /// <summary>
    /// Decodes a picture the right way up, in standard colours, at <see cref="DecodedPicture.Fit"/> for
    /// <paramref name="maxEdge"/>.
    /// </summary>
    /// <param name="picture">The picture's bytes.</param>
    /// <param name="format">What its first bytes say it is (<see cref="PictureSignature"/>); it is read as that and nothing else.</param>
    /// <param name="maxEdge">The longest side wanted, in pixels.</param>
    /// <param name="ct">Stops it before it starts.</param>
    /// <exception cref="UnreadableImageException">
    /// The picture could not be decoded, is larger than <see cref="DecodedPicture.MaxEdge"/>, or the system lacks what
    /// it needs to read that format, which the message says how to get.
    /// </exception>
    Task<DecodedPicture> DecodeAsync(byte[] picture, PictureFormat format, int maxEdge, CancellationToken ct);
}
