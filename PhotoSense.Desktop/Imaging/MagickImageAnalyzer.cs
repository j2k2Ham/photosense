using System.Diagnostics.CodeAnalysis;
using ImageMagick;
using PhotoSense.Domain.Services;

namespace PhotoSense.Desktop.Imaging;

/// <summary>Decodes pictures with Magick.NET on a computer, HEIC among them, and measures them the way every decoder does.</summary>
public sealed class MagickImageAnalyzer : IImageAnalyzer
{
    static MagickImageAnalyzer()
    {
        // Files are processed in parallel, one image per thread.
        ResourceLimits.Thread = 1;
    }

    /// <inheritdoc />
    public Task<ImageAnalysis> AnalyzeAsync(Stream imageStream, CancellationToken ct)
        => Task.Run(() => Decoded(() => Analyze(imageStream)), ct);

    /// <inheritdoc />
    public Task<byte[]> RenderJpegAsync(Stream imageStream, int maxEdge, CancellationToken ct)
        => Task.Run(() => Decoded(() =>
        {
            using var image = Load(imageStream, maxEdge);
            _ = ToStandardColors(image);
            if (image.Width > maxEdge || image.Height > maxEdge) image.Resize(new MagickGeometry((uint)maxEdge, (uint)maxEdge));
            return ToJpeg(image, 88);
        }), ct);

    /// <summary>
    /// Decodes with Magick, turning the errors of its decoder into the one PhotoSense knows a picture it cannot decode by,
    /// with Magick's own words for why.
    /// </summary>
    private static T Decoded<T>(Func<T> decode)
    {
        try { return decode(); }
        catch (MagickException ex) { throw new UnreadableImageException(ex.Message, ex); }
    }

    private static ImageAnalysis Analyze(Stream imageStream)
    {
        using var image = Load(imageStream, ImageFingerprint.WorkEdge);
        // A JPEG is decoded at reduced size for speed, so its true size comes from the header.
        var sideways = image.Orientation is Orientation.LeftTop or Orientation.RightTop or Orientation.RightBottom or Orientation.LeftBottom;
        int width = (int)(sideways ? image.BaseHeight : image.BaseWidth), height = (int)(sideways ? image.BaseWidth : image.BaseHeight);
        var format = image.Format.ToString().ToUpperInvariant();
        int? quality = image.Format is MagickFormat.Jpeg or MagickFormat.Jpg && image.Quality > 0 ? (int)image.Quality : null;

        image.AutoOrient();
        if (image.Width > ImageFingerprint.WorkEdge || image.Height > ImageFingerprint.WorkEdge) image.Scale(new MagickGeometry(ImageFingerprint.WorkEdge, ImageFingerprint.WorkEdge));
        _ = ToStandardColors(image);

        using var thumbnail = image.Clone();
        if (thumbnail.Width > ImageFingerprint.ThumbnailEdge || thumbnail.Height > ImageFingerprint.ThumbnailEdge) thumbnail.Resize(new MagickGeometry(ImageFingerprint.ThumbnailEdge, ImageFingerprint.ThumbnailEdge));

        // Stretched to a square on purpose: aspect ratio is checked separately, and a fixed grid lets two signatures be compared cell by cell.
        image.Resize(new MagickGeometry(ImageFingerprint.SignatureEdge, ImageFingerprint.SignatureEdge) { IgnoreAspectRatio = true });
        var signature = Required(image.GetPixels().ToByteArray(PixelMapping.RGB));

        return new ImageAnalysis(width, height, format, quality, ImageFingerprint.PerceptualHash(signature), signature, ToJpeg(thumbnail, 82));
    }

    // The pixels read from an image. Magick.NET says it may hand back none, for an image it could not unpack; no
    // picture that decodes has been found to do so, so no test can reach the refusal.
    [ExcludeFromCodeCoverage(Justification = "Magick.NET's guard against an image it could not unpack, which no decodable picture reaches.")]
    private static byte[] Required(byte[]? pixels) => pixels ?? throw new UnreadableImageException("Image has no pixel data");

    private static MagickImage Load(Stream imageStream, int neededEdge)
    {
        // An empty file is a transfer that never finished; Magick would refuse it as a mistake of the caller's.
        if (imageStream.Length == 0) throw new UnreadableImageException("The file is empty.");
        imageStream.Position = 0;
        var settings = new MagickReadSettings { FrameIndex = 0, FrameCount = 1 };
        // Lets the JPEG decoder skip detail it would only throw away. It picks the nearest reduction it
        // supports, which can land below the size asked for, so ask for double what is needed.
        settings.SetDefine(MagickFormat.Jpeg, "size", $"{neededEdge * 2}x{neededEdge * 2}");
        return new MagickImage(imageStream, settings);
    }

    /// <summary>
    /// Converts the picture's colours to standard ones (sRGB). Phones tag pictures with a wide-gamut profile; without
    /// conversion the same picture saved with and without the profile would compare as different colours.
    /// </summary>
    /// <returns>Whether they were converted; false when the picture has no profile, or one that cannot be used, and the pixels are kept as they are.</returns>
    private static bool ToStandardColors(IMagickImage<byte> image)
    {
        if (image.GetColorProfile() is null) return false;
        try
        {
            image.TransformColorSpace(ColorProfiles.SRGB);
            return true;
        }
        catch (MagickException)
        {
            return false;
        }
    }

    private static byte[] ToJpeg(IMagickImage<byte> image, uint quality)
    {
        image.Strip();
        image.Format = MagickFormat.Jpeg;
        image.Quality = quality;
        return image.ToByteArray();
    }
}
