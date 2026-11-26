using Trovolve.Domain.ValueObjects;

namespace Trovolve.Domain.Services;

/// <summary>
/// A picture as a decoder hands it back: at full size, the right way up, in standard colours (sRGB), three bytes a pixel
/// (red, green, blue), row by row from the top.
/// </summary>
/// <param name="Width">Its width, the right way up, in pixels.</param>
/// <param name="Height">Its height, the right way up.</param>
/// <param name="Rgb">Its pixels: <paramref name="Width"/> × <paramref name="Height"/> × 3 bytes.</param>
public sealed record DecodedPicture(int Width, int Height, byte[] Rgb)
{
    /// <summary>The widest or tallest picture decoded, in pixels: wider than any phone's panorama. A larger one is unreadable.</summary>
    public const int MaxEdge = 32_768;

    /// <summary>
    /// The size a picture of <paramref name="width"/> × <paramref name="height"/> is reduced to, asked for no more than
    /// <paramref name="maxEdge"/> on its long side: that long side, or the picture's own when smaller, and the short side
    /// in proportion, at least one pixel.
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
/// only turns a picture's bytes into pixels, at full size; reducing them, measuring them and writing previews is the same
/// for every decoder (<c>PictureAnalyzer</c>), so that every computer measures a picture alike. A decoder that reduced a
/// picture itself would set it apart from the same picture decoded elsewhere.
/// </summary>
public interface IPictureDecoder
{
    /// <summary>
    /// Decodes a picture at full size, the right way up, in standard colours.
    /// </summary>
    /// <param name="picture">The picture's bytes.</param>
    /// <param name="format">What its first bytes say it is (<see cref="PictureSignature"/>); it is read as that and nothing else.</param>
    /// <param name="ct">Stops it before it starts.</param>
    /// <exception cref="UnreadableImageException">
    /// The picture could not be decoded, is larger than <see cref="DecodedPicture.MaxEdge"/>, or the system lacks what
    /// it needs to read that format, which the message says how to get.
    /// </exception>
    Task<DecodedPicture> DecodeAsync(byte[] picture, PictureFormat format, CancellationToken ct);
}
