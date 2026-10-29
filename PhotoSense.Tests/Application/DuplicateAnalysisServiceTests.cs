using Moq;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.DTOs;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Infrastructure.Persistence;
using static PhotoSense.Tests.TestPhotos;

namespace PhotoSense.Tests.Application;

public class DuplicateAnalysisServiceTests
{
    private static readonly PhotoRanking KeepJpeg = new(FormatPreference.WidelyCompatible);

    // Hashes far enough apart that the pictures are never compared.
    private const ulong PictureA = 0x0000000000000000, PictureB = 0xFFFFFFFF00000000, PictureC = 0x00000000FFFFFFFF;

    [Test]
    public async Task Pictures_With_The_Same_Hash_But_Another_Shape_Are_Not_Grouped()
    {
        // Close enough by hash to be compared, and then told apart: one is upright, the other on its side.
        var analysis = DuplicateAnalysisService.Analyze([Make("landscape.jpg"), Make("portrait.jpg", width: 3024, height: 4032)]);
        await Assert.That(analysis.Duplicates).IsEmpty();
        await Assert.That(analysis.Similar).IsEmpty();
    }

    [Test]
    public async Task A_Lone_Photo_Is_Not_A_Duplicate_Of_Anything()
    {
        var analysis = DuplicateAnalysisService.Analyze([Make("only.jpg")]);
        await Assert.That(analysis.Duplicates).IsEmpty();
        await Assert.That(analysis.Similar).IsEmpty();
    }

    [Test]
    public async Task Identical_Files_Form_A_Group_Around_The_Plainest_Copy()
    {
        var copy = Make("IMG_2736a.JPG", contentHash: "SAME", size: 500);
        var plain = Make("IMG_2736.JPG", contentHash: "same", size: 500);
        var other = Make("other.jpg", hash: PictureB);
        var group = DuplicateAnalysisService.Analyze([copy, other, plain]).Duplicates.Single();
        await Assert.That(group.Keeper).IsSameReferenceAs(plain);
        var member = group.Members.Single();
        await Assert.That(member.Photo).IsSameReferenceAs(copy);
        await Assert.That(member.Match).IsEqualTo(MatchKind.Identical);
        await Assert.That(group.ReclaimableBytes).IsEqualTo(500);
        await Assert.That(group.Key).IsEqualTo(plain.Id.ToString());
    }

    [Test]
    public async Task The_Other_Format_And_An_Identical_Twin_Join_The_Kept_Jpeg()
    {
        var heic = Make("IMG_4198.HEIC", taken: Shot, size: 3_000);
        var jpeg = Make("IMG_4198.JPG", contentHash: "J", taken: Shot, format: "JPEG", quality: 94, size: 6_000, signature: Signature(0.1));
        var jpegAgain = Make("IMG_4198 (1).JPG", contentHash: "J", taken: Shot, format: "JPEG", quality: 94, size: 6_000, signature: Signature(0.1));
        // No preference given: the JPEG is the one kept.
        var group = DuplicateAnalysisService.Analyze([jpegAgain, heic, jpeg]).Duplicates.Single();
        await Assert.That(group.Keeper).IsSameReferenceAs(jpeg);
        await Assert.That(group.Members.Select(m => (m.Photo, m.Match))).IsEquivalentTo(new[] { (jpegAgain, MatchKind.Identical), (heic, MatchKind.SamePicture) }, EqualityComparer<(Photo, MatchKind)>.Default, CollectionOrdering.Matching);
        await Assert.That(group.ReclaimableBytes).IsEqualTo(9_000);
    }

    [Test]
    public async Task When_Originals_Are_Preferred_The_Camera_Original_Is_Kept_And_Its_Conversions_Go()
    {
        var heic = Make("IMG_4198.HEIC", taken: Shot, size: 3_000);
        var jpeg = Make("IMG_4198.JPG", contentHash: "J", taken: Shot, format: "JPEG", quality: 94, size: 6_000, signature: Signature(0.1));
        var jpegAgain = Make("IMG_4198 (1).JPG", contentHash: "J", taken: Shot, format: "JPEG", quality: 94, size: 6_000, signature: Signature(0.1));
        var group = DuplicateAnalysisService.Analyze([jpegAgain, jpeg, heic], new PhotoRanking(FormatPreference.CameraOriginal)).Duplicates.Single();
        await Assert.That(group.Keeper).IsSameReferenceAs(heic);
        await Assert.That(group.Members.Select(m => m.Photo)).IsEquivalentTo(new[] { jpeg, jpegAgain }, EqualityComparer<Photo>.Default, CollectionOrdering.Matching);
        foreach (var m in group.Members) await Assert.That(m.Match).IsEqualTo(MatchKind.SamePicture);
        await Assert.That(group.ReclaimableBytes).IsEqualTo(12_000);
    }

    [Test]
    public async Task Burst_Frames_Are_Offered_For_Review_Not_Removal()
    {
        var first = Make("IMG_6527.JPG", taken: Shot, format: "JPEG");
        var second = Make("IMG_6528.JPG", taken: Shot.AddMilliseconds(54), format: "JPEG");
        var third = Make("IMG_6529.JPG", taken: Shot.AddMilliseconds(166), format: "JPEG");
        var analysis = DuplicateAnalysisService.Analyze([third, first, second]);
        await Assert.That(analysis.Duplicates).IsEmpty();
        var group = analysis.Similar.Single();
        await Assert.That(group.Keeper).IsSameReferenceAs(first);
        await Assert.That(group.Members.Count).IsEqualTo(2);
        foreach (var m in group.Members) await Assert.That(m.Match).IsEqualTo(MatchKind.Similar);
    }

    [Test]
    public async Task A_Photo_Already_Set_To_Go_With_Its_Original_Is_Not_Offered_As_A_Look_Alike()
    {
        var heic = Make("IMG_0001.HEIC", taken: Shot);
        var jpeg = Make("IMG_0001.JPG", taken: Shot, format: "JPEG");
        var burst = Make("IMG_0002.HEIC", taken: Shot.AddMilliseconds(100));
        var analysis = DuplicateAnalysisService.Analyze([jpeg, burst, heic], KeepJpeg);
        await Assert.That(analysis.Duplicates.Single().Members.Single().Photo).IsSameReferenceAs(heic);
        var similar = analysis.Similar.Single();
        await Assert.That(similar.Keeper).IsSameReferenceAs(jpeg);
        await Assert.That(similar.Members.Single().Photo).IsSameReferenceAs(burst);
    }

    [Test]
    public async Task Every_Member_Was_Compared_With_The_Keeper_Itself()
    {
        // b matches both a and c, but a and c are too far apart to match each other.
        // Chaining through b would remove c on the strength of a photo that is itself being removed.
        var a = Make("a.heic", hash: 0x00, taken: Shot);
        var b = Make("b.jpg", hash: 0x3F, taken: Shot, format: "JPEG", width: 2016, height: 1512);
        var c = Make("c.jpg", hash: 0xFFF, taken: Shot, format: "JPEG", width: 1008, height: 756);
        var analysis = DuplicateAnalysisService.Analyze([c, b, a]);
        var group = analysis.Duplicates.Single();
        await Assert.That(group.Keeper).IsSameReferenceAs(a);
        await Assert.That(group.Members.Single().Photo).IsSameReferenceAs(b);
        await Assert.That(analysis.Duplicates.SelectMany(g => g.Members)).DoesNotContain(m => m.Photo == c);
    }

    [Test]
    public async Task A_Copy_Marked_Keep_Stays_In_The_Group_But_Is_Not_Removable()
    {
        var keeper = Make("IMG_0001.JPG", taken: Shot, format: "JPEG", quality: 95);
        var keptCopy = Make("IMG_0001 resaved.JPG", taken: Shot, format: "JPEG", quality: 80, size: 700, kept: true);
        var copy = Make("IMG_0001 small.JPG", taken: Shot, format: "JPEG", width: 1600, height: 1200, size: 300);
        var group = DuplicateAnalysisService.Analyze([keptCopy, copy, keeper]).Duplicates.Single();
        await Assert.That(group.Keeper).IsSameReferenceAs(keeper);
        await Assert.That(group.Members.Count).IsEqualTo(2);
        await Assert.That(group.Removable.Single()).IsSameReferenceAs(copy);
        await Assert.That(group.ReclaimableBytes).IsEqualTo(300);
    }

    [Test]
    public async Task Groups_Freeing_The_Most_Space_Come_First_And_Unhashed_Photos_Are_Ignored()
    {
        var smallA = Make("a1.jpg", contentHash: "A", hash: PictureA, size: 10);
        var smallB = Make("a2.jpg", contentHash: "A", hash: PictureA, size: 10);
        var bigA = Make("b1.jpg", contentHash: "B", hash: PictureB, size: 9_000);
        var bigB = Make("b2.jpg", contentHash: "B", hash: PictureB, size: 9_000);
        var unhashed = Make("c.jpg", hash: PictureC); unhashed.ContentHash = null;
        var blank = Make("d.jpg", hash: PictureC); blank.ContentHash = " ";
        var analysis = DuplicateAnalysisService.Analyze([smallA, smallB, unhashed, blank, bigA, bigB]);
        await Assert.That(analysis.Duplicates.Select(g => g.ReclaimableBytes)).IsEquivalentTo(new long[] { 9_000, 10 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Undecodable_Files_Are_Still_Matched_When_Identical()
    {
        var a = Make("broken.heic", contentHash: "X"); a.Signature = null; a.PerceptualHash = null;
        var b = Make("broken (1).heic", contentHash: "X"); b.Signature = null; b.PerceptualHash = null;
        var decoded = Make("fine.heic");
        var group = DuplicateAnalysisService.Analyze([a, b, decoded]).Duplicates.Single();
        await Assert.That(group.Keeper).IsSameReferenceAs(a);
    }

    [Test]
    public async Task Identical_Videos_Form_A_Group_And_Unread_Ones_Are_Left_Out()
    {
        var first = Video("clip.MOV", folder: "a", contentHash: "V");
        var second = Video("clip.MOV", folder: "b", contentHash: "V");
        var unread = Video("other.MOV"); // its size was shared with nothing, so it was never hashed
        var analysis = DuplicateAnalysisService.Analyze([second, unread, first]);
        var group = analysis.Duplicates.Single();
        await Assert.That(group.Keeper).IsSameReferenceAs(first);
        await Assert.That((group.Members.Single().Photo, group.Members[0].Match)).IsEqualTo((second, MatchKind.Identical));
        await Assert.That(analysis.Similar).IsEmpty();
    }

    [Test]
    public async Task A_Live_Photos_Video_Travels_With_Its_Picture_And_Is_Not_Matched_On_Its_Own()
    {
        // One Live Photo imported twice: once as HEIC, once converted to JPEG, each with the same video.
        var heic = Make("IMG_1.HEIC", folder: "a", taken: Shot); heic.LivePhotoId = "LIVE-1";
        var jpeg = Make("IMG_1.JPG", folder: "b", taken: Shot, format: "JPEG"); jpeg.LivePhotoId = "LIVE-1";
        var videoA = Video("IMG_1.MOV", folder: "a", contentHash: "M", livePhotoId: "live-1");
        var videoB = Video("IMG_1.MOV", folder: "b", contentHash: "M", livePhotoId: "LIVE-1");

        var analysis = DuplicateAnalysisService.Analyze([videoA, heic, videoB, jpeg], KeepJpeg);

        // Matched on their own, one video would be removed as a copy of the other, and the other would
        // then leave with the HEIC it belongs to: both gone.
        var group = analysis.Duplicates.Single();
        await Assert.That(group.Keeper).IsSameReferenceAs(jpeg);
        await Assert.That(group.Members.Single().Photo).IsSameReferenceAs(heic);
    }

    [Test]
    public async Task A_Video_Without_Its_Picture_Beside_It_Is_Matched_Like_Any_Other_File()
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

        await Assert.That(analysis.Duplicates.Count).IsEqualTo(2);
        var strays = analysis.Duplicates.Single(g => g.Keeper.ContentHash == "M");
        await Assert.That((strays.Keeper, strays.Members.Single().Photo)).IsEqualTo((strayCopy, strayCopyAgain));
        var others = analysis.Duplicates.Single(g => g.Keeper.ContentHash == "U");
        await Assert.That(others.Members.Count + 1).IsEqualTo(2);
        await Assert.That(analysis.Duplicates.SelectMany(g => g.Members.Select(m => m.Photo).Append(g.Keeper))).DoesNotContain(p => p == itsVideo);
    }

    [Test]
    public async Task Analysis_Is_Reused_Until_The_Photos_Change()
    {
        var repo = new InMemoryPhotoRepository();
        await repo.AddOrUpdateAsync(Make("a.jpg", contentHash: "A"));
        await repo.AddOrUpdateAsync(Make("b.jpg", contentHash: "A"));
        var service = new DuplicateAnalysisService(repo, KeepJpeg);

        var first = await service.GetAsync();
        await Assert.That(first.Duplicates).HasSingleItem();
        await Assert.That(await service.GetAsync()).IsSameReferenceAs(first);

        await repo.DeleteAsync(first.Duplicates[0].Members[0].Photo.Id);
        var afterChange = await service.GetAsync();
        await Assert.That(afterChange).IsNotSameReferenceAs(first);
        await Assert.That(afterChange.Duplicates).IsEmpty();
    }

    [Test]
    public async Task Reads_The_Photos_Once_Per_Version()
    {
        var repo = new Mock<IPhotoRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Photo>());
        var service = new DuplicateAnalysisService(repo.Object);
        await service.GetAsync();
        await service.GetAsync();
        repo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Live_Photo_Halves_Recorded_By_Name_Alone_Are_Still_Paired()
    {
        // A scan always records full paths; a record without one must still not stop an analysis.
        var picture = new Photo { SourcePath = "IMG_1.HEIC", FileName = "IMG_1.HEIC", ContentHash = "P", LivePhotoId = "LIVE-1" };
        var itsVideo = new Photo { SourcePath = "IMG_1.MOV", FileName = "IMG_1.MOV", ContentHash = "M", LivePhotoId = "LIVE-1" };
        var pathless = new Photo { SourcePath = "", FileName = "IMG_2.MOV", ContentHash = "N", LivePhotoId = "LIVE-2" };
        var strayCopy = Video("IMG_1.MOV", folder: "elsewhere", contentHash: "M", livePhotoId: "LIVE-1");

        var analysis = DuplicateAnalysisService.Analyze([picture, itsVideo, pathless, strayCopy]);

        await Assert.That(analysis.Duplicates).IsEmpty(); // the video beside its picture is not matched on its own
        await Assert.That(analysis.Similar).IsEmpty();
    }
}
