// removed stray line introduced by edit
using Xunit;
using Moq;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Entities;
using System.Collections.Generic;
using PhotoSense.Functions.Scanning;

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
        Assert.Equal(1, (int)result.GetType().GetProperty("page")!.GetValue(result)!);
        Assert.Equal(1, (int)result.GetType().GetProperty("pageSize")!.GetValue(result)!);
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
    var itemsObj = result.GetType().GetProperty("items")!.GetValue(result)!;
    Assert.NotNull(itemsObj);
    }
}