using Xunit;
using Moq;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Functions.Scanning;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Services;
using System.Collections.Generic;

namespace PhotoSense.Tests.Functions;

public class HideKeptFilteringTests
{
    [Fact]
    public async Task Hides_Kept_Photos_And_Groups()
    {
        var dups = new Mock<IDuplicateGroupingService>();
        var near = new Mock<INearDuplicateService>();
        var keptPhoto = new Photo { SourcePath="a", FileName="a", FileSizeBytes=1, Set=PhotoSet.Primary, ContentHash="h1", IsKept=true};
        var other = new Photo { SourcePath="b", FileName="b", FileSizeBytes=1, Set=PhotoSet.Primary, ContentHash="h1"};
        dups.Setup(d => d.GetDuplicateGroupsAsync(default)).ReturnsAsync(new List<PhotoSense.Domain.DTOs.DuplicateGroup>{ new("h1", new List<Photo>{ keptPhoto, other }) });
        var facade = new ScanGroupingFacade(dups.Object, near.Object);
        var result = await facade.BuildAsync(false, 12, null, true, 1, 10, default);
        var items = (IEnumerable<object>)result.GetType().GetProperty("items")!.GetValue(result)!;
        var first = items.First();
        var photos = (IEnumerable<object>)first.GetType().GetProperty("photos")!.GetValue(first)!;
        Assert.Single(photos); // kept removed
    }
}