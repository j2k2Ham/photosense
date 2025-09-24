using Moq;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using Xunit;

namespace PhotoSense.Tests.Application;

public class DuplicateGroupingServiceTests
{
    [Fact]
    public async Task Returns_Only_Groups_With_Duplicates()
    {
        var repo = new Mock<IPhotoRepository>();
        var photos = new List<Photo>
        {
            new() { SourcePath = "p1", FileName = "f1", FileSizeBytes = 1, ContentHash = "H1", Set = PhotoSet.Primary },
            new() { SourcePath = "p2", FileName = "f2", FileSizeBytes = 1, ContentHash = "H1", Set = PhotoSet.Primary },
            new() { SourcePath = "p3", FileName = "f3", FileSizeBytes = 1, ContentHash = "H2", Set = PhotoSet.Primary }
        };
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(photos);
        var svc = new DuplicateGroupingService(repo.Object);
        var groups = await svc.GetDuplicateGroupsAsync();
        Assert.Single(groups);
        Assert.Equal("H1", groups[0].Hash);
        Assert.Equal(2, groups[0].Photos.Count);
    }
}
