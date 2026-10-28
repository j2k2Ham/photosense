using ImageMagick;
using PhotoSense.Domain.Services;

namespace PhotoSense.Desktop.Imaging;

public sealed class MagickImageAnalyzer : IImageAnalyzer
{
    static MagickImageAnalyzer()
    {
        // Files are processed in parallel, one image per thread.
        ResourceLimits.Thread = 1;
    }

    public Task<ImageAnalysis> AnalyzeAsync(Stream imageStream, CancellationToken ct = default)
        => Task.Run(() => Analyze(imageStream), ct);

    public Task<byte[]> RenderJpegAsync(Stream imageStream, int maxEdge, CancellationToken ct = default)
        => Task.Run(() =>
        {
            using var image = Load(imageStream, maxEdge);
            ToStandardColors(image);
            if (image.Width > maxEdge || image.Height > maxEdge) image.Resize(new MagickGeometry((uint)maxEdge, (uint)maxEdge));
            return ToJpeg(image, 88);
        }, ct);

    private static ImageAnalysis Analyze(Stream imageStream)
    {
        using var image = Load(imageStream, ImageFingerprint.WorkEdge);
        // A JPEG is decoded at reduced size for speed, so its true size comes from the header.
        var sideways = image.Orientation is OrientationType.LeftTop or OrientationType.RightTop or OrientationType.RightBottom or OrientationType.LeftBottom;
        int width = (int)(sideways ? image.BaseHeight : image.BaseWidth), height = (int)(sideways ? image.BaseWidth : image.BaseHeight);
        var format = image.Format.ToString().ToUpperInvariant();
        int? quality = image.Format is MagickFormat.Jpeg or MagickFormat.Jpg && image.Quality > 0 ? (int)image.Quality : null;

        image.AutoOrient();
        if (image.Width > ImageFingerprint.WorkEdge || image.Height > ImageFingerprint.WorkEdge) image.Scale(new MagickGeometry(ImageFingerprint.WorkEdge, ImageFingerprint.WorkEdge));
        ToStandardColors(image);

        using var thumbnail = image.Clone();
        if (thumbnail.Width > ImageFingerprint.ThumbnailEdge || thumbnail.Height > ImageFingerprint.ThumbnailEdge) thumbnail.Resize(new MagickGeometry(ImageFingerprint.ThumbnailEdge, ImageFingerprint.ThumbnailEdge));

        // Stretched to a square on purpose: aspect ratio is checked separately, and a fixed grid lets two signatures be compared cell by cell.
        image.Resize(new MagickGeometry(ImageFingerprint.SignatureEdge, ImageFingerprint.SignatureEdge) { IgnoreAspectRatio = true });
        var signature = Required(image.GetPixels().ToByteArray(PixelMapping.RGB));

        return new ImageAnalysis(width, height, format, quality, ImageFingerprint.PerceptualHash(signature), signature, ToJpeg(thumbnail, 82));
    }

    /// <summary>The pixels read from an image; the decoder hands back none for an image it could not unpack.</summary>
    public static byte[] Required(byte[]? pixels) => pixels ?? throw new InvalidOperationException("Image has no pixel data");

    private static MagickImage Load(Stream imageStream, int neededEdge)
    {
        imageStream.Position = 0;
        var settings = new MagickReadSettings { FrameIndex = 0, FrameCount = 1 };
        // Lets the JPEG decoder skip detail it would only throw away. It picks the nearest reduction it
        // supports, which can land below the size asked for, so ask for double what is needed.
        settings.SetDefine(MagickFormat.Jpeg, "size", $"{neededEdge * 2}x{neededEdge * 2}");
        return new MagickImage(imageStream, settings);
    }

    // Phones tag pictures with a wide-gamut profile; without conversion the same picture saved
    // with and without the profile would compare as different colours.
    private static void ToStandardColors(IMagickImage<byte> image)
    {
        if (image.GetColorProfile() is null) return;
        try { image.TransformColorSpace(ColorProfiles.SRGB); }
        catch (MagickException) { /* unusable profile: keep the pixels as they are */ }
    }

    private static byte[] ToJpeg(IMagickImage<byte> image, uint quality)
    {
        image.Strip();
        image.Format = MagickFormat.Jpeg;
        image.Quality = quality;
        return image.ToByteArray();
    }
}
