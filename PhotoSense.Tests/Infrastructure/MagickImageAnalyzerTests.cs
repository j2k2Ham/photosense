using ImageMagick;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Entities;
using PhotoSense.Infrastructure.Imaging;
using PhotoSense.Infrastructure.Metadata;
using Xunit;

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

    [Fact]
    public async Task Measures_A_Jpeg()
    {
        using var picture = Picture(1);
        var analysis = await _analyzer.AnalyzeAsync(Encode(picture, MagickFormat.Jpeg, 85));

        Assert.Equal((1200, 900, "JPEG", 85), (analysis.Width, analysis.Height, analysis.Format, analysis.EncodedQuality));
        Assert.Equal(MagickImageAnalyzer.SignatureEdge * MagickImageAnalyzer.SignatureEdge * 3, analysis.Signature.Length);
        Assert.Equal(analysis.PerceptualHash, MagickImageAnalyzer.PerceptualHash(analysis.Signature));
        using var thumbnail = new MagickImage(analysis.ThumbnailJpeg);
        Assert.Equal((MagickFormat.Jpeg, 320u, 240u), (thumbnail.Format, thumbnail.Width, thumbnail.Height));
    }

    [Fact]
    public async Task Formats_Without_A_Quality_Setting_Report_None_And_Small_Images_Are_Not_Enlarged()
    {
        using var picture = Picture(2, 200, 100);
        var analysis = await _analyzer.AnalyzeAsync(Encode(picture, MagickFormat.Png));
        Assert.Equal((200, 100, "PNG", (int?)null), (analysis.Width, analysis.Height, analysis.Format, analysis.EncodedQuality));
        using var thumbnail = new MagickImage(analysis.ThumbnailJpeg);
        Assert.Equal((200u, 100u), (thumbnail.Width, thumbnail.Height));
    }

    [Fact]
    public async Task A_Resized_Recompressed_Copy_In_Another_Format_Matches_Its_Original()
    {
        using var picture = Picture(3, 2400, 1800);
        var original = await _analyzer.AnalyzeAsync(Encode(picture, MagickFormat.Png));
        using var small = picture.Clone();
        small.Resize(600, 450);
        var copy = await _analyzer.AnalyzeAsync(Encode(small, MagickFormat.Jpeg, 80));

        Assert.InRange(PhotoMatcher.HashDistance(original.PerceptualHash, copy.PerceptualHash), 0, PhotoMatcher.CandidateBits);
        Assert.InRange(Difference(original.Signature, copy.Signature), 0, PhotoMatcher.SameDifference);
    }

    [Fact]
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
                Assert.True(PhotoMatcher.HashDistance(hashes[i], hashes[j]) > PhotoMatcher.CandidateBits, $"pictures {i} and {j} hash too close");
    }

    [Fact]
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

        Assert.Equal((800, 600), (tagged.Width, tagged.Height));
        Assert.InRange(PhotoMatcher.HashDistance(expected.PerceptualHash, tagged.PerceptualHash), 0, PhotoMatcher.CandidateBits);
        Assert.InRange(Difference(expected.Signature, tagged.Signature), 0, PhotoMatcher.SameDifference);
        using var thumbnail = new MagickImage(tagged.ThumbnailJpeg);
        Assert.Equal((320u, 240u), (thumbnail.Width, thumbnail.Height));
    }

    [Fact]
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
        Assert.InRange(Difference(standard.Signature, analysed.Signature), 0, PhotoMatcher.SameDifference);
        Assert.True(Difference(standard.Signature, misread.Signature) > PhotoMatcher.SimilarDifference, "the two colour spaces should differ visibly when the profile is ignored");
    }

    [Fact]
    public async Task What_Is_Not_An_Image_Is_Refused()
    {
        await Assert.ThrowsAnyAsync<MagickException>(() => _analyzer.AnalyzeAsync(new MemoryStream("not an image at all"u8.ToArray())));
        await Assert.ThrowsAnyAsync<MagickException>(() => _analyzer.RenderJpegAsync(new MemoryStream([1, 2, 3, 4]), 100));
    }

    [Theory]
    [InlineData(500, 500, 375)]
    [InlineData(5000, 1200, 900)] // never enlarged
    public async Task Renders_An_Upright_Jpeg_No_Larger_Than_Asked(int maxEdge, uint width, uint height)
    {
        using var picture = Picture(6);
        using var rendered = new MagickImage(await _analyzer.RenderJpegAsync(Encode(picture, MagickFormat.Png), maxEdge));
        Assert.Equal((MagickFormat.Jpeg, width, height), (rendered.Format, rendered.Width, rendered.Height));
    }

    [Fact]
    public void The_Hash_Sets_Half_Its_Bits_And_Ignores_Overall_Brightness()
    {
        using var picture = Picture(7, MagickImageAnalyzer.SignatureEdge, MagickImageAnalyzer.SignatureEdge);
        var rgb = picture.GetPixels().ToByteArray(PixelMapping.RGB)!;
        var brighter = rgb.Select(b => (byte)Math.Min(255, b / 2 + 60)).ToArray();
        var hash = MagickImageAnalyzer.PerceptualHash(rgb);
        Assert.Equal(31, System.Numerics.BitOperations.PopCount(hash)); // terms above the median of 64
        Assert.InRange(PhotoMatcher.HashDistance(hash, MagickImageAnalyzer.PerceptualHash(brighter)), 0, 4);
    }

    [Fact]
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

        Assert.Equal(TestPhotos.Shot, photo.TakenOn);
        Assert.Equal(DateTimeKind.Unspecified, photo.TakenOn!.Value.Kind);
        Assert.Equal("iPhone 15 Pro Max", photo.CameraModel);
        Assert.Equal(35.225, photo.Latitude!.Value, 3);
        Assert.Equal(-80.8333, photo.Longitude!.Value, 3);
    }

    [Fact]
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

        Assert.Equal(new DateTime(2024, 4, 7, 17, 35, 59), photo.TakenOn);
        Assert.Null(photo.Latitude);
        Assert.Null(photo.Longitude);
    }
}
