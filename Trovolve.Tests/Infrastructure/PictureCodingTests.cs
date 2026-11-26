using ImageMagick;
using Trovolve.Desktop.Imaging;
using Trovolve.Domain.Services;
using Trovolve.Domain.ValueObjects;
using Trovolve.Infrastructure.Imaging;

namespace Trovolve.Tests.Infrastructure;

/// <summary>What every computer does alike once the system has decoded a picture: resizing, writing JPEG, reading a JPEG's quality.</summary>
public class PictureCodingTests
{
    // A picture with detail everywhere, different for each seed.
    private static byte[] Noise(int seed, int width, int height)
    {
        var rgb = new byte[width * height * 3];
        new Random(seed).NextBytes(rgb);
        return rgb;
    }

    // A smooth picture, as photos mostly are.
    private static byte[] Smooth(int width, int height)
    {
        var rgb = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                for (var c = 0; c < 3; c++)
                    rgb[(y * width + x) * 3 + c] = (byte)(128 + 100 * Math.Sin(x / 17.0 + c) * Math.Cos(y / 23.0 - c));
        return rgb;
    }

    // The JPEG decoded by Magick, as the pixels any browser would show.
    private static byte[] Decoded(byte[] jpeg, out uint width, out uint height)
    {
        using var image = new MagickImage(jpeg);
        (width, height) = (image.Width, image.Height);
        return image.GetPixels().ToByteArray(PixelMapping.RGB)!;
    }

    private static double MeanError(byte[] a, byte[] b) => a.Zip(b, (x, y) => Math.Abs(x - y)).Average();

    [Test]
    public async Task Each_Pixel_Reduced_Is_The_Average_Of_What_It_Covers_And_One_Enlarged_Takes_The_Colour_It_Falls_In()
    {
        byte[] square = [0, 0, 0, 100, 100, 100, 200, 200, 200, 60, 60, 60];
        await Assert.That(Pixels.Resize(square, 2, 2, 1, 1)).IsEquivalentTo((byte[])[90, 90, 90], CollectionOrdering.Matching);
        // A third of the way: each new pixel takes two parts of one old and one of the next.
        byte[] row = [0, 0, 0, 90, 90, 90, 180, 180, 180];
        await Assert.That(Pixels.Resize(row, 3, 1, 2, 1)).IsEquivalentTo((byte[])[30, 30, 30, 150, 150, 150], CollectionOrdering.Matching);
        await Assert.That(Pixels.Resize([10, 20, 30], 1, 1, 2, 2)).IsEquivalentTo((byte[])[10, 20, 30, 10, 20, 30, 10, 20, 30, 10, 20, 30], CollectionOrdering.Matching);
        // The same size is the same picture, a copy of it.
        await Assert.That(Pixels.Resize(square, 2, 2, 2, 2)).IsEquivalentTo(square, CollectionOrdering.Matching);
    }

    [Test]
    [Arguments(90, 1.5)]
    [Arguments(40, 4.0)]
    public async Task A_Jpeg_Written_Shows_The_Picture_As_Closely_As_Its_Quality_Allows(int quality, double meanError)
    {
        // Not a multiple of 8 either way, so the blocks at the edges repeat the last row and column.
        var picture = Smooth(101, 67);
        var jpeg = JpegWriter.Write(picture, 101, 67, quality);
        var shown = Decoded(jpeg, out var width, out var height);

        await Assert.That((width, height)).IsEqualTo((101u, 67u));
        await Assert.That(MeanError(picture, shown)).IsLessThan(meanError);
    }

    [Test]
    public async Task A_Quality_Outside_One_To_A_Hundred_Is_Held_To_Them()
    {
        var picture = Smooth(16, 16);
        await Assert.That(JpegWriter.Write(picture, 16, 16, 0)).IsEquivalentTo(JpegWriter.Write(picture, 16, 16, 1), CollectionOrdering.Matching);
        await Assert.That(JpegWriter.Write(picture, 16, 16, 250)).IsEquivalentTo(JpegWriter.Write(picture, 16, 16, 100), CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_Picture_Of_Fine_Detail_And_Of_A_Single_Pixel_Are_Written_As_Jpegs_Every_Decoder_Reads()
    {
        // Detail in every block, then a block whose only detail is its finest (runs of zeros longer than sixteen).
        var noise = Noise(1, 40, 24);
        var checks = new byte[8 * 8 * 3];
        for (var i = 0; i < 64; i++) checks.AsSpan(i * 3, 3).Fill((byte)(((i / 8) + i) % 2 == 0 ? 0 : 255));
        foreach (var (rgb, width, height) in new[] { (noise, 40, 24), (checks, 8, 8), ((byte[])[255, 0, 128], 1, 1) })
        {
            var shown = Decoded(JpegWriter.Write(rgb, width, height, 95), out var w, out var h);
            await Assert.That((w, h)).IsEqualTo(((uint)width, (uint)height));
            await Assert.That(MeanError(rgb, shown)).IsLessThan(32);
        }
    }

    [Test]
    public async Task The_Quality_A_Jpeg_Was_Saved_At_Is_Read_From_Its_Luminance_Table()
    {
        var picture = Smooth(64, 48);
        foreach (var quality in new[] { 10, 50, 75, 92, 100 })
            await Assert.That(JpegQuality.Estimate(JpegWriter.Write(picture, 64, 48, quality))).IsEqualTo(quality);
        using var image = new MagickImage(picture, new PixelReadSettings(64, 48, StorageType.Char, PixelMapping.RGB)) { Format = MagickFormat.Jpeg, Quality = 85 };
        await Assert.That(JpegQuality.Estimate(image.ToByteArray())).IsEqualTo(85);
    }

    [Test]
    public async Task A_File_With_No_Luminance_Table_Before_Its_Picture_Has_No_Quality()
    {
        byte[] Segment(byte marker, params byte[] body) => [0xFF, marker, (byte)((body.Length + 2) >> 8), (byte)(body.Length + 2), .. body];
        byte[] Table(byte idAndWidth, int entries) => [idAndWidth, .. Enumerable.Repeat((byte)1, entries)];
        byte[] start = [0xFF, 0xD8];

        await Assert.That(JpegQuality.Estimate([0x89, (byte)'P', (byte)'N', (byte)'G'])).IsNull();
        await Assert.That(JpegQuality.Estimate([0xFF, 0xD8])).IsNull();
        await Assert.That(JpegQuality.Estimate([0xFF, 0x00, 0, 0])).IsNull();
        // The picture's data, or its end, before any table.
        await Assert.That(JpegQuality.Estimate([.. start, .. Segment(0xDA, 0)])).IsNull();
        await Assert.That(JpegQuality.Estimate([.. start, 0xFF, 0xD9, 0, 0])).IsNull();
        // Something other than a segment where one should begin.
        await Assert.That(JpegQuality.Estimate([.. start, 0x12, 0x34, 0, 4, 0, 0])).IsNull();
        // A chrominance table alone, then one cut short, then one that says it runs past the file.
        await Assert.That(JpegQuality.Estimate([.. start, .. Segment(0xDB, Table(0x01, 64))])).IsNull();
        await Assert.That(JpegQuality.Estimate([.. start, .. Segment(0xDB, Table(0x10, 100))])).IsNull();
        await Assert.That(JpegQuality.Estimate([.. start, 0xFF, 0xDB, 0, 200, .. Table(0x00, 64)])).IsNull();
        // A table of two bytes a value, after a chrominance table in the same segment, and after a segment of another kind.
        var wide = (byte[])[0x10, .. JpegWriter.ZigZag.SelectMany(k => new byte[] { 0, JpegWriter.LuminanceTable(60)[k] })];
        await Assert.That(JpegQuality.Estimate([.. start, .. Segment(0xE1, 1, 2, 3), .. Segment(0xDB, [.. Table(0x01, 64), .. wide])])).IsEqualTo(60);
    }

    [Test]
    [Arguments(4032, 3024, 512, 512, 384)]
    [Arguments(3024, 4032, 512, 384, 512)]
    [Arguments(300, 200, 512, 300, 200)]
    [Arguments(10_000, 1, 512, 512, 1)]
    [Arguments(1, 10_000, 512, 1, 512)]
    public async Task Every_Decoder_Decodes_A_Picture_At_The_Same_Size(int width, int height, int maxEdge, int across, int down)
        => await Assert.That(DecodedPicture.Fit(width, height, maxEdge)).IsEqualTo((across, down));

    /// <summary>A decoder that must never be asked: what the analyzer refuses, it refuses first.</summary>
    private sealed class NeverAsked : IPictureDecoder
    {
        public Task<DecodedPicture> DecodeAsync(byte[] picture, PictureFormat format, int maxEdge, CancellationToken ct) => throw new InvalidOperationException("The decoder was asked.");
    }

    [Test]
    public async Task What_Is_Empty_Or_Not_A_Picture_It_Reads_Is_Refused_Before_The_System_Sees_It(CancellationToken ct)
    {
        var analyzer = new PictureAnalyzer(new NeverAsked());
        var empty = await Assert.ThrowsExactlyAsync<UnreadableImageException>(() => analyzer.AnalyzeAsync(new MemoryStream(), ct));
        await Assert.That(empty!.Message).IsEqualTo("The file is empty.");
        // Text, and a drawing, PostScript and ImageMagick's own scripts, as a file named .jpg could hold them: none is
        // handed to any decoder, which could read other files or run programs.
        foreach (var text in new[] { "not an image at all", "<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\"><image href=\"file:///etc/passwd\"/></svg>",
            "%!PS-Adobe-3.0\n(hello) print", "push graphic-context\nimage over 0,0 0,0 'label:@/etc/passwd'\npop graphic-context" })
        {
            var refused = await Assert.ThrowsExactlyAsync<UnreadableImageException>(() => analyzer.AnalyzeAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)), ct));
            await Assert.That(refused!.Message).IsEqualTo("It is not a picture Trovolve reads: JPEG, PNG, GIF, BMP, TIFF, WebP or HEIC.");
        }
        await Assert.ThrowsExactlyAsync<UnreadableImageException>(() => analyzer.RenderJpegAsync(new MemoryStream([1, 2, 3, 4]), 100, ct));
    }

    [Test]
    public async Task A_Computer_With_No_Decoder_Trovolve_Uses_Says_So_For_Every_Picture(CancellationToken ct)
    {
        var refused = await Assert.ThrowsExactlyAsync<UnreadableImageException>(() => new UnavailablePictureDecoder().DecodeAsync([0xFF, 0xD8, 0xFF], PictureFormat.Jpeg, 512, ct));
        await Assert.That(refused!.Message).IsEqualTo("Trovolve reads pictures on Windows and on a Mac, with the system's own decoder; this computer has none it can use.");
        await Assert.ThrowsAsync<OperationCanceledException>(() => new UnavailablePictureDecoder().DecodeAsync([], PictureFormat.Jpeg, 512, new CancellationToken(canceled: true)));
    }

    [Test]
    [Arguments(PictureFormat.Heic, false, false, "Install HEIF Image Extensions and HEVC Video Extensions from the Microsoft Store to read photos from an iPhone.")]
    [Arguments(PictureFormat.Heic, false, true, "Install HEIF Image Extensions from the Microsoft Store to read photos from an iPhone.")]
    [Arguments(PictureFormat.Heic, true, false, "Install HEVC Video Extensions from the Microsoft Store to read photos from an iPhone.")]
    [Arguments(PictureFormat.Heic, true, true, null)]
    [Arguments(PictureFormat.WebP, false, true, "Install Webp Image Extensions from the Microsoft Store to read WebP pictures.")]
    [Arguments(PictureFormat.WebP, true, true, null)]
    [Arguments(PictureFormat.Avif, false, true, "Install AV1 Video Extension from the Microsoft Store to read AVIF pictures.")]
    [Arguments(PictureFormat.Avif, true, true, null)]
    [Arguments(PictureFormat.Jpeg, false, false, null)]
    public async Task Windows_Says_Which_Free_Extension_To_Install_For_A_Picture_It_Cannot_Read(PictureFormat format, bool readerFound, bool hevcInstalled, string? said)
        => await Assert.That(WindowsExtensions.Missing(format, readerFound, hevcInstalled)).IsEqualTo(said);
}
