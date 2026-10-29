using ImageMagick;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Entities;
using PhotoSense.Infrastructure.Imaging;
using PhotoSense.Infrastructure.Metadata;

namespace PhotoSense.Tests.Infrastructure;

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
    public async Task Measures_A_Jpeg()
    {
        using var picture = Picture(1);
        var analysis = await _analyzer.AnalyzeAsync(Encode(picture, MagickFormat.Jpeg, 85));

        await Assert.That((analysis.Width, analysis.Height, analysis.Format, analysis.EncodedQuality)).IsEqualTo((1200, 900, "JPEG", 85));
        await Assert.That(analysis.Signature.Length).IsEqualTo(MagickImageAnalyzer.SignatureEdge * MagickImageAnalyzer.SignatureEdge * 3);
        await Assert.That(MagickImageAnalyzer.PerceptualHash(analysis.Signature)).IsEqualTo(analysis.PerceptualHash);
        using var thumbnail = new MagickImage(analysis.ThumbnailJpeg);
        await Assert.That((thumbnail.Format, thumbnail.Width, thumbnail.Height)).IsEqualTo((MagickFormat.Jpeg, 320u, 240u));
    }

    [Test]
    public async Task Formats_Without_A_Quality_Setting_Report_None_And_Small_Images_Are_Not_Enlarged()
    {
        using var picture = Picture(2, 200, 100);
        var analysis = await _analyzer.AnalyzeAsync(Encode(picture, MagickFormat.Png));
        await Assert.That((analysis.Width, analysis.Height, analysis.Format, analysis.EncodedQuality)).IsEqualTo((200, 100, "PNG", (int?)null));
        using var thumbnail = new MagickImage(analysis.ThumbnailJpeg);
        await Assert.That((thumbnail.Width, thumbnail.Height)).IsEqualTo((200u, 100u));
    }

    [Test]
    public async Task A_Resized_Recompressed_Copy_In_Another_Format_Matches_Its_Original()
    {
        using var picture = Picture(3, 2400, 1800);
        var original = await _analyzer.AnalyzeAsync(Encode(picture, MagickFormat.Png));
        using var small = picture.Clone();
        small.Resize(600, 450);
        var copy = await _analyzer.AnalyzeAsync(Encode(small, MagickFormat.Jpeg, 80));

        await Assert.That(PhotoMatcher.HashDistance(original.PerceptualHash, copy.PerceptualHash)).IsGreaterThanOrEqualTo(0).And.IsLessThanOrEqualTo(PhotoMatcher.CandidateBits);
        await Assert.That(Difference(original.Signature, copy.Signature)).IsGreaterThanOrEqualTo(0).And.IsLessThanOrEqualTo(PhotoMatcher.SameDifference);
    }

    [Test]
    public async Task Different_Pictures_Are_Far_Apart()
    {
        var hashes = new List<ulong>();
        for (int seed = 10; seed < 16; seed++)
        {
            using var picture = Picture(seed, 400, 300);
            hashes.Add((await _analyzer.AnalyzeAsync(Encode(picture, MagickFormat.Jpeg))).PerceptualHash);
        }
        for (int i = 0; i < hashes.Count; i++)
            for (int j = i + 1; j < hashes.Count; j++)
                await Assert.That(PhotoMatcher.HashDistance(hashes[i], hashes[j]) > PhotoMatcher.CandidateBits).IsTrue().Because($"pictures {i} and {j} hash too close");
    }

    [Test]
    public async Task A_Picture_Stored_Sideways_With_An_Orientation_Tag_Is_Read_Upright()
    {
        using var upright = Picture(4, 800, 600);
        var expected = await _analyzer.AnalyzeAsync(Encode(upright, MagickFormat.Jpeg));

        // What a phone held upright writes: the sensor's pixels as they are, plus a tag saying how to turn them.
        using var sideways = upright.Clone();
        sideways.Rotate(-90);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Orientation, (ushort)6);
        sideways.SetProfile(exif);
        sideways.Orientation = OrientationType.RightTop;
        var tagged = await _analyzer.AnalyzeAsync(Encode(sideways, MagickFormat.Jpeg));

        await Assert.That((tagged.Width, tagged.Height)).IsEqualTo((800, 600));
        await Assert.That(PhotoMatcher.HashDistance(expected.PerceptualHash, tagged.PerceptualHash)).IsGreaterThanOrEqualTo(0).And.IsLessThanOrEqualTo(PhotoMatcher.CandidateBits);
        await Assert.That(Difference(expected.Signature, tagged.Signature)).IsGreaterThanOrEqualTo(0).And.IsLessThanOrEqualTo(PhotoMatcher.SameDifference);
        using var thumbnail = new MagickImage(tagged.ThumbnailJpeg);
        await Assert.That((thumbnail.Width, thumbnail.Height)).IsEqualTo((320u, 240u));
    }

    [Test]
    public async Task A_Copy_Saved_In_Another_Colour_Space_Matches_The_Standard_One()
    {
        using var picture = Picture(5, 800, 600);
        var standard = await _analyzer.AnalyzeAsync(Encode(picture, MagickFormat.Png));

        // The same colours written as Adobe RGB numbers, with the profile that says so.
        using var converted = picture.Clone();
        converted.TransformColorSpace(ColorProfiles.SRGB, ColorProfiles.AdobeRGB1998);
        var rawNumbers = converted.GetPixels().ToByteArray(PixelMapping.RGB)!;
        var analysed = await _analyzer.AnalyzeAsync(Encode(converted, MagickFormat.Png));

        using var unconverted = new MagickImage(rawNumbers, new PixelReadSettings(800, 600, StorageType.Char, PixelMapping.RGB));
        var misread = await _analyzer.AnalyzeAsync(Encode(unconverted, MagickFormat.Png));
        await Assert.That(Difference(standard.Signature, analysed.Signature)).IsGreaterThanOrEqualTo(0).And.IsLessThanOrEqualTo(PhotoMatcher.SameDifference);
        await Assert.That(Difference(standard.Signature, misread.Signature) > PhotoMatcher.SimilarDifference).IsTrue().Because("the two colour spaces should differ visibly when the profile is ignored");
    }

    [Test]
    public async Task An_Image_That_Yields_No_Pixels_Is_Refused()
    {
        var pixels = new byte[] { 1, 2, 3 };
        await Assert.That(MagickImageAnalyzer.Required(pixels)).IsSameReferenceAs(pixels);
        await Assert.That(Assert.ThrowsExactly<InvalidOperationException>(() => MagickImageAnalyzer.Required(null)).Message).IsEqualTo("Image has no pixel data");
    }

    [Test]
    public async Task What_Is_Not_An_Image_Is_Refused()
    {
        await Assert.ThrowsAsync<MagickException>(() => _analyzer.AnalyzeAsync(new MemoryStream("not an image at all"u8.ToArray())));
        await Assert.ThrowsAsync<MagickException>(() => _analyzer.RenderJpegAsync(new MemoryStream([1, 2, 3, 4]), 100));
    }

    [Test]
    [Arguments(500, 500, 375)]
    [Arguments(5000, 1200, 900)] // never enlarged
    public async Task Renders_An_Upright_Jpeg_No_Larger_Than_Asked(int maxEdge, uint width, uint height)
    {
        using var picture = Picture(6);
        using var rendered = new MagickImage(await _analyzer.RenderJpegAsync(Encode(picture, MagickFormat.Png), maxEdge));
        await Assert.That((rendered.Format, rendered.Width, rendered.Height)).IsEqualTo((MagickFormat.Jpeg, width, height));
    }

    [Test]
    public async Task The_Hash_Sets_Half_Its_Bits_And_Ignores_Overall_Brightness()
    {
        using var picture = Picture(7, MagickImageAnalyzer.SignatureEdge, MagickImageAnalyzer.SignatureEdge);
        var rgb = picture.GetPixels().ToByteArray(PixelMapping.RGB)!;
        var brighter = rgb.Select(b => (byte)Math.Min(255, b / 2 + 60)).ToArray();
        var hash = MagickImageAnalyzer.PerceptualHash(rgb);
        await Assert.That(System.Numerics.BitOperations.PopCount(hash)).IsEqualTo(31); // terms above the median of 64
        await Assert.That(PhotoMatcher.HashDistance(hash, MagickImageAnalyzer.PerceptualHash(brighter))).IsGreaterThanOrEqualTo(0).And.IsLessThanOrEqualTo(4);
    }

    [Test]
    public async Task Capture_Details_Are_Read_From_The_File()
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

        await new BasicExifMetadataExtractor().ExtractAsync(photo, Encode(picture, MagickFormat.Jpeg));

        await Assert.That(photo.TakenOn).IsEqualTo(TestPhotos.Shot);
        await Assert.That(photo.TakenOn!.Value.Kind).IsEqualTo(DateTimeKind.Unspecified);
        await Assert.That(photo.CameraModel).IsEqualTo("iPhone 15 Pro Max");
        await Assert.That(Math.Round(photo.Latitude!.Value, 3)).IsEqualTo(35.225);
        await Assert.That(Math.Round(photo.Longitude!.Value, 3)).IsEqualTo(Math.Round(-80.8333, 3));
    }

    [Test]
    public async Task A_Position_Of_Zero_Zero_Is_Treated_As_No_Position()
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

        await new BasicExifMetadataExtractor().ExtractAsync(photo, Encode(picture, MagickFormat.Jpeg));

        await Assert.That(photo.TakenOn).IsEqualTo(new DateTime(2024, 4, 7, 17, 35, 59));
        await Assert.That(photo.Latitude).IsNull();
        await Assert.That(photo.Longitude).IsNull();
    }

    [Test]
    public async Task A_Picture_With_An_Unusable_Colour_Profile_Is_Read_As_It_Is()
    {
        using var picture = Picture(6, 400, 300);
        var plain = await _analyzer.AnalyzeAsync(Encode(picture, MagickFormat.Jpeg));

        // A profile that announces RGB and then holds nothing a conversion could be made from.
        var unusable = ColorProfiles.SRGB.ToByteArray()!;
        Array.Clear(unusable, 128, unusable.Length - 128);
        using var tagged = picture.Clone();
        tagged.SetProfile(new ColorProfile(unusable));
        var encoded = Encode(tagged, MagickFormat.Jpeg);
        using (var reread = new MagickImage(encoded.ToArray()))
            await Assert.That(reread.GetColorProfile()).IsNotNull();

        var analysed = await _analyzer.AnalyzeAsync(encoded);

        await Assert.That(Difference(plain.Signature, analysed.Signature)).IsGreaterThanOrEqualTo(0).And.IsLessThanOrEqualTo(PhotoMatcher.SameDifference);
        await Assert.That(analysed.PerceptualHash).IsEqualTo(plain.PerceptualHash);
    }
}
