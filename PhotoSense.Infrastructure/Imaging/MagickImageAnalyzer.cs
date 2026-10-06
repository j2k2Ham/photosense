using ImageMagick;
using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Imaging;

public sealed class MagickImageAnalyzer : IImageAnalyzer
{
    public const int SignatureEdge = 32;
    private const int WorkEdge = 512;
    private const int ThumbnailEdge = 320;

    // Low-frequency DCT terms 1..8 of a 32-sample row; term 0 (overall brightness) is left out of the hash.
    private static readonly double[,] Cosines = BuildCosines();

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
        using var image = Load(imageStream, WorkEdge);
        // A JPEG is decoded at reduced size for speed, so its true size comes from the header.
        var sideways = image.Orientation is OrientationType.LeftTop or OrientationType.RightTop or OrientationType.RightBottom or OrientationType.LeftBottom;
        int width = (int)(sideways ? image.BaseHeight : image.BaseWidth), height = (int)(sideways ? image.BaseWidth : image.BaseHeight);
        var format = image.Format.ToString().ToUpperInvariant();
        int? quality = image.Format is MagickFormat.Jpeg or MagickFormat.Jpg && image.Quality > 0 ? (int)image.Quality : null;

        image.AutoOrient();
        if (image.Width > WorkEdge || image.Height > WorkEdge) image.Scale(new MagickGeometry(WorkEdge, WorkEdge));
        ToStandardColors(image);

        using var thumbnail = image.Clone();
        if (thumbnail.Width > ThumbnailEdge || thumbnail.Height > ThumbnailEdge) thumbnail.Resize(new MagickGeometry(ThumbnailEdge, ThumbnailEdge));

        // Stretched to a square on purpose: aspect ratio is checked separately, and a fixed grid lets two signatures be compared cell by cell.
        image.Resize(new MagickGeometry(SignatureEdge, SignatureEdge) { IgnoreAspectRatio = true });
        var signature = image.GetPixels().ToByteArray(PixelMapping.RGB)
            ?? throw new InvalidOperationException("Image has no pixel data");

        return new ImageAnalysis(width, height, format, quality, PerceptualHash(signature), signature, ToJpeg(thumbnail, 82));
    }

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

    /// <summary>64-bit DCT hash of a 32x32 RGB grid: one bit per low-frequency term, set when above the median.</summary>
    public static ulong PerceptualHash(ReadOnlySpan<byte> rgb)
    {
        const int n = SignatureEdge;
        Span<double> luma = stackalloc double[n * n];
        for (int i = 0; i < luma.Length; i++)
            luma[i] = 0.299 * rgb[i * 3] + 0.587 * rgb[i * 3 + 1] + 0.114 * rgb[i * 3 + 2];

        Span<double> terms = stackalloc double[64];
        for (int u = 0; u < 8; u++)
            for (int v = 0; v < 8; v++)
            {
                double sum = 0;
                for (int y = 0; y < n; y++)
                {
                    double cy = Cosines[u, y];
                    for (int x = 0; x < n; x++) sum += luma[y * n + x] * cy * Cosines[v, x];
                }
                terms[u * 8 + v] = sum;
            }

        Span<double> sorted = stackalloc double[64];
        terms.CopyTo(sorted);
        sorted.Sort();
        var median = sorted[32];
        ulong hash = 0;
        for (int i = 0; i < 64; i++)
            if (terms[i] > median) hash |= 1UL << i;
        return hash;
    }

    private static double[,] BuildCosines()
    {
        var table = new double[8, SignatureEdge];
        for (int u = 0; u < 8; u++)
            for (int x = 0; x < SignatureEdge; x++)
                table[u, x] = Math.Cos((2 * x + 1) * (u + 1) * Math.PI / (2 * SignatureEdge));
        return table;
    }
}
