using Moq;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using Xunit;

namespace PhotoSense.Tests.Application;

public class NearDuplicateServiceDistanceTests
{
    [Fact]
    public async Task Returns_Empty_When_Length_Mismatch()
    {
        var repo = new Mock<IPhotoRepository>();
        var photos = new List<Photo>
        {
            new() { SourcePath = "p1", FileName = "f1", FileSizeBytes = 1, PerceptualHash = "AAAA", Set = PhotoSet.Primary },
            new() { SourcePath = "p2", FileName = "f2", FileSizeBytes = 1, PerceptualHash = "AAAAAA", Set = PhotoSet.Primary }
        };
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(photos);
        var svc = new NearDuplicateService(repo.Object);
        var groups = await svc.GetNearDuplicatesAsync();
        Assert.Empty(groups); // distance is int.MaxValue so cannot cluster
    }
}
