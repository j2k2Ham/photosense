using Moq;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.DTOs;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Services;
using PhotoSense.Functions.Scanning;
using static PhotoSense.Tests.TestPhotos;

namespace PhotoSense.Tests.Functions;

public class ScanGroupingFacadeTests
{
    private static DuplicateGroup Group(string name, int copies, MatchKind match = MatchKind.Identical, bool allKept = false)
    {
        var keeper = Make($"{name}.HEIC", folder: Path.Combine("photos", "2024"), taken: Shot, size: 3_000);
        var members = Enumerable.Range(1, copies)
            .Select(i => new DuplicateMember(Make($"{name} ({i}).JPG", folder: Path.Combine("photos", "backup"), format: "JPEG", quality: 94, size: 1_000, kept: allKept), match, 0, 0))
            .ToList();
        return new DuplicateGroup(keeper, members);
    }

    private static ScanGroupingFacade Facade(IReadOnlyList<DuplicateGroup> duplicates, IReadOnlyList<DuplicateGroup>? similar = null)
    {
        var analysis = new Mock<IDuplicateAnalysisService>();
        analysis.Setup(a => a.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new DuplicateAnalysis(duplicates, similar ?? []));
        var places = new Mock<IPlaceNameResolver>();
        places.Setup(p => p.Describe(35.2, -80.8)).Returns("Charlotte, North Carolina, US");
        return new ScanGroupingFacade(analysis.Object, new PhotoDtoMapper(places.Object), new PhotoRanking());
    }

    [Test]
    public async Task Describes_A_Group_With_Its_Keeper_Members_And_Why_The_Keeper_Was_Preferred()
    {
        var group = new DuplicateGroup(
            Make("IMG_4198.HEIC", folder: Path.Combine("photos", "2024"), taken: Shot, size: 3_000),
            [
                new DuplicateMember(Make("IMG_4198 (1).HEIC", size: 3_000), MatchKind.Identical, 0, 0),
                new DuplicateMember(Make("IMG_4198.JPG", format: "JPEG", quality: 94, size: 6_000, taken: Shot, width: 1600, height: 1200), MatchKind.SamePicture, 2, 0.1)
            ]);
        group.Keeper.Latitude = 35.2; group.Keeper.Longitude = -80.8; group.Keeper.CameraModel = "iPhone";

        var page = await Facade([group]).BuildAsync(similar: false, q: null, hideKept: false, page: 1, pageSize: 10, default);

        await Assert.That((page.Mode, page.Page, page.PageSize, page.Total, page.TotalPages)).IsEqualTo(("duplicates", 1, 10, 1, 1));
        await Assert.That((page.RemovableCount, page.ReclaimableBytes)).IsEqualTo((2, 9_000L));
        var dto = page.Items.Single();
        await Assert.That(dto.Key).IsEqualTo(group.Key);
        await Assert.That(dto.ReclaimableBytes).IsEqualTo(9_000);
        await Assert.That((dto.Keeper.Id, dto.Keeper.FileName, dto.Keeper.Folder, dto.Keeper.Width, dto.Keeper.Height, dto.Keeper.Format)).IsEqualTo((group.Keeper.Id.Value, "IMG_4198.HEIC", Path.Combine("photos", "2024"), 4032, 3024, "HEIC"));
        await Assert.That((dto.Keeper.TakenOn!.Value, dto.Keeper.Latitude!.Value, dto.Keeper.Longitude!.Value, dto.Keeper.CameraModel, dto.Keeper.Set, dto.Keeper.Kept)).IsEqualTo((Shot, 35.2, -80.8, "iPhone", "Primary", false));
        await Assert.That(dto.Keeper.PlaceName).IsEqualTo("Charlotte, North Carolina, US");
        await Assert.That(dto.Keeper.IsVideo).IsFalse();
        await Assert.That(dto.Members.Select(m => (m.Match, m.KeeperReason))).IsEquivalentTo(new[] { ("identical", "Identical file"), ("samePicture", "Higher resolution: 4032×3024 vs 1600×1200") }, CollectionOrdering.Matching);
        foreach (var m in dto.Members) await Assert.That(m.Photo.PlaceName).IsNull(); // no position, so no place
    }

    [Test]
    public async Task Pages_Through_Groups_While_Totals_Cover_Every_Duplicate()
    {
        var facade = Facade(Enumerable.Range(0, 5).Select(i => Group($"IMG_{i}", copies: 2)).ToList());

        var last = await facade.BuildAsync(false, null, false, page: 3, pageSize: 2, default);
        await Assert.That((last.Page, last.Total, last.TotalPages)).IsEqualTo((3, 5, 3));
        await Assert.That(last.Items.Single().Keeper.FileName).IsEqualTo("IMG_4.HEIC"); // 5 groups => pages: 2,2,1
        await Assert.That((last.RemovableCount, last.ReclaimableBytes)).IsEqualTo((10, 10_000L));

        await Assert.That((await facade.BuildAsync(false, null, false, page: 4, pageSize: 2, default)).Items).IsEmpty();
    }

    [Test]
    public async Task Search_Matches_File_Names_And_Folders_Of_Keeper_Or_Copies()
    {
        var facade = Facade([Group("IMG_1", 1), Group("DSC_2", 1)]);
        await Assert.That((await facade.BuildAsync(false, "dsc_", false, 1, 10, default)).Items.Single().Keeper.FileName).IsEqualTo("DSC_2.HEIC");
        await Assert.That((await facade.BuildAsync(false, "img_1 (1)", false, 1, 10, default)).Items.Single().Keeper.FileName).IsEqualTo("IMG_1.HEIC");
        await Assert.That((await facade.BuildAsync(false, "BACKUP", false, 1, 10, default)).Total).IsEqualTo(2);
        await Assert.That((await facade.BuildAsync(false, "nothing like this", false, 1, 10, default)).Total).IsEqualTo(0);
        // Totals describe what a bulk removal would take, whatever is being searched for.
        await Assert.That((await facade.BuildAsync(false, "dsc_", false, 1, 10, default)).RemovableCount).IsEqualTo(2);
    }

    [Test]
    public async Task Reviewed_Groups_Can_Be_Hidden()
    {
        var facade = Facade([Group("IMG_1", 2, allKept: true), Group("IMG_2", 1)]);
        await Assert.That((await facade.BuildAsync(false, null, hideKept: false, 1, 10, default)).Total).IsEqualTo(2);
        var hidden = await facade.BuildAsync(false, null, hideKept: true, 1, 10, default);
        await Assert.That(hidden.Items.Single().Keeper.FileName).IsEqualTo("IMG_2.HEIC");
        await Assert.That(hidden.RemovableCount).IsEqualTo(1);
    }

    [Test]
    public async Task Similar_Groups_Are_Listed_Separately_And_Never_Counted_As_Removable()
    {
        var facade = Facade([Group("IMG_1", 1)], similar: [Group("IMG_9", 3, MatchKind.Similar)]);
        var page = await facade.BuildAsync(similar: true, null, false, 1, 10, default);
        await Assert.That(page.Mode).IsEqualTo("similar");
        var group = page.Items.Single();
        await Assert.That(group.Keeper.FileName).IsEqualTo("IMG_9.HEIC");
        foreach (var m in group.Members) await Assert.That(m.Match).IsEqualTo("similar");
        await Assert.That(page.RemovableCount).IsEqualTo(1);
    }
}
