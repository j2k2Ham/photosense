using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Entities;
using Xunit;
using static PhotoSense.Tests.TestPhotos;

namespace PhotoSense.Tests.Application;

public class PhotoRankingTests
{
    private static readonly PhotoRanking KeepOriginal = new(FormatPreference.CameraOriginal);
    private static readonly PhotoRanking KeepJpeg = new(FormatPreference.WidelyCompatible);

    private static Photo Best(PhotoRanking ranking, params Photo[] photos) => photos.OrderBy(p => p, ranking.BestFirst).First();
    private static Photo Best(params Photo[] photos) => Best(KeepOriginal, photos);

    [Fact]
    public void Higher_Resolution_Wins_Over_Everything_Else()
    {
        var full = Make("full.jpg", format: "JPEG", quality: 60, size: 900_000);
        var small = Make("small.png", format: "PNG", width: 1600, height: 1200, size: 5_000_000);
        Assert.Same(full, Best(small, full));
        Assert.Same(full, Best(KeepJpeg, small, full));
        Assert.Equal("Higher resolution: 4032×3024 vs 1600×1200", KeepOriginal.WhyKept(full, small));
    }

    [Fact]
    public void The_Cameras_Own_File_Beats_A_Larger_Conversion_Of_It_When_Originals_Are_Preferred()
    {
        // What an iPhone import leaves behind: the HEIC it shot and a JPEG conversion padded to twice the size.
        var heic = Make("IMG_4198.HEIC", size: 3_046_000, taken: Shot);
        var heif = Make("IMG_4199.HEIF", format: "HEIF", size: 3_046_000, taken: Shot);
        var jpeg = Make("IMG_4198.JPG", format: "JPEG", quality: 94, size: 6_093_000, taken: Shot);
        Assert.Same(heic, Best(jpeg, heic));
        Assert.Same(heif, Best(jpeg, heif));
        Assert.Equal("Camera original: HEIC rather than a JPEG conversion", KeepOriginal.WhyKept(heic, jpeg));
    }

    [Fact]
    public void The_Jpeg_Is_Kept_Instead_When_Files_Must_Open_Everywhere()
    {
        var heic = Make("IMG_4198.HEIC", size: 9_000_000, taken: Shot);
        var jpeg = Make("IMG_4198.JPG", format: "JPEG", quality: 94, size: 6_093_000, taken: Shot);
        var jpg = Make("IMG_4199.JPG", format: "JPG", quality: 94, size: 6_093_000, taken: Shot);
        Assert.Same(jpeg, Best(KeepJpeg, heic, jpeg));
        Assert.Same(jpg, Best(KeepJpeg, heic, jpg));
        Assert.Equal("Opens everywhere: JPEG rather than HEIC", KeepJpeg.WhyKept(jpeg, heic));

        // Resolution still comes first: a smaller JPEG does not displace the full-size HEIC.
        var smallJpeg = Make("small.JPG", format: "JPEG", quality: 94, width: 1600, height: 1200);
        Assert.Same(heic, Best(KeepJpeg, smallJpeg, heic));
    }

    [Fact]
    public void Unless_Told_Otherwise_The_Jpeg_Is_Kept()
    {
        var heic = Make("IMG_4198.HEIC", size: 3_046_000, taken: Shot);
        var jpeg = Make("IMG_4198.JPG", format: "JPEG", quality: 94, size: 6_093_000, taken: Shot);
        var unset = new PhotoRanking();
        Assert.Same(jpeg, Best(unset, heic, jpeg));
        Assert.Equal("Opens everywhere: JPEG rather than HEIC", unset.WhyKept(jpeg, heic));
        Assert.Equal(FormatPreference.WidelyCompatible, new PhotoStorageOptions().KeepFormat);
    }

    [Theory]
    [InlineData("PNG")]
    [InlineData("TIFF")]
    [InlineData("TIF")]
    [InlineData("BMP")]
    [InlineData("png")]
    public void A_Lossless_Copy_Outranks_Both_Whatever_The_Preference(string lossless)
    {
        var heic = Make("a.HEIC", taken: Shot);
        var jpeg = Make("a.JPG", format: "JPEG", quality: 94, taken: Shot);
        var exact = Make("a.x", format: lossless);
        Assert.Same(exact, Best(KeepOriginal, heic, jpeg, exact));
        Assert.Same(exact, Best(KeepJpeg, heic, jpeg, exact));
        Assert.Equal($"Lossless format: {lossless} rather than JPEG", KeepJpeg.WhyKept(exact, jpeg));
        Assert.Equal($"Lossless format: {lossless} rather than HEIC", KeepOriginal.WhyKept(exact, heic));
    }

    [Fact]
    public void A_File_Of_Unknown_Format_Ranks_Last_Among_Equals()
    {
        var unknown = Make("mystery.dat", size: 9_000_000); unknown.Format = null;
        var heic = Make("a.HEIC", size: 10);
        var jpeg = Make("a.JPG", format: "JPEG", size: 10);
        Assert.Same(heic, Best(KeepOriginal, unknown, heic));
        Assert.Same(jpeg, Best(KeepJpeg, unknown, jpeg));
    }

    [Fact]
    public void A_Copy_With_Its_Capture_Details_Beats_One_Without()
    {
        var intact = Make("a.jpg", format: "JPEG", quality: 80, taken: Shot);
        var stripped = Make("b.jpg", format: "JPEG", quality: 95, size: 2_000_000);
        Assert.Same(intact, Best(stripped, intact));
        Assert.Equal("Still has its capture date and details", KeepOriginal.WhyKept(intact, stripped));
    }

    [Fact]
    public void Less_Compression_Then_Larger_File_Decide_Between_Equal_Formats()
    {
        var fine = Make("fine.jpg", format: "JPEG", quality: 94, size: 1_000_000);
        var coarse = Make("coarse.jpg", format: "JPEG", quality: 50, size: 4_000_000);
        Assert.Same(fine, Best(coarse, fine));
        Assert.Equal("Less compressed: JPEG quality 94 vs 50", KeepOriginal.WhyKept(fine, coarse));

        var larger = Make("larger.jpg", format: "JPEG", quality: 94, size: 3_145_728);
        Assert.Same(larger, Best(fine, larger));
        Assert.Equal("Larger file: 3.0 MB vs 1.0 MB", KeepOriginal.WhyKept(larger, fine));

        // Same rank, one records a quality and one does not.
        var unrated = Make("unrated.webp", format: "WEBP", size: 9_000_000);
        var rated = Make("rated.webp", format: "WEBP", quality: 80, size: 10);
        Assert.Same(rated, Best(unrated, rated));
        Assert.Equal("Better-preserved copy at the same resolution", KeepOriginal.WhyKept(rated, unrated));
        Assert.Equal("Better-preserved copy at the same resolution", KeepOriginal.WhyKept(fine, Make("x.jpg", format: "JPEG")));
    }

    [Fact]
    public void Equal_Copies_Fall_Back_To_Folder_Then_Plainest_Name()
    {
        var primary = Make("IMG_0001 (1).HEIC", set: PhotoSet.Primary);
        var secondary = Make("IMG_0001.HEIC", set: PhotoSet.Secondary);
        var unknown = Make("IMG_0001.HEIC", folder: "other", set: PhotoSet.Unknown);
        Assert.Same(primary, Best(unknown, secondary, primary));
        Assert.Same(secondary, Best(unknown, secondary));
        Assert.Equal("Same quality; this one is in the primary folder", KeepOriginal.WhyKept(primary, secondary));

        var plain = Make("IMG_0001.HEIC");
        var copy = Make("IMG_0001 (1).HEIC");
        Assert.Same(plain, Best(copy, plain));
        Assert.Equal("Same quality; kept the one with the plainer name", KeepOriginal.WhyKept(plain, copy));

        var a = Make("IMG_0001.HEIC", folder: "a");
        var b = Make("IMG_0001.HEIC", folder: "b");
        Assert.Same(a, Best(b, a));
        Assert.Equal(0, KeepOriginal.BestFirst.Compare(a, a));
    }

    [Theory]
    [InlineData(FormatPreference.CameraOriginal)]
    [InlineData(FormatPreference.WidelyCompatible)]
    public void Ranking_Does_Not_Depend_On_The_Order_Photos_Arrive_In(FormatPreference preference)
    {
        var ranking = new PhotoRanking(preference);
        var photos = new[]
        {
            Make("a.heic"), Make("b.jpg", format: "JPEG", quality: 94), Make("c.jpg", format: "JPEG", quality: 50),
            Make("d.webp", format: "WEBP", size: 5_000_000), Make("e.png", format: "PNG", width: 800, height: 600),
            Make("f.jpg", format: "JPEG", quality: 94, taken: Shot), Make("g.gif", format: "GIF", size: 10)
        };
        var expected = photos.OrderBy(p => p, ranking.BestFirst).Select(p => p.FileName).ToList();
        var random = new Random(1);
        for (int i = 0; i < 20; i++)
        {
            var shuffled = photos.OrderBy(_ => random.Next()).ToList();
            Assert.Equal(expected, shuffled.OrderBy(p => p, ranking.BestFirst).Select(p => p.FileName));
        }
    }
}
