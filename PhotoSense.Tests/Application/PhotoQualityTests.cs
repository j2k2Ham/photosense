using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Entities;
using Xunit;
using static PhotoSense.Tests.TestPhotos;

namespace PhotoSense.Tests.Application;

public class PhotoQualityTests
{
    private static Photo Best(params Photo[] photos) => photos.OrderBy(p => p, PhotoQuality.BestFirst).First();

    [Fact]
    public void Higher_Resolution_Wins_Over_Everything_Else()
    {
        var full = Make("full.jpg", format: "JPEG", quality: 60, size: 900_000);
        var small = Make("small.png", format: "PNG", width: 1600, height: 1200, size: 5_000_000);
        Assert.Same(full, Best(small, full));
        Assert.Equal("Higher resolution: 4032×3024 vs 1600×1200", PhotoQuality.WhyKept(full, small));
    }

    [Fact]
    public void A_Jpeg_Is_Preferred_To_A_Heic_Of_The_Same_Shot_Because_It_Opens_Everywhere()
    {
        // What an iPhone import leaves behind: the HEIC it shot and a JPEG conversion of it.
        var heic = Make("IMG_4198.HEIC", size: 9_000_000, taken: Shot);
        var jpeg = Make("IMG_4198.JPG", format: "JPEG", quality: 94, size: 6_093_000, taken: Shot);
        Assert.Same(jpeg, Best(heic, jpeg));
        Assert.Equal("Opens everywhere: JPEG rather than HEIC", PhotoQuality.WhyKept(jpeg, heic));

        // Resolution still comes first: a smaller JPEG does not displace the full-size HEIC.
        var smallJpeg = Make("small.JPG", format: "JPEG", quality: 94, width: 1600, height: 1200);
        Assert.Same(heic, Best(smallJpeg, heic));

        // And a lossless copy outranks both.
        var png = Make("IMG_4198.PNG", format: "PNG");
        Assert.Same(png, Best(heic, jpeg, png));
        Assert.Equal("Lossless format: PNG rather than JPEG", PhotoQuality.WhyKept(png, jpeg));
    }

    [Fact]
    public void A_Copy_With_Its_Capture_Details_Beats_One_Without()
    {
        var intact = Make("a.jpg", format: "JPEG", quality: 80, taken: Shot);
        var stripped = Make("b.jpg", format: "JPEG", quality: 95, size: 2_000_000);
        Assert.Same(intact, Best(stripped, intact));
        Assert.Equal("Still has its capture date and details", PhotoQuality.WhyKept(intact, stripped));
    }

    [Fact]
    public void Less_Compression_Then_Larger_File_Decide_Between_Equal_Formats()
    {
        var fine = Make("fine.jpg", format: "JPEG", quality: 94, size: 1_000_000);
        var coarse = Make("coarse.jpg", format: "JPEG", quality: 50, size: 4_000_000);
        Assert.Same(fine, Best(coarse, fine));
        Assert.Equal("Less compressed: JPEG quality 94 vs 50", PhotoQuality.WhyKept(fine, coarse));

        var larger = Make("larger.jpg", format: "JPEG", quality: 94, size: 3_145_728);
        Assert.Same(larger, Best(fine, larger));
        Assert.Equal("Larger file: 3.0 MB vs 1.0 MB", PhotoQuality.WhyKept(larger, fine));

        var unrated = Make("unrated.webp", format: "WEBP", size: 9_000_000);
        Assert.Same(fine, Best(unrated, fine));
        Assert.Equal("Opens everywhere: JPEG rather than WEBP", PhotoQuality.WhyKept(fine, unrated));

        // Same rank, one records a quality and one does not.
        var rated = Make("rated.webp", format: "WEBP", quality: 80, size: 10);
        Assert.Same(rated, Best(unrated, rated));
        Assert.Equal("Better-preserved copy at the same resolution", PhotoQuality.WhyKept(rated, unrated));
    }

    [Fact]
    public void Equal_Copies_Fall_Back_To_Folder_Then_Plainest_Name()
    {
        var primary = Make("IMG_0001 (1).HEIC", set: PhotoSet.Primary);
        var secondary = Make("IMG_0001.HEIC", set: PhotoSet.Secondary);
        var unknown = Make("IMG_0001.HEIC", folder: "other", set: PhotoSet.Unknown);
        Assert.Same(primary, Best(unknown, secondary, primary));
        Assert.Equal("Same quality; this one is in the primary folder", PhotoQuality.WhyKept(primary, secondary));

        var plain = Make("IMG_0001.HEIC");
        var copy = Make("IMG_0001 (1).HEIC");
        Assert.Same(plain, Best(copy, plain));
        Assert.Equal("Same quality; kept the one with the plainer name", PhotoQuality.WhyKept(plain, copy));

        var a = Make("IMG_0001.HEIC", folder: "a");
        var b = Make("IMG_0001.HEIC", folder: "b");
        Assert.Same(a, Best(b, a));
    }

    [Fact]
    public void Ranking_Does_Not_Depend_On_The_Order_Photos_Arrive_In()
    {
        var photos = new[]
        {
            Make("a.heic"), Make("b.jpg", format: "JPEG", quality: 94), Make("c.jpg", format: "JPEG", quality: 50),
            Make("d.webp", format: "WEBP", size: 5_000_000), Make("e.png", format: "PNG", width: 800, height: 600),
            Make("f.jpg", format: "JPEG", quality: 94, taken: Shot), Make("g.gif", format: "GIF", size: 10)
        };
        var expected = photos.OrderBy(p => p, PhotoQuality.BestFirst).Select(p => p.FileName).ToList();
        var random = new Random(1);
        for (int i = 0; i < 20; i++)
        {
            var shuffled = photos.OrderBy(_ => random.Next()).ToList();
            Assert.Equal(expected, shuffled.OrderBy(p => p, PhotoQuality.BestFirst).Select(p => p.FileName));
        }
    }
}
