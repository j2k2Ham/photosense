using ImageMagick;
using Trovolve.Desktop.Imaging;
using Trovolve.Domain.Entities;
using Trovolve.Domain.Matching;
using Trovolve.Domain.Services;
using Trovolve.Infrastructure.Imaging;
using Trovolve.Infrastructure.Metadata;

namespace Trovolve.Tests.Infrastructure;

/// <summary>A picture decoder to hold to the same measures, named in the test's title.</summary>
/// <param name="Name">Which decoder.</param>
/// <param name="Decoder">The decoder.</param>
/// <param name="Systems">Whether it is a system's own, which may lack an extension a format needs.</param>
public sealed record DecoderCase(string Name, IPictureDecoder Decoder, bool Systems)
{
    /// <summary>The analyzer the program uses, with this decoder.</summary>
    public PictureAnalyzer Analyzer => new(Decoder);

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>
/// Every decoder measures pictures the same way: Magick.NET, which stands in where a system has none, everywhere; and
/// the system's own where the tests run on it, Windows Imaging Component on Windows and ImageIO on a Mac.
/// </summary>
public class PictureDecodersTests
{
    /// <summary>The decoders of the system the tests run on.</summary>
    public static IEnumerable<Func<DecoderCase>> Decoders()
    {
        yield return () => new DecoderCase("Magick.NET", new MagickPictureDecoder(), Systems: false);
        if (OperatingSystem.IsWindows())
        {
            var wic = new WicPictureDecoder();
            yield return () => new DecoderCase("Windows Imaging Component", wic, Systems: true);
        }
        if (OperatingSystem.IsMacOS())
        {
            var imageIO = new ImageIOPictureDecoder();
            yield return () => new DecoderCase("ImageIO", imageIO, Systems: true);
        }
    }

    /// <summary>The system's own decoders alone, which are held to Magick's measures.</summary>
    public static IEnumerable<Func<DecoderCase>> SystemDecoders() => Decoders().Where(d => d().Systems);

    // A smooth, photo-like picture: a few low-frequency waves per colour channel, different for each seed.
    private static MagickImage Picture(int seed, int width = 1200, int height = 900)
    {
        var random = new Random(seed);
        var p = Enumerable.Range(0, 18).Select(_ => random.NextDouble() * 6 - 3).ToArray();
        var rgb = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                double u = (double)x / width, v = (double)y / height;
                for (int c = 0; c < 3; c++)
                {
                    int o = c * 6;
                    var value = 128 + 60 * Math.Sin(p[o] * u * 3 + p[o + 1]) + 60 * Math.Cos(p[o + 2] * v * 3 + p[o + 3]) + 30 * Math.Sin((u + v) * p[o + 4] * 4 + p[o + 5]);
                    rgb[(y * width + x) * 3 + c] = (byte)Math.Clamp(value, 0, 255);
                }
            }
        return new MagickImage(rgb, new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.RGB));
    }

    private static MemoryStream Encode(IMagickImage<byte> image, MagickFormat format, uint quality = 92)
    {
        image.Format = format;
        image.Quality = quality;
        return new MemoryStream(image.ToByteArray());
    }

    private static double Difference(byte[] a, byte[] b) => PhotoMatcher.SignatureDifference(a, b);

    // The same picture, as a camera stores it for each orientation: the sensor's pixels as they are, and the tag saying how to turn them.
    private static MemoryStream Oriented(MagickImage upright, ushort orientation)
    {
        using var stored = upright.Clone();
        switch (orientation)
        {
            case 2: stored.Flop(); break;
            case 3: stored.Rotate(180); break;
            case 4: stored.Flip(); break;
            case 5: stored.Transpose(); break;
            case 6: stored.Rotate(-90); break;
            case 7: stored.Transverse(); break;
            case 8: stored.Rotate(90); break;
        }
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Orientation, orientation);
        stored.SetProfile(exif);
        stored.Orientation = (Orientation)orientation;
        return Encode(stored, MagickFormat.Jpeg);
    }

    [Test]
    [MethodDataSource(nameof(Decoders))]
    public async Task Measures_A_Jpeg(DecoderCase decoder, CancellationToken ct)
    {
        using var picture = Picture(1);
        var analysis = await decoder.Analyzer.AnalyzeAsync(Encode(picture, MagickFormat.Jpeg, 85), ct);

        await Assert.That((analysis.Width, analysis.Height, analysis.Format, analysis.EncodedQuality)).IsEqualTo((1200, 900, "JPEG", 85));
        await Assert.That(analysis.Signature.Length).IsEqualTo(ImageFingerprint.SignatureEdge * ImageFingerprint.SignatureEdge * 3);
        await Assert.That(ImageFingerprint.PerceptualHash(analysis.Signature)).IsEqualTo(analysis.PerceptualHash);
        using var thumbnail = new MagickImage(analysis.ThumbnailJpeg);
        await Assert.That((thumbnail.Format, thumbnail.Width, thumbnail.Height)).IsEqualTo((MagickFormat.Jpeg, 320u, 240u));
    }

    [Test]
    [MethodDataSource(nameof(Decoders))]
    public async Task Formats_Without_A_Quality_Setting_Report_None_And_Small_Images_Are_Not_Enlarged(DecoderCase decoder, CancellationToken ct)
    {
        using var picture = Picture(2, 200, 100);
        var analysis = await decoder.Analyzer.AnalyzeAsync(Encode(picture, MagickFormat.Png), ct);
        await Assert.That((analysis.Width, analysis.Height, analysis.Format, analysis.EncodedQuality)).IsEqualTo((200, 100, "PNG", (int?)null));
        using var thumbnail = new MagickImage(analysis.ThumbnailJpeg);
        await Assert.That((thumbnail.Width, thumbnail.Height)).IsEqualTo((200u, 100u));
    }

    [Test]
    [MethodDataSource(nameof(Decoders))]
    public async Task A_Resized_Recompressed_Copy_In_Another_Format_Matches_Its_Original(DecoderCase decoder, CancellationToken ct)
    {
        using var picture = Picture(3, 2400, 1800);
        var original = await decoder.Analyzer.AnalyzeAsync(Encode(picture, MagickFormat.Png), ct);
        using var small = picture.Clone();
        small.Resize(600, 450);
        var copy = await decoder.Analyzer.AnalyzeAsync(Encode(small, MagickFormat.Jpeg, 80), ct);

        await Assert.That(PhotoMatcher.HashDistance(original.PerceptualHash, copy.PerceptualHash)).IsGreaterThanOrEqualTo(0).And.IsLessThanOrEqualTo(PhotoMatcher.CandidateBits);
        await Assert.That(Difference(original.Signature, copy.Signature)).IsGreaterThanOrEqualTo(0).And.IsLessThanOrEqualTo(PhotoMatcher.SameDifference);
    }

    [Test]
    [MethodDataSource(nameof(Decoders))]
    public async Task Different_Pictures_Are_Far_Apart(DecoderCase decoder, CancellationToken ct)
    {
        var hashes = new List<ulong>();
        for (int seed = 10; seed < 16; seed++)
        {
            using var picture = Picture(seed, 400, 300);
            hashes.Add((await decoder.Analyzer.AnalyzeAsync(Encode(picture, MagickFormat.Jpeg), ct)).PerceptualHash);
        }
        for (int i = 0; i < hashes.Count; i++)
            for (int j = i + 1; j < hashes.Count; j++)
                await Assert.That(PhotoMatcher.HashDistance(hashes[i], hashes[j]) > PhotoMatcher.CandidateBits).IsTrue().Because($"pictures {i} and {j} hash too close");
    }

    [Test]
    [MethodDataSource(nameof(Decoders))]
    public async Task A_Picture_Stored_In_Any_Orientation_With_Its_Tag_Is_Read_Upright(DecoderCase decoder, CancellationToken ct)
    {
        using var upright = Picture(4, 800, 600);
        var expected = await decoder.Analyzer.AnalyzeAsync(Encode(upright, MagickFormat.Jpeg), ct);

        for (ushort orientation = 1; orientation <= 8; orientation++)
        {
            var tagged = await decoder.Analyzer.AnalyzeAsync(Oriented(upright, orientation), ct);
            await Assert.That((tagged.Width, tagged.Height)).IsEqualTo((800, 600)).Because($"orientation {orientation}");
            await Assert.That(PhotoMatcher.HashDistance(expected.PerceptualHash, tagged.PerceptualHash)).IsGreaterThanOrEqualTo(0).And.IsLessThanOrEqualTo(PhotoMatcher.CandidateBits).Because($"orientation {orientation}");
            await Assert.That(Difference(expected.Signature, tagged.Signature)).IsGreaterThanOrEqualTo(0).And.IsLessThanOrEqualTo(PhotoMatcher.SameDifference).Because($"orientation {orientation}");
            using var thumbnail = new MagickImage(tagged.ThumbnailJpeg);
            await Assert.That((thumbnail.Width, thumbnail.Height)).IsEqualTo((320u, 240u));
        }
    }

    [Test]
    [MethodDataSource(nameof(Decoders))]
    public async Task A_Copy_Saved_In_Another_Colour_Space_Matches_The_Standard_One(DecoderCase decoder, CancellationToken ct)
    {
        using var picture = Picture(5, 800, 600);
        var standard = await decoder.Analyzer.AnalyzeAsync(Encode(picture, MagickFormat.Png), ct);

        // The same colours written as Adobe RGB numbers, with the profile that says so.
        using var converted = picture.Clone();
        converted.TransformColorSpace(ColorProfiles.SRGB, ColorProfiles.AdobeRGB1998);
        var rawNumbers = converted.GetPixels().ToByteArray(PixelMapping.RGB)!;
        var analysed = await decoder.Analyzer.AnalyzeAsync(Encode(converted, MagickFormat.Png), ct);

        using var unconverted = new MagickImage(rawNumbers, new PixelReadSettings(800, 600, StorageType.Char, PixelMapping.RGB));
        var misread = await decoder.Analyzer.AnalyzeAsync(Encode(unconverted, MagickFormat.Png), ct);
        await Assert.That(Difference(standard.Signature, analysed.Signature)).IsGreaterThanOrEqualTo(0).And.IsLessThanOrEqualTo(PhotoMatcher.SameDifference)
            .Because($"converted {Difference(standard.Signature, analysed.Signature)}, misread {Difference(standard.Signature, misread.Signature)}, same {PhotoMatcher.SameDifference}");
        await Assert.That(Difference(standard.Signature, misread.Signature) > PhotoMatcher.SimilarDifference).IsTrue().Because("the two colour spaces should differ visibly when the profile is ignored");
    }

    [Test]
    [MethodDataSource(nameof(Decoders))]
    public async Task A_Picture_With_An_Unusable_Colour_Profile_Is_Read_As_It_Is(DecoderCase decoder, CancellationToken ct)
    {
        using var picture = Picture(6, 400, 300);
        var plain = await decoder.Analyzer.AnalyzeAsync(Encode(picture, MagickFormat.Jpeg), ct);

        // A profile that announces RGB and then holds nothing a conversion could be made from.
        var unusable = ColorProfiles.SRGB.ToByteArray()!;
        Array.Clear(unusable, 128, unusable.Length - 128);
        using var tagged = picture.Clone();
        tagged.SetProfile(new ColorProfile(unusable));
        var analysed = await decoder.Analyzer.AnalyzeAsync(Encode(tagged, MagickFormat.Jpeg), ct);

        await Assert.That(Difference(plain.Signature, analysed.Signature)).IsGreaterThanOrEqualTo(0).And.IsLessThanOrEqualTo(PhotoMatcher.SameDifference);
        await Assert.That(PhotoMatcher.HashDistance(plain.PerceptualHash, analysed.PerceptualHash)).IsGreaterThanOrEqualTo(0).And.IsLessThanOrEqualTo(PhotoMatcher.CandidateBits);
    }

    [Test]
    [MethodDataSource(nameof(Decoders))]
    public async Task Each_Format_Is_Decoded_As_What_Its_First_Bytes_Say_Or_Says_What_The_System_Lacks(DecoderCase decoder, CancellationToken ct)
    {
        using var picture = Picture(seed: 3, width: 300, height: 200);
        foreach (var (written, read) in new[] { (MagickFormat.Jpeg, "JPEG"), (MagickFormat.Png, "PNG"), (MagickFormat.Gif, "GIF"), (MagickFormat.Bmp, "BMP"),
            (MagickFormat.Tiff, "TIFF"), (MagickFormat.WebP, "WEBP"), (MagickFormat.Avif, "AVIF") })
        {
            ImageAnalysis analysis;
            try
            {
                analysis = await decoder.Analyzer.AnalyzeAsync(new MemoryStream(picture.ToByteArray(written)), ct);
            }
            // A system without the free extension a format needs says which to install; JPEG to TIFF every system reads.
            catch (UnreadableImageException missing) when (decoder.Systems && read is "WEBP" or "AVIF")
            {
                await Assert.That(missing.Message).StartsWith("Install ");
                continue;
            }
            await Assert.That((analysis.Format, analysis.Width, analysis.Height)).IsEqualTo((read, 300, 200));
        }
    }

    [Test]
    [MethodDataSource(nameof(Decoders))]
    public async Task A_Picture_That_Is_Broken_Inside_Is_Refused_With_The_Decoders_Own_Words(DecoderCase decoder, CancellationToken ct)
    {
        // A JPEG's first bytes, then nothing a JPEG holds.
        var analyzing = await Assert.ThrowsExactlyAsync<UnreadableImageException>(() => decoder.Analyzer.AnalyzeAsync(new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5]), ct));
        await Assert.That(analyzing!.Message).IsNotEmpty();
    }

    [Test]
    [MethodDataSource(nameof(Decoders))]
    public async Task A_Picture_Claiming_To_Be_Wider_Than_Any_Real_One_Is_Refused_Not_Decoded(DecoderCase decoder, CancellationToken ct)
    {
        // 40,000 pixels wide and one high, a few bytes once compressed: refused before it is drawn.
        await Assert.ThrowsExactlyAsync<UnreadableImageException>(() => decoder.Analyzer.AnalyzeAsync(new MemoryStream(GrayPng(DecodedPicture.MaxEdge + 7_232, 1)), ct));
        // One within the limits decodes.
        await Assert.That((await decoder.Analyzer.AnalyzeAsync(new MemoryStream(GrayPng(4_000, 2)), ct)).Width).IsEqualTo(4_000);
    }

    [Test]
    [MethodDataSource(nameof(Decoders))]
    public async Task Renders_An_Upright_Jpeg_No_Larger_Than_Asked(DecoderCase decoder, CancellationToken ct)
    {
        using var picture = Picture(6);
        foreach (var (maxEdge, width, height) in new[] { (500, 500u, 375u), (5000, 1200u, 900u) })
        {
            using var rendered = new MagickImage(await decoder.Analyzer.RenderJpegAsync(Encode(picture, MagickFormat.Png), maxEdge, ct));
            await Assert.That((rendered.Format, rendered.Width, rendered.Height)).IsEqualTo((MagickFormat.Jpeg, width, height));
        }
    }

    [Test]
    [MethodDataSource(nameof(SystemDecoders))]
    public async Task The_Systems_Own_Decoder_Measures_A_Picture_As_Magick_Does(DecoderCase decoder, CancellationToken ct)
    {
        using var upright = Picture(12, 1600, 1200);
        for (ushort orientation = 1; orientation <= 8; orientation++)
        {
            var file = Oriented(upright, orientation).ToArray();
            var magick = await MagickPictureDecoder.Analyzer.AnalyzeAsync(new MemoryStream(file), ct);
            var system = await decoder.Analyzer.AnalyzeAsync(new MemoryStream(file), ct);
            await Assert.That((system.Width, system.Height, system.Format, system.EncodedQuality)).IsEqualTo((magick.Width, magick.Height, magick.Format, magick.EncodedQuality));
            await Assert.That(PhotoMatcher.HashDistance(magick.PerceptualHash, system.PerceptualHash)).IsGreaterThanOrEqualTo(0).And.IsLessThanOrEqualTo(PhotoMatcher.CandidateBits).Because($"orientation {orientation}");
            await Assert.That(Difference(magick.Signature, system.Signature)).IsGreaterThanOrEqualTo(0).And.IsLessThanOrEqualTo(PhotoMatcher.SameDifference).Because($"orientation {orientation}");
            await Assert.That(Math.Abs(system.Sharpness!.Value - magick.Sharpness!.Value) / magick.Sharpness.Value).IsLessThan(0.25).Because($"orientation {orientation}");
        }
    }

    // A grey PNG of the size given, written by hand: Magick, whose limits a test sets for the whole program, never makes it.
    private static byte[] GrayPng(int width, int height)
    {
        static byte[] BigEndian(uint n) => [(byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n];
        static uint Crc(IEnumerable<byte> bytes)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in bytes)
            {
                crc ^= b;
                for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
            return crc ^ 0xFFFFFFFFu;
        }
        static byte[] Chunk(string type, byte[] data)
        {
            byte[] typed = [.. System.Text.Encoding.ASCII.GetBytes(type), .. data];
            return [.. BigEndian((uint)data.Length), .. typed, .. BigEndian(Crc(typed))];
        }
        using var packed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(packed, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
            for (var row = 0; row < height; row++) zlib.Write(new byte[width + 1]);
        return [0x89, .. "PNG"u8, 0x0D, 0x0A, 0x1A, 0x0A,
            .. Chunk("IHDR", [.. BigEndian((uint)width), .. BigEndian((uint)height), 8, 0, 0, 0, 0]), .. Chunk("IDAT", packed.ToArray()), .. Chunk("IEND", [])];
    }
}

/// <summary>Measures and details read from a picture whatever decodes it.</summary>
public class PictureMeasuresTests
{
    private static MagickImage Picture(int seed, int width, int height)
    {
        var random = new Random(seed);
        var rgb = new byte[width * height * 3];
        random.NextBytes(rgb);
        return new MagickImage(rgb, new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.RGB));
    }

    private static MemoryStream Jpeg(MagickImage image)
    {
        image.Format = MagickFormat.Jpeg;
        return new MemoryStream(image.ToByteArray());
    }

    [Test]
    public async Task The_Hash_Sets_Half_Its_Bits_And_Ignores_Overall_Brightness()
    {
        // A smooth picture, as one is after reduction to the signature's grid.
        var rgb = new byte[ImageFingerprint.SignatureEdge * ImageFingerprint.SignatureEdge * 3];
        for (var i = 0; i < rgb.Length; i++) rgb[i] = (byte)(128 + 60 * Math.Sin(i / 97.0) + 40 * Math.Cos(i / 13.0));
        var brighter = rgb.Select(b => (byte)Math.Min(255, b / 2 + 60)).ToArray();
        var hash = ImageFingerprint.PerceptualHash(rgb);
        await Assert.That(System.Numerics.BitOperations.PopCount(hash)).IsGreaterThanOrEqualTo(28).And.IsLessThanOrEqualTo(36);
        await Assert.That(PhotoMatcher.HashDistance(hash, ImageFingerprint.PerceptualHash(brighter))).IsGreaterThanOrEqualTo(0).And.IsLessThanOrEqualTo(4);
    }

    [Test]
    public async Task Capture_Details_Are_Read_From_The_File(CancellationToken ct)
    {
        using var picture = Picture(8, 320, 240);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.DateTimeOriginal, "2024:04:07 17:35:59");
        exif.SetValue(ExifTag.SubsecTimeOriginal, "648");
        exif.SetValue(ExifTag.Model, "iPhone 15 Pro Max");
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        exif.SetValue(ExifTag.GPSLatitude, [new Rational(35, 1), new Rational(13, 1), new Rational(30, 1)]);
        exif.SetValue(ExifTag.GPSLongitudeRef, "W");
        exif.SetValue(ExifTag.GPSLongitude, [new Rational(80, 1), new Rational(50, 1), new Rational(0, 1)]);
        picture.SetProfile(exif);
        var photo = new Photo { SourcePath = "x", FileName = "x.jpg" };

        await new BasicExifMetadataExtractor().ExtractAsync(photo, Jpeg(picture), ct);

        await Assert.That(photo.TakenOn).IsEqualTo(TestPhotos.Shot);
        await Assert.That(photo.TakenOn!.Value.Kind).IsEqualTo(DateTimeKind.Unspecified);
        await Assert.That(photo.CameraModel).IsEqualTo("iPhone 15 Pro Max");
        await Assert.That(Math.Round(photo.Latitude!.Value, 3)).IsEqualTo(35.225);
        await Assert.That(Math.Round(photo.Longitude!.Value, 3)).IsEqualTo(Math.Round(-80.8333, 3));
    }

    [Test]
    public async Task A_Position_Of_Zero_Zero_Is_Treated_As_No_Position(CancellationToken ct)
    {
        using var picture = Picture(9, 320, 240);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.DateTimeOriginal, "2024:04:07 17:35:59");
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        exif.SetValue(ExifTag.GPSLatitude, [new Rational(0, 1), new Rational(0, 1), new Rational(0, 1)]);
        exif.SetValue(ExifTag.GPSLongitudeRef, "E");
        exif.SetValue(ExifTag.GPSLongitude, [new Rational(0, 1), new Rational(0, 1), new Rational(0, 1)]);
        picture.SetProfile(exif);
        var photo = new Photo { SourcePath = "x", FileName = "x.jpg" };

        await new BasicExifMetadataExtractor().ExtractAsync(photo, Jpeg(picture), ct);

        await Assert.That(photo.TakenOn).IsEqualTo(new DateTime(2024, 4, 7, 17, 35, 59));
        await Assert.That(photo.Latitude).IsNull();
        await Assert.That(photo.Longitude).IsNull();
    }
}
