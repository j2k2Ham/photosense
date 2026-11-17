using CoreGraphics;
using CoreImage;
using ImageIO;
using PhotoSense.Domain.Services;

namespace PhotoSense.iOS;

/// <summary>
/// Pictures decoded by the phone itself, which reads HEIC, JPEG and the rest, and measured as on a computer.
/// Magick.NET, which a computer uses, has no build for a phone.
/// </summary>
public sealed class ImageIOAnalyzer : IImageAnalyzer
{
    /// <inheritdoc />
    public Task<ImageAnalysis> AnalyzeAsync(Stream imageStream, CancellationToken ct) => Task.Run(() => Analyze(imageStream), ct);

    /// <inheritdoc />
    public Task<byte[]> RenderJpegAsync(Stream imageStream, int maxEdge, CancellationToken ct) => Task.Run(() =>
    {
        using var source = Source(imageStream);
        using var image = Upright(source, maxEdge);
        return Jpeg(image, 0.88f);
    }, ct);

    private static ImageAnalysis Analyze(Stream imageStream)
    {
        using var source = Source(imageStream);
        var properties = source.GetProperties(0);
        // The size as it is seen, the right way up.
        var sideways = properties.Orientation is CIImageOrientation.LeftTop or CIImageOrientation.RightTop or CIImageOrientation.RightBottom or CIImageOrientation.LeftBottom;
        int across = properties.PixelWidth ?? 0, down = properties.PixelHeight ?? 0;
        using var work = Upright(source, ImageFingerprint.WorkEdge);
        using var thumbnail = Upright(source, ImageFingerprint.ThumbnailEdge);
        var signature = Grid(work);
        // Decoded at the size sharpness is measured at (ImageFingerprint.SharpnessEdge), so it is measured as it is.
        var sharpness = ImageFingerprint.Sharpness(Rgb(work, (int)work.Width, (int)work.Height), (int)work.Width, (int)work.Height);
        return new ImageAnalysis(sideways ? down : across, sideways ? across : down, FormatOf(source.TypeIdentifier), null,
            ImageFingerprint.PerceptualHash(signature), signature, Jpeg(thumbnail, 0.82f), sharpness);
    }

    private static CGImageSource Source(Stream imageStream)
    {
        imageStream.Position = 0;
        var bytes = new byte[imageStream.Length];
        imageStream.ReadExactly(bytes);
        return CGImageSource.FromData(NSData.FromArray(bytes)) ?? throw new UnreadableImageException("The picture could not be read.");
    }

    // Turned the right way up and no larger than asked, and decoded no larger either, which is what makes it quick.
    private static CGImage Upright(CGImageSource source, int maxEdge)
        => source.CreateThumbnail(0, new CGImageThumbnailOptions { CreateThumbnailFromImageAlways = true, CreateThumbnailWithTransform = true, MaxPixelSize = maxEdge })
           ?? throw new UnreadableImageException("The picture could not be decoded.");

    // Stretched to a square on purpose, as on a computer: aspect ratio is checked separately. Drawn in standard
    // colours, so a picture saved with and without a wide-gamut profile compares as one.
    private static byte[] Grid(CGImage image) => Rgb(image, ImageFingerprint.SignatureEdge, ImageFingerprint.SignatureEdge);

    // The picture drawn at the size given in standard colours, three bytes a pixel, row by row.
    private static byte[] Rgb(CGImage image, int width, int height)
    {
        var rgba = new byte[width * height * 4];
        using (var colours = CGColorSpace.CreateSrgb())
        using (var context = new CGBitmapContext(rgba, width, height, 8, width * 4, colours, CGImageAlphaInfo.NoneSkipLast))
        {
            context.InterpolationQuality = CGInterpolationQuality.High;
            context.DrawImage(new CGRect(0, 0, width, height), image);
        }
        var rgb = new byte[width * height * 3];
        for (var i = 0; i < width * height; i++)
        {
            rgb[i * 3] = rgba[i * 4];
            rgb[i * 3 + 1] = rgba[i * 4 + 1];
            rgb[i * 3 + 2] = rgba[i * 4 + 2];
        }
        return rgb;
    }

    private static byte[] Jpeg(CGImage image, float quality)
    {
        using var data = new NSMutableData();
        using (var destination = CGImageDestination.Create(data, "public.jpeg", 1) ?? throw new UnreadableImageException("A JPEG could not be made."))
        {
            destination.AddImage(image, new CGImageDestinationOptions { LossyCompressionQuality = quality });
            if (!destination.Close()) throw new UnreadableImageException("The JPEG could not be written.");
        }
        return data.ToArray();
    }

    private static string FormatOf(string? type) => type switch
    {
        "public.jpeg" => "JPEG",
        "public.heic" => "HEIC",
        "public.heif" => "HEIF",
        "public.png" => "PNG",
        "public.tiff" => "TIFF",
        "com.compuserve.gif" => "GIF",
        null => "UNKNOWN",
        _ => type[(type.LastIndexOf('.') + 1)..].ToUpperInvariant(),
    };
}
