using ImageMagick;
using Trovolve.Domain.Matching;
using Trovolve.Domain.Entities;
using Trovolve.Domain.Services;
using Trovolve.Desktop.Imaging;
using Trovolve.Infrastructure.Metadata;

namespace Trovolve.Tests.Infrastructure;

/// <summary>Decoding and measuring pictures with Magick.NET.</summary>
public class MagickImageAnalyzerTests
{
    private readonly MagickImageAnalyzer _analyzer = new();

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

    [Test]
    public async Task Measures_A_Jpeg(CancellationToken ct)
    {
        using var picture = Picture(1);
        var analysis = await _analyzer.AnalyzeAsync(Encode(picture, MagickFormat.Jpeg, 85), ct);

        await Assert.That((analysis.Width, analysis.Height, analysis.Format, analysis.EncodedQuality)).IsEqualTo((1200, 900, "JPEG", 85));
        await Assert.That(analysis.Signature.Length).IsEqualTo(ImageFingerprint.SignatureEdge * ImageFingerprint.SignatureEdge * 3);
        await Assert.That(ImageFingerprint.PerceptualHash(analysis.Signature)).IsEqualTo(analysis.PerceptualHash);
        using var thumbnail = new MagickImage(analysis.ThumbnailJpeg);
        await Assert.That((thumbnail.Format, thumbnail.Width, thumbnail.Height)).IsEqualTo((MagickFormat.Jpeg, 320u, 240u));
    }

    [Test]
    public async Task Formats_Without_A_Quality_Setting_Report_None_And_Small_Images_Are_Not_Enlarged(CancellationToken ct)
    {
        using var picture = Picture(2, 200, 100);
        var analysis = await _analyzer.AnalyzeAsync(Encode(picture, MagickFormat.Png), ct);
        await Assert.That((analysis.Width, analysis.Height, analysis.Format, analysis.EncodedQuality)).IsEqualTo((200, 100, "PNG", (int?)null));
        using var thumbnail = new MagickImage(analysis.ThumbnailJpeg);
        await Assert.That((thumbnail.Width, thumbnail.Height)).IsEqualTo((200u, 100u));
    }

    [Test]
    public async Task A_Resized_Recompressed_Copy_In_Another_Format_Matches_Its_Original(CancellationToken ct)
    {
        using var picture = Picture(3, 2400, 1800);
        var original = await _analyzer.AnalyzeAsync(Encode(picture, MagickFormat.Png), ct);
        using var small = picture.Clone();
        small.Resize(600, 450);
        var copy = await _analyzer.AnalyzeAsync(Encode(small, MagickFormat.Jpeg, 80), ct);

        await Assert.That(PhotoMatcher.HashDistance(original.PerceptualHash, copy.PerceptualHash)).IsGreaterThanOrEqualTo(0).And.IsLessThanOrEqualTo(PhotoMatcher.CandidateBits);
        await Assert.That(Difference(original.Signature, copy.Signature)).IsGreaterThanOrEqualTo(0).And.IsLessThanOrEqualTo(PhotoMatcher.SameDifference);
    }

    [Test]
    public async Task Different_Pictures_Are_Far_Apart(CancellationToken ct)
    {
        var hashes = new List<ulong>();
        for (int seed = 10; seed < 16; seed++)
        {
            using var picture = Picture(seed, 400, 300);
            hashes.Add((await _analyzer.AnalyzeAsync(Encode(picture, MagickFormat.Jpeg), ct)).PerceptualHash);
        }
        for (int i = 0; i < hashes.Count; i++)
            for (int j = i + 1; j < hashes.Count; j++)
                await Assert.That(PhotoMatcher.HashDistance(hashes[i], hashes[j]) > PhotoMatcher.CandidateBits).IsTrue().Because($"pictures {i} and {j} hash too close");
    }

    [Test]
    public async Task A_Picture_Stored_Sideways_With_An_Orientation_Tag_Is_Read_Upright(CancellationToken ct)
    {
        using var upright = Picture(4, 800, 600);
        var expected = await _analyzer.AnalyzeAsync(Encode(upright, MagickFormat.Jpeg), ct);

        // What a phone held upright writes: the sensor's pixels as they are, plus a tag saying how to turn them.
        using var sideways = upright.Clone();
        sideways.Rotate(-90);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Orientation, (ushort)6);
        sideways.SetProfile(exif);
        sideways.Orientation = Orientation.RightTop;
        var tagged = await _analyzer.AnalyzeAsync(Encode(sideways, MagickFormat.Jpeg), ct);

        await Assert.That((tagged.Width, tagged.Height)).IsEqualTo((800, 600));
        await Assert.That(PhotoMatcher.HashDistance(expected.PerceptualHash, tagged.PerceptualHash)).IsGreaterThanOrEqualTo(0).And.IsLessThanOrEqualTo(PhotoMatcher.CandidateBits);
        await Assert.That(Difference(expected.Signature, tagged.Signature)).IsGreaterThanOrEqualTo(0).And.IsLessThanOrEqualTo(PhotoMatcher.SameDifference);
        using var thumbnail = new MagickImage(tagged.ThumbnailJpeg);
        await Assert.That((thumbnail.Width, thumbnail.Height)).IsEqualTo((320u, 240u));
    }

    [Test]
    public async Task A_Copy_Saved_In_Another_Colour_Space_Matches_The_Standard_One(CancellationToken ct)
    {
        using var picture = Picture(5, 800, 600);
        var standard = await _analyzer.AnalyzeAsync(Encode(picture, MagickFormat.Png), ct);

        // The same colours written as Adobe RGB numbers, with the profile that says so.
        using var converted = picture.Clone();
        converted.TransformColorSpace(ColorProfiles.SRGB, ColorProfiles.AdobeRGB1998);
        var rawNumbers = converted.GetPixels().ToByteArray(PixelMapping.RGB)!;
        var analysed = await _analyzer.AnalyzeAsync(Encode(converted, MagickFormat.Png), ct);

        using var unconverted = new MagickImage(rawNumbers, new PixelReadSettings(800, 600, StorageType.Char, PixelMapping.RGB));
        var misread = await _analyzer.AnalyzeAsync(Encode(unconverted, MagickFormat.Png), ct);
        await Assert.That(Difference(standard.Signature, analysed.Signature)).IsGreaterThanOrEqualTo(0).And.IsLessThanOrEqualTo(PhotoMatcher.SameDifference);
        await Assert.That(Difference(standard.Signature, misread.Signature) > PhotoMatcher.SimilarDifference).IsTrue().Because("the two colour spaces should differ visibly when the profile is ignored");
    }

    [Test]
    public async Task What_Is_Not_A_Picture_It_Reads_Is_Refused_Before_Magick_Sees_It_Whatever_It_Is_Called(CancellationToken ct)
    {
        // Text, and a drawing, PostScript and ImageMagick's own scripts, as a file named .jpg could hold them: none is
        // handed to their readers, which could read other files or run programs.
        foreach (var text in new[] { "not an image at all", "<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\"><image href=\"file:///etc/passwd\"/></svg>",
            "%!PS-Adobe-3.0\n(hello) print", "push graphic-context\nimage over 0,0 0,0 'label:@/etc/passwd'\npop graphic-context", "<?xml version=\"1.0\"?><image><read filename=\"/etc/passwd\"/></image>" })
        {
            var analyzing = await Assert.ThrowsExactlyAsync<UnreadableImageException>(() => _analyzer.AnalyzeAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)), ct));
            await Assert.That((analyzing!.Message, analyzing.InnerException)).IsEqualTo(("It is not a picture Trovolve reads: JPEG, PNG, GIF, BMP, TIFF, WebP or HEIC.", (Exception?)null));
        }
        await Assert.ThrowsExactlyAsync<UnreadableImageException>(() => _analyzer.RenderJpegAsync(new MemoryStream([1, 2, 3, 4]), 100, ct));
    }

    [Test]
    [Arguments(MagickFormat.Jpeg, "JPEG")]
    [Arguments(MagickFormat.Png, "PNG")]
    [Arguments(MagickFormat.Gif, "GIF")]
    [Arguments(MagickFormat.Bmp, "BMP")]
    [Arguments(MagickFormat.Tiff, "TIFF")]
    [Arguments(MagickFormat.WebP, "WEBP")]
    [Arguments(MagickFormat.Avif, "AVIF")]
    public async Task Each_Format_It_Reads_Is_Decoded_As_What_Its_First_Bytes_Say(MagickFormat written, string read, CancellationToken ct)
    {
        using var picture = Picture(seed: 3, width: 300, height: 200);
        var analysis = await _analyzer.AnalyzeAsync(new MemoryStream(picture.ToByteArray(written)), ct);
        await Assert.That((analysis.Format, analysis.Width, analysis.Height)).IsEqualTo((read, 300, 200));
    }

    [Test]
    public async Task A_Picture_That_Is_Broken_Inside_Is_Refused_With_Magicks_Own_Words(CancellationToken ct)
    {
        // A JPEG's first bytes, then nothing a JPEG holds: Magick's error is turned into the one Trovolve knows.
        var analyzing = await Assert.ThrowsExactlyAsync<UnreadableImageException>(() => _analyzer.AnalyzeAsync(new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5]), ct));
        await Assert.That(analyzing!.InnerException).IsAssignableTo<MagickException>();
        await Assert.That(analyzing.Message).IsEqualTo(analyzing.InnerException!.Message);
    }

    [Test]
    public async Task A_Picture_Claiming_To_Be_Wider_Than_Any_Real_One_Is_Refused_Not_Decoded(CancellationToken ct)
    {
        // 40,000 pixels wide and one high, a few bytes once compressed: the decoder's limits refuse it before it is drawn.
        var wide = await Assert.ThrowsExactlyAsync<UnreadableImageException>(() => _analyzer.AnalyzeAsync(new MemoryStream(GrayPng(MagickImageAnalyzer.MaxEdge + 7_232, 1)), ct));
        await Assert.That(wide!.InnerException).IsAssignableTo<MagickException>();
        // One within the limits decodes.
        await Assert.That((await _analyzer.AnalyzeAsync(new MemoryStream(GrayPng(4_000, 2)), ct)).Width).IsEqualTo(4_000);
    }

    // A grey PNG of the size given, written by hand: Magick, whose limits a test sets for the whole program, never makes it.
    private static byte[] GrayPng(uint width, uint height)
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
            .. Chunk("IHDR", [.. BigEndian(width), .. BigEndian(height), 8, 0, 0, 0, 0]), .. Chunk("IDAT", packed.ToArray()), .. Chunk("IEND", [])];
    }

    [Test]
    public async Task An_Empty_File_Is_Refused_Before_Magick_Is_Asked(CancellationToken ct)
    {
        // A transfer that never finished: Magick would take it for a mistake of the caller's.
        var error = await Assert.ThrowsExactlyAsync<UnreadableImageException>(() => _analyzer.AnalyzeAsync(new MemoryStream(), ct));
        await Assert.That((error!.Message, error.InnerException)).IsEqualTo(("The file is empty.", (Exception?)null));
    }

    [Test]
    [Arguments(500, 500, 375)]
    [Arguments(5000, 1200, 900)] // never enlarged
    public async Task Renders_An_Upright_Jpeg_No_Larger_Than_Asked(int maxEdge, uint width, uint height, CancellationToken ct)
    {
        using var picture = Picture(6);
        using var rendered = new MagickImage(await _analyzer.RenderJpegAsync(Encode(picture, MagickFormat.Png), maxEdge, ct));
        await Assert.That((rendered.Format, rendered.Width, rendered.Height)).IsEqualTo((MagickFormat.Jpeg, width, height));
    }

    [Test]
    public async Task The_Hash_Sets_Half_Its_Bits_And_Ignores_Overall_Brightness(CancellationToken ct)
    {
        using var picture = Picture(7, ImageFingerprint.SignatureEdge, ImageFingerprint.SignatureEdge);
        var rgb = picture.GetPixels().ToByteArray(PixelMapping.RGB)!;
        var brighter = rgb.Select(b => (byte)Math.Min(255, b / 2 + 60)).ToArray();
        var hash = ImageFingerprint.PerceptualHash(rgb);
        await Assert.That(System.Numerics.BitOperations.PopCount(hash)).IsEqualTo(31); // terms above the median of 64
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

        await new BasicExifMetadataExtractor().ExtractAsync(photo, Encode(picture, MagickFormat.Jpeg), ct);

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

        await new BasicExifMetadataExtractor().ExtractAsync(photo, Encode(picture, MagickFormat.Jpeg), ct);

        await Assert.That(photo.TakenOn).IsEqualTo(new DateTime(2024, 4, 7, 17, 35, 59));
        await Assert.That(photo.Latitude).IsNull();
        await Assert.That(photo.Longitude).IsNull();
    }

    [Test]
    public async Task A_Picture_With_An_Unusable_Colour_Profile_Is_Read_As_It_Is(CancellationToken ct)
    {
        using var picture = Picture(6, 400, 300);
        var plain = await _analyzer.AnalyzeAsync(Encode(picture, MagickFormat.Jpeg), ct);

        // A profile that announces RGB and then holds nothing a conversion could be made from.
        var unusable = ColorProfiles.SRGB.ToByteArray()!;
        Array.Clear(unusable, 128, unusable.Length - 128);
        using var tagged = picture.Clone();
        tagged.SetProfile(new ColorProfile(unusable));
        var encoded = Encode(tagged, MagickFormat.Jpeg);
        using (var reread = new MagickImage(encoded.ToArray()))
            await Assert.That(reread.GetColorProfile()).IsNotNull();

        var analysed = await _analyzer.AnalyzeAsync(encoded, ct);

        await Assert.That(Difference(plain.Signature, analysed.Signature)).IsGreaterThanOrEqualTo(0).And.IsLessThanOrEqualTo(PhotoMatcher.SameDifference);
        await Assert.That(analysed.PerceptualHash).IsEqualTo(plain.PerceptualHash);
    }
}
