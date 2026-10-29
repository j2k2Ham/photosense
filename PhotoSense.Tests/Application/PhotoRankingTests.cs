using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Entities;
using static PhotoSense.Tests.TestPhotos;

namespace PhotoSense.Tests.Application;

public class PhotoRankingTests
{
    private static readonly PhotoRanking KeepOriginal = new(FormatPreference.CameraOriginal);
    private static readonly PhotoRanking KeepJpeg = new(FormatPreference.WidelyCompatible);

    private static Photo Best(PhotoRanking ranking, params Photo[] photos) => photos.OrderBy(p => p, ranking.BestFirst).First();
    private static Photo Best(params Photo[] photos) => Best(KeepOriginal, photos);

    [Test]
    public async Task Higher_Resolution_Wins_Over_Everything_Else()
    {
        var full = Make("full.jpg", format: "JPEG", quality: 60, size: 900_000);
        var small = Make("small.png", format: "PNG", width: 1600, height: 1200, size: 5_000_000);
        await Assert.That(Best(small, full)).IsSameReferenceAs(full);
        await Assert.That(Best(KeepJpeg, small, full)).IsSameReferenceAs(full);
        await Assert.That(KeepOriginal.WhyKept(full, small)).IsEqualTo("Higher resolution: 4032×3024 vs 1600×1200");
    }

    [Test]
    public async Task The_Cameras_Own_File_Beats_A_Larger_Conversion_Of_It_When_Originals_Are_Preferred()
    {
        // What an iPhone import leaves behind: the HEIC it shot and a JPEG conversion padded to twice the size.
        var heic = Make("IMG_4198.HEIC", size: 3_046_000, taken: Shot);
        var heif = Make("IMG_4199.HEIF", format: "HEIF", size: 3_046_000, taken: Shot);
        var jpeg = Make("IMG_4198.JPG", format: "JPEG", quality: 94, size: 6_093_000, taken: Shot);
        await Assert.That(Best(jpeg, heic)).IsSameReferenceAs(heic);
        await Assert.That(Best(jpeg, heif)).IsSameReferenceAs(heif);
        await Assert.That(KeepOriginal.WhyKept(heic, jpeg)).IsEqualTo("Camera original: HEIC rather than a JPEG conversion");
    }

    [Test]
    public async Task The_Jpeg_Is_Kept_Instead_When_Files_Must_Open_Everywhere()
    {
        var heic = Make("IMG_4198.HEIC", size: 9_000_000, taken: Shot);
        var jpeg = Make("IMG_4198.JPG", format: "JPEG", quality: 94, size: 6_093_000, taken: Shot);
        var jpg = Make("IMG_4199.JPG", format: "JPG", quality: 94, size: 6_093_000, taken: Shot);
        await Assert.That(Best(KeepJpeg, heic, jpeg)).IsSameReferenceAs(jpeg);
        await Assert.That(Best(KeepJpeg, heic, jpg)).IsSameReferenceAs(jpg);
        await Assert.That(KeepJpeg.WhyKept(jpeg, heic)).IsEqualTo("Opens everywhere: JPEG rather than HEIC");

        // Resolution still comes first: a smaller JPEG does not displace the full-size HEIC.
        var smallJpeg = Make("small.JPG", format: "JPEG", quality: 94, width: 1600, height: 1200);
        await Assert.That(Best(KeepJpeg, smallJpeg, heic)).IsSameReferenceAs(heic);
    }

    [Test]
    public async Task Unless_Told_Otherwise_The_Jpeg_Is_Kept()
    {
        var heic = Make("IMG_4198.HEIC", size: 3_046_000, taken: Shot);
        var jpeg = Make("IMG_4198.JPG", format: "JPEG", quality: 94, size: 6_093_000, taken: Shot);
        var unset = new PhotoRanking();
        await Assert.That(Best(unset, heic, jpeg)).IsSameReferenceAs(jpeg);
        await Assert.That(unset.WhyKept(jpeg, heic)).IsEqualTo("Opens everywhere: JPEG rather than HEIC");
        await Assert.That(new PhotoStorageOptions().KeepFormat).IsEqualTo(FormatPreference.WidelyCompatible);
    }

    [Test]
    [Arguments("PNG")]
    [Arguments("TIFF")]
    [Arguments("TIF")]
    [Arguments("BMP")]
    [Arguments("png")]
    public async Task A_Lossless_Copy_Outranks_Both_Whatever_The_Preference(string lossless)
    {
        var heic = Make("a.HEIC", taken: Shot);
        var jpeg = Make("a.JPG", format: "JPEG", quality: 94, taken: Shot);
        var exact = Make("a.x", format: lossless);
        await Assert.That(Best(KeepOriginal, heic, jpeg, exact)).IsSameReferenceAs(exact);
        await Assert.That(Best(KeepJpeg, heic, jpeg, exact)).IsSameReferenceAs(exact);
        await Assert.That(KeepJpeg.WhyKept(exact, jpeg)).IsEqualTo($"Lossless format: {lossless} rather than JPEG");
        await Assert.That(KeepOriginal.WhyKept(exact, heic)).IsEqualTo($"Lossless format: {lossless} rather than HEIC");
    }

    [Test]
    public async Task A_File_Of_Unknown_Format_Ranks_Last_Among_Equals()
    {
        var unknown = Make("mystery.dat", size: 9_000_000); unknown.Format = null;
        var heic = Make("a.HEIC", size: 10);
        var jpeg = Make("a.JPG", format: "JPEG", size: 10);
        await Assert.That(Best(KeepOriginal, unknown, heic)).IsSameReferenceAs(heic);
        await Assert.That(Best(KeepJpeg, unknown, jpeg)).IsSameReferenceAs(jpeg);
    }

    [Test]
    public async Task A_Copy_With_Its_Capture_Details_Beats_One_Without()
    {
        var intact = Make("a.jpg", format: "JPEG", quality: 80, taken: Shot);
        var stripped = Make("b.jpg", format: "JPEG", quality: 95, size: 2_000_000);
        await Assert.That(Best(stripped, intact)).IsSameReferenceAs(intact);
        await Assert.That(KeepOriginal.WhyKept(intact, stripped)).IsEqualTo("Still has its capture date and details");
    }

    [Test]
    public async Task Less_Compression_Then_Larger_File_Decide_Between_Equal_Formats()
    {
        var fine = Make("fine.jpg", format: "JPEG", quality: 94, size: 1_000_000);
        var coarse = Make("coarse.jpg", format: "JPEG", quality: 50, size: 4_000_000);
        await Assert.That(Best(coarse, fine)).IsSameReferenceAs(fine);
        await Assert.That(KeepOriginal.WhyKept(fine, coarse)).IsEqualTo("Less compressed: JPEG quality 94 vs 50");

        var larger = Make("larger.jpg", format: "JPEG", quality: 94, size: 3_145_728);
        await Assert.That(Best(fine, larger)).IsSameReferenceAs(larger);
        await Assert.That(KeepOriginal.WhyKept(larger, fine)).IsEqualTo("Larger file: 3.0 MB vs 1.0 MB");

        // Same rank, one records a quality and one does not.
        var unrated = Make("unrated.webp", format: "WEBP", size: 9_000_000);
        var rated = Make("rated.webp", format: "WEBP", quality: 80, size: 10);
        await Assert.That(Best(unrated, rated)).IsSameReferenceAs(rated);
        await Assert.That(KeepOriginal.WhyKept(rated, unrated)).IsEqualTo("Better-preserved copy at the same resolution");
        await Assert.That(KeepOriginal.WhyKept(fine, Make("x.jpg", format: "JPEG"))).IsEqualTo("Better-preserved copy at the same resolution");
    }

    [Test]
    public async Task Equal_Copies_Fall_Back_To_Folder_Then_Plainest_Name()
    {
        var primary = Make("IMG_0001 (1).HEIC", set: PhotoSet.Primary);
        var secondary = Make("IMG_0001.HEIC", set: PhotoSet.Secondary);
        var unknown = Make("IMG_0001.HEIC", folder: "other", set: PhotoSet.Unknown);
        await Assert.That(Best(unknown, secondary, primary)).IsSameReferenceAs(primary);
        await Assert.That(Best(unknown, secondary)).IsSameReferenceAs(secondary);
        await Assert.That(KeepOriginal.WhyKept(primary, secondary)).IsEqualTo("Same quality; this one is in the primary folder");

        var plain = Make("IMG_0001.HEIC");
        var copy = Make("IMG_0001 (1).HEIC");
        await Assert.That(Best(copy, plain)).IsSameReferenceAs(plain);
        await Assert.That(KeepOriginal.WhyKept(plain, copy)).IsEqualTo("Same quality; kept the one with the plainer name");

        var a = Make("IMG_0001.HEIC", folder: "a");
        var b = Make("IMG_0001.HEIC", folder: "b");
        await Assert.That(Best(b, a)).IsSameReferenceAs(a);
        await Assert.That(KeepOriginal.BestFirst.Compare(a, a)).IsEqualTo(0);
    }

    [Test]
    [Arguments(FormatPreference.CameraOriginal)]
    [Arguments(FormatPreference.WidelyCompatible)]
    public async Task Ranking_Does_Not_Depend_On_The_Order_Photos_Arrive_In(FormatPreference preference)
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
            await Assert.That(shuffled.OrderBy(p => p, ranking.BestFirst).Select(p => p.FileName)).IsEquivalentTo(expected, CollectionOrdering.Matching);
        }
    }
}
