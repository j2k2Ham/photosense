using Moq;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.DTOs;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Infrastructure.Persistence;
using Xunit;
using static PhotoSense.Tests.TestPhotos;

namespace PhotoSense.Tests.Application;

public class DuplicateAnalysisServiceTests
{
    // Hashes far enough apart that the pictures are never compared.
    private const ulong PictureA = 0x0000000000000000, PictureB = 0xFFFFFFFF00000000, PictureC = 0x00000000FFFFFFFF;

    [Fact]
    public void A_Lone_Photo_Is_Not_A_Duplicate_Of_Anything()
    {
        var analysis = DuplicateAnalysisService.Analyze([Make("only.jpg")]);
        Assert.Empty(analysis.Duplicates);
        Assert.Empty(analysis.Similar);
    }

    [Fact]
    public void Identical_Files_Form_A_Group_Around_The_Plainest_Copy()
    {
        var copy = Make("IMG_2736a.JPG", contentHash: "SAME", size: 500);
        var plain = Make("IMG_2736.JPG", contentHash: "same", size: 500);
        var other = Make("other.jpg", hash: PictureB);
        var group = Assert.Single(DuplicateAnalysisService.Analyze([copy, other, plain]).Duplicates);
        Assert.Same(plain, group.Keeper);
        var member = Assert.Single(group.Members);
        Assert.Same(copy, member.Photo);
        Assert.Equal(MatchKind.Identical, member.Match);
        Assert.Equal(500, group.ReclaimableBytes);
        Assert.Equal(plain.Id.ToString(), group.Key);
    }

    [Fact]
    public void The_Other_Format_And_An_Identical_Twin_Join_The_Kept_Jpeg()
    {
        var heic = Make("IMG_4198.HEIC", taken: Shot, size: 3_000);
        var jpeg = Make("IMG_4198.JPG", contentHash: "J", taken: Shot, format: "JPEG", quality: 94, size: 6_000, signature: Signature(0.1));
        var jpegAgain = Make("IMG_4198 (1).JPG", contentHash: "J", taken: Shot, format: "JPEG", quality: 94, size: 6_000, signature: Signature(0.1));
        var group = Assert.Single(DuplicateAnalysisService.Analyze([jpegAgain, heic, jpeg]).Duplicates);
        Assert.Same(jpeg, group.Keeper);
        Assert.Equal(new[] { (jpegAgain, MatchKind.Identical), (heic, MatchKind.SamePicture) }, group.Members.Select(m => (m.Photo, m.Match)));
        Assert.Equal(9_000, group.ReclaimableBytes);
    }

    [Fact]
    public void Burst_Frames_Are_Offered_For_Review_Not_Removal()
    {
        var first = Make("IMG_6527.JPG", taken: Shot, format: "JPEG");
        var second = Make("IMG_6528.JPG", taken: Shot.AddMilliseconds(54), format: "JPEG");
        var third = Make("IMG_6529.JPG", taken: Shot.AddMilliseconds(166), format: "JPEG");
        var analysis = DuplicateAnalysisService.Analyze([third, first, second]);
        Assert.Empty(analysis.Duplicates);
        var group = Assert.Single(analysis.Similar);
        Assert.Same(first, group.Keeper);
        Assert.Equal(2, group.Members.Count);
        Assert.All(group.Members, m => Assert.Equal(MatchKind.Similar, m.Match));
    }

    [Fact]
    public void A_Photo_Already_Set_To_Go_With_Its_Original_Is_Not_Offered_As_A_Look_Alike()
    {
        var heic = Make("IMG_0001.HEIC", taken: Shot);
        var jpeg = Make("IMG_0001.JPG", taken: Shot, format: "JPEG");
        var burst = Make("IMG_0002.HEIC", taken: Shot.AddMilliseconds(100));
        var analysis = DuplicateAnalysisService.Analyze([jpeg, burst, heic]);
        Assert.Same(heic, Assert.Single(Assert.Single(analysis.Duplicates).Members).Photo);
        var similar = Assert.Single(analysis.Similar);
        Assert.Same(jpeg, similar.Keeper);
        Assert.Same(burst, Assert.Single(similar.Members).Photo);
    }

    [Fact]
    public void Every_Member_Was_Compared_With_The_Keeper_Itself()
    {
        // b matches both a and c, but a and c are too far apart to match each other.
        // Chaining through b would remove c on the strength of a photo that is itself being removed.
        var a = Make("a.heic", hash: 0x00, taken: Shot);
        var b = Make("b.jpg", hash: 0x3F, taken: Shot, format: "JPEG", width: 2016, height: 1512);
        var c = Make("c.jpg", hash: 0xFFF, taken: Shot, format: "JPEG", width: 1008, height: 756);
        var analysis = DuplicateAnalysisService.Analyze([c, b, a]);
        var group = Assert.Single(analysis.Duplicates);
        Assert.Same(a, group.Keeper);
        Assert.Same(b, Assert.Single(group.Members).Photo);
        Assert.DoesNotContain(analysis.Duplicates.SelectMany(g => g.Members), m => m.Photo == c);
    }

    [Fact]
    public void A_Copy_Marked_Keep_Stays_In_The_Group_But_Is_Not_Removable()
    {
        var keeper = Make("IMG_0001.JPG", taken: Shot, format: "JPEG", quality: 95);
        var keptCopy = Make("IMG_0001 resaved.JPG", taken: Shot, format: "JPEG", quality: 80, size: 700, kept: true);
        var copy = Make("IMG_0001 small.JPG", taken: Shot, format: "JPEG", width: 1600, height: 1200, size: 300);
        var group = Assert.Single(DuplicateAnalysisService.Analyze([keptCopy, copy, keeper]).Duplicates);
        Assert.Same(keeper, group.Keeper);
        Assert.Equal(2, group.Members.Count);
        Assert.Same(copy, Assert.Single(group.Removable));
        Assert.Equal(300, group.ReclaimableBytes);
    }

    [Fact]
    public void Groups_Freeing_The_Most_Space_Come_First_And_Unhashed_Photos_Are_Ignored()
    {
        var smallA = Make("a1.jpg", contentHash: "A", hash: PictureA, size: 10);
        var smallB = Make("a2.jpg", contentHash: "A", hash: PictureA, size: 10);
        var bigA = Make("b1.jpg", contentHash: "B", hash: PictureB, size: 9_000);
        var bigB = Make("b2.jpg", contentHash: "B", hash: PictureB, size: 9_000);
        var unhashed = Make("c.jpg", hash: PictureC); unhashed.ContentHash = null;
        var blank = Make("d.jpg", hash: PictureC); blank.ContentHash = " ";
        var analysis = DuplicateAnalysisService.Analyze([smallA, smallB, unhashed, blank, bigA, bigB]);
        Assert.Equal(new long[] { 9_000, 10 }, analysis.Duplicates.Select(g => g.ReclaimableBytes));
    }

    [Fact]
    public void Undecodable_Files_Are_Still_Matched_When_Identical()
    {
        var a = Make("broken.heic", contentHash: "X"); a.Signature = null; a.PerceptualHash = null;
        var b = Make("broken (1).heic", contentHash: "X"); b.Signature = null; b.PerceptualHash = null;
        var decoded = Make("fine.heic");
        var group = Assert.Single(DuplicateAnalysisService.Analyze([a, b, decoded]).Duplicates);
        Assert.Same(a, group.Keeper);
    }

    [Fact]
    public void Identical_Videos_Form_A_Group_And_Unread_Ones_Are_Left_Out()
    {
        var first = Video("clip.MOV", folder: "a", contentHash: "V");
        var second = Video("clip.MOV", folder: "b", contentHash: "V");
        var unread = Video("other.MOV"); // its size was shared with nothing, so it was never hashed
        var analysis = DuplicateAnalysisService.Analyze([second, unread, first]);
        var group = Assert.Single(analysis.Duplicates);
        Assert.Same(first, group.Keeper);
        Assert.Equal((second, MatchKind.Identical), (Assert.Single(group.Members).Photo, group.Members[0].Match));
        Assert.Empty(analysis.Similar);
    }

    [Fact]
    public void A_Live_Photos_Video_Travels_With_Its_Picture_And_Is_Not_Matched_On_Its_Own()
    {
        // One Live Photo imported twice: once as HEIC, once converted to JPEG, each with the same video.
        var heic = Make("IMG_1.HEIC", folder: "a", taken: Shot); heic.LivePhotoId = "LIVE-1";
        var jpeg = Make("IMG_1.JPG", folder: "b", taken: Shot, format: "JPEG"); jpeg.LivePhotoId = "LIVE-1";
        var videoA = Video("IMG_1.MOV", folder: "a", contentHash: "M", livePhotoId: "live-1");
        var videoB = Video("IMG_1.MOV", folder: "b", contentHash: "M", livePhotoId: "LIVE-1");

        var analysis = DuplicateAnalysisService.Analyze([videoA, heic, videoB, jpeg]);

        // Matched on their own, one video would be removed as a copy of the other, and the other would
        // then leave with the HEIC it belongs to: both gone.
        var group = Assert.Single(analysis.Duplicates);
        Assert.Same(jpeg, group.Keeper);
        Assert.Same(heic, Assert.Single(group.Members).Photo);
    }

    [Fact]
    public void A_Video_Without_Its_Picture_Beside_It_Is_Matched_Like_Any_Other_File()
    {
        var picture = Make("IMG_1.HEIC", folder: "a", taken: Shot); picture.LivePhotoId = "LIVE-1";
        var itsVideo = Video("IMG_1.MOV", folder: "a", contentHash: "M", livePhotoId: "LIVE-1");
        var strayCopy = Video("IMG_1.MOV", folder: "elsewhere", contentHash: "M", livePhotoId: "LIVE-1");
        var strayCopyAgain = Video("IMG_1 (1).MOV", folder: "elsewhere", contentHash: "M", livePhotoId: "LIVE-1");
        // Same name, same folder as the picture, but another Live Photo's identifier: not this picture's video.
        var unrelated = Video("IMG_1.MOV", folder: "b", contentHash: "U", livePhotoId: "OTHER");
        var besideUnrelated = Make("IMG_1.JPG", folder: "b", contentHash: "P2", hash: PictureB); besideUnrelated.LivePhotoId = "LIVE-9";
        var unrelatedCopy = Video("copy.MOV", folder: "b", contentHash: "U", livePhotoId: "OTHER");

        var analysis = DuplicateAnalysisService.Analyze([picture, itsVideo, strayCopy, strayCopyAgain, unrelated, besideUnrelated, unrelatedCopy]);

        Assert.Equal(2, analysis.Duplicates.Count);
        var strays = analysis.Duplicates.Single(g => g.Keeper.ContentHash == "M");
        Assert.Equal((strayCopy, strayCopyAgain), (strays.Keeper, Assert.Single(strays.Members).Photo));
        var others = analysis.Duplicates.Single(g => g.Keeper.ContentHash == "U");
        Assert.Equal(2, others.Members.Count + 1);
        Assert.DoesNotContain(analysis.Duplicates.SelectMany(g => g.Members.Select(m => m.Photo).Append(g.Keeper)), p => p == itsVideo);
    }

    [Fact]
    public async Task Analysis_Is_Reused_Until_The_Photos_Change()
    {
        var repo = new InMemoryPhotoRepository();
        await repo.AddOrUpdateAsync(Make("a.jpg", contentHash: "A"));
        await repo.AddOrUpdateAsync(Make("b.jpg", contentHash: "A"));
        var service = new DuplicateAnalysisService(repo);

        var first = await service.GetAsync();
        Assert.Single(first.Duplicates);
        Assert.Same(first, await service.GetAsync());

        await repo.DeleteAsync(first.Duplicates[0].Members[0].Photo.Id);
        var afterChange = await service.GetAsync();
        Assert.NotSame(first, afterChange);
        Assert.Empty(afterChange.Duplicates);
    }

    [Fact]
    public async Task Reads_The_Photos_Once_Per_Version()
    {
        var repo = new Mock<IPhotoRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Photo>());
        var service = new DuplicateAnalysisService(repo.Object);
        await service.GetAsync();
        await service.GetAsync();
        repo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
