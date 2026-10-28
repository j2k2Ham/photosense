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
    public Task<ImageAnalysis> AnalyzeAsync(Stream imageStream, CancellationToken ct = default) => Task.Run(() => Analyze(imageStream), ct);

    public Task<byte[]> RenderJpegAsync(Stream imageStream, int maxEdge, CancellationToken ct = default) => Task.Run(() =>
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
        return new ImageAnalysis(sideways ? down : across, sideways ? across : down, FormatOf(source.TypeIdentifier), null,
            ImageFingerprint.PerceptualHash(signature), signature, Jpeg(thumbnail, 0.82f));
    }

    private static CGImageSource Source(Stream imageStream)
    {
        imageStream.Position = 0;
        var bytes = new byte[imageStream.Length];
        imageStream.ReadExactly(bytes);
        return CGImageSource.FromData(NSData.FromArray(bytes)) ?? throw new InvalidOperationException("The picture could not be read.");
    }

    // Turned the right way up and no larger than asked, and decoded no larger either, which is what makes it quick.
    private static CGImage Upright(CGImageSource source, int maxEdge)
        => source.CreateThumbnail(0, new CGImageThumbnailOptions { CreateThumbnailFromImageAlways = true, CreateThumbnailWithTransform = true, MaxPixelSize = maxEdge })
           ?? throw new InvalidOperationException("The picture could not be decoded.");

    // Stretched to a square on purpose, as on a computer: aspect ratio is checked separately. Drawn in standard
    // colours, so a picture saved with and without a wide-gamut profile compares as one.
    private static byte[] Grid(CGImage image)
    {
        const int n = ImageFingerprint.SignatureEdge;
        var rgba = new byte[n * n * 4];
        using (var colours = CGColorSpace.CreateSrgb())
        using (var context = new CGBitmapContext(rgba, n, n, 8, n * 4, colours, CGImageAlphaInfo.NoneSkipLast))
        {
            context.InterpolationQuality = CGInterpolationQuality.High;
            context.DrawImage(new CGRect(0, 0, n, n), image);
        }
        var rgb = new byte[n * n * 3];
        for (var i = 0; i < n * n; i++)
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
        using (var destination = CGImageDestination.Create(data, "public.jpeg", 1) ?? throw new InvalidOperationException("A JPEG could not be made."))
        {
            destination.AddImage(image, new CGImageDestinationOptions { LossyCompressionQuality = quality });
            if (!destination.Close()) throw new InvalidOperationException("The JPEG could not be written.");
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
