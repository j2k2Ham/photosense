// removed stray line introduced by edit
using Xunit;
using Moq;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Entities;
using System.Collections.Generic;
using PhotoSense.Functions.Scanning;
using PhotoSense.Contracts.Duplicates;

namespace PhotoSense.Tests.Functions;

public class ScanGroupingFacadeTests
{
    [Fact]
    public async Task Exact_Groups_Are_Paged()
    {
        var dups = new Mock<IDuplicateGroupingService>();
        var near = new Mock<INearDuplicateService>();
        dups.Setup(d => d.GetDuplicateGroupsAsync(default)).ReturnsAsync(new List<PhotoSense.Domain.DTOs.DuplicateGroup> {
            new("hash1", new List<Photo>{ new(){ SourcePath="a", FileName="a", FileSizeBytes=1, Set=PhotoSet.Primary, ContentHash="hash1"}, new(){ SourcePath="b", FileName="b", FileSizeBytes=1, Set=PhotoSet.Primary, ContentHash="hash1"}}),
            new("hash2", new List<Photo>{ new(){ SourcePath="c", FileName="c", FileSizeBytes=1, Set=PhotoSet.Primary, ContentHash="hash2"}, new(){ SourcePath="d", FileName="d", FileSizeBytes=1, Set=PhotoSet.Primary, ContentHash="hash2"}})
        });
    var facade = new ScanGroupingFacade(dups.Object, near.Object);
    var result = await facade.BuildAsync(false, 12, null, false, 1, 1, default);
    var exact = Assert.IsType<PhotoSense.Contracts.Duplicates.ExactDuplicateGroupsPageDto>(result);
        Assert.Equal(1, exact.Page);
        Assert.Equal(1, exact.PageSize);
    }

    [Fact]
    public async Task Near_Groups_Include_Distance()
    {
        var dups = new Mock<IDuplicateGroupingService>();
        var nearSvc = new Mock<INearDuplicateService>();
        var p1 = new Photo{ SourcePath="a", FileName="a", FileSizeBytes=1, Set=PhotoSet.Primary, PerceptualHash=new string('a',32)};
        var p2 = new Photo{ SourcePath="b", FileName="b", FileSizeBytes=1, Set=PhotoSet.Primary, PerceptualHash=new string('b',32)};
        nearSvc.Setup(n => n.GetNearDuplicatesAsync(12, default)).ReturnsAsync(new List<PhotoSense.Domain.DTOs.NearDuplicateGroup>{ new(new string('a',32), new List<Photo>{p1,p2}) });
    var facade = new ScanGroupingFacade(dups.Object, nearSvc.Object);
    var result = await facade.BuildAsync(true, 12, null, false, 1, 10, default);
    var nearPage = Assert.IsType<PhotoSense.Contracts.Duplicates.NearDuplicateGroupsPageDto>(result);
    Assert.True(nearPage.Items.Count > 0);
    var nearItem = Assert.IsType<PhotoSense.Contracts.Duplicates.NearGroupItemDto>(nearPage.Items[0]);
    Assert.True(nearItem.Distance >= 0);
    }

    [Fact]
    public async Task Pagination_Last_Page_Smaller_And_OutOfRange_Empty()
    {
        var dups = new Mock<IDuplicateGroupingService>();
        var near = new Mock<INearDuplicateService>();
        var groups = new List<PhotoSense.Domain.DTOs.DuplicateGroup>();
        for (int i=0;i<5;i++)
        {
            groups.Add(new PhotoSense.Domain.DTOs.DuplicateGroup($"h{i}", new List<Photo>{ new(){ SourcePath="x", FileName=$"f{i}.jpg", FileSizeBytes=1, Set=PhotoSet.Primary, ContentHash=$"h{i}" }, new(){ SourcePath="y", FileName=$"f{i}b.jpg", FileSizeBytes=1, Set=PhotoSet.Primary, ContentHash=$"h{i}" }}));
        }
        dups.Setup(d=>d.GetDuplicateGroupsAsync(default)).ReturnsAsync(groups);
        var facade = new ScanGroupingFacade(dups.Object, near.Object);
        var pageSize = 2;
    var last = await facade.BuildAsync(false, 12, null, false, 3, pageSize, default);
    var lastExact = Assert.IsType<PhotoSense.Contracts.Duplicates.ExactDuplicateGroupsPageDto>(last);
    Assert.Equal(3, lastExact.Page);
    Assert.Single(lastExact.Items); // 5 groups => pages: 2,2,1
    var outRes = await facade.BuildAsync(false, 12, null, false, 4, pageSize, default);
    var outExact = Assert.IsType<PhotoSense.Contracts.Duplicates.ExactDuplicateGroupsPageDto>(outRes);
    Assert.Empty(outExact.Items);
    }
}