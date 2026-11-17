using Android.Graphics;
using Android.Util;
using PhotoSense.Domain.Services;

namespace PhotoSense.AndroidApp;

/// <summary>
/// Pictures decoded by the phone itself, which reads HEIC, JPEG and the rest and turns them the right way up, and
/// measured as on a computer. Magick.NET, which a computer uses, has no build for a phone.
/// </summary>
public sealed class AndroidImageAnalyzer : IImageAnalyzer
{
    /// <inheritdoc />
    public Task<ImageAnalysis> AnalyzeAsync(Stream imageStream, CancellationToken ct) => Task.Run(() => Analyze(imageStream), ct);

    /// <inheritdoc />
    public Task<byte[]> RenderJpegAsync(Stream imageStream, int maxEdge, CancellationToken ct) => Task.Run(() =>
    {
        using var image = Decode(Bytes(imageStream), maxEdge, out _, out _);
        return Jpeg(image, 88);
    }, ct);

    private static ImageAnalysis Analyze(Stream imageStream)
    {
        var bytes = Bytes(imageStream);
        using var work = Decode(bytes, ImageFingerprint.WorkEdge, out var size, out var type);
        using var thumbnail = Decode(bytes, ImageFingerprint.ThumbnailEdge, out _, out _);
        var signature = Grid(work);
        // Decoded at the size sharpness is measured at (ImageFingerprint.SharpnessEdge), so it is measured as it is.
        var sharpness = ImageFingerprint.Sharpness(Rgb(work), work.Width, work.Height);
        return new ImageAnalysis(size.Width, size.Height, FormatOf(type), null, ImageFingerprint.PerceptualHash(signature), signature, Jpeg(thumbnail, 82), sharpness);
    }

    private static byte[] Bytes(Stream imageStream)
    {
        imageStream.Position = 0;
        var bytes = new byte[imageStream.Length];
        imageStream.ReadExactly(bytes);
        return bytes;
    }

    // Decoded no larger than asked, which is what makes it quick, in standard colours, upright, and with its pixels to hand.
    private static Bitmap Decode(byte[] bytes, int maxEdge, out Size size, out string? type)
    {
        var told = new Told(maxEdge);
        Bitmap? bitmap;
        try { bitmap = ImageDecoder.DecodeBitmap(ImageDecoder.CreateSource(Java.Nio.ByteBuffer.Wrap(bytes)), told); }
        // Android's own word for a picture it cannot decode, for any reason; the rest of PhotoSense knows it by one of its own.
        catch (Java.IO.IOException e) { throw new UnreadableImageException(e.Message ?? "The picture could not be decoded.", e); }
        (size, type) = (told.Size, told.Type);
        return bitmap ?? throw new UnreadableImageException("The picture could not be decoded.");
    }

    private sealed class Told(int maxEdge) : Java.Lang.Object, ImageDecoder.IOnHeaderDecodedListener
    {
        /// <summary>The picture's size, as its header says.</summary>
        public Size Size { get; private set; } = new(0, 0);
        /// <summary>The picture's type, as its header says.</summary>
        public string? Type { get; private set; }

        /// <summary>Asks for the picture at the size wanted, in standard colours, with its pixels in memory.</summary>
        public void OnHeaderDecoded(ImageDecoder decoder, ImageDecoder.ImageInfo info, ImageDecoder.Source source)
        {
            Size = info.Size;
            Type = info.MimeType;
            var scale = Math.Min(1.0, (double)maxEdge / Math.Max(info.Size.Width, info.Size.Height));
            decoder.SetTargetSize(Math.Max(1, (int)Math.Round(info.Size.Width * scale)), Math.Max(1, (int)Math.Round(info.Size.Height * scale)));
            decoder.Allocator = ImageDecoderAllocator.Software;
            decoder.SetTargetColorSpace(ColorSpace.Get(ColorSpace.Named.Srgb!)!);
        }
    }

    // Stretched to a square on purpose, as on a computer: aspect ratio is checked separately.
    private static byte[] Grid(Bitmap image)
    {
        const int n = ImageFingerprint.SignatureEdge;
        using var square = Bitmap.CreateScaledBitmap(image, n, n, filter: true)!;
        return Rgb(square);
    }

    // A picture's pixels, three bytes each, row by row.
    private static byte[] Rgb(Bitmap image)
    {
        int width = image.Width, height = image.Height;
        var pixels = new int[width * height];
        image.GetPixels(pixels, 0, width, 0, 0, width, height);
        var rgb = new byte[pixels.Length * 3];
        for (var i = 0; i < pixels.Length; i++)
        {
            rgb[i * 3] = (byte)(pixels[i] >> 16);
            rgb[i * 3 + 1] = (byte)(pixels[i] >> 8);
            rgb[i * 3 + 2] = (byte)pixels[i];
        }
        return rgb;
    }

    private static byte[] Jpeg(Bitmap image, int quality)
    {
        using var output = new MemoryStream();
        if (!image.Compress(Bitmap.CompressFormat.Jpeg!, quality, output)) throw new UnreadableImageException("The JPEG could not be written.");
        return output.ToArray();
    }

    private static string FormatOf(string? type) => type switch
    {
        "image/jpeg" => "JPEG",
        "image/heic" => "HEIC",
        "image/heif" => "HEIF",
        "image/png" => "PNG",
        "image/gif" => "GIF",
        "image/webp" => "WEBP",
        null => "UNKNOWN",
        _ => type[(type.IndexOf('/') + 1)..].ToUpperInvariant(),
    };
}
