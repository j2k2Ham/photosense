using Moq;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Core.Domain.Entities;
using PhotoSense.Core.Domain.Repositories;
using Xunit;

namespace PhotoSense.Tests.Application;

public class NearDuplicateServiceTests
{
    [Fact]
    public async Task Clusters_Near_Duplicates_By_HammingDistance()
    {
        var repo = new Mock<IPhotoRepository>();
        var photos = new List<Photo>
        {
            new() { SourcePath = "p1", FileName = "f1", FileSizeBytes = 1, PerceptualHash = "AAAA", Set = PhotoSet.Primary },
            new() { SourcePath = "p2", FileName = "f2", FileSizeBytes = 1, PerceptualHash = "AAAB", Set = PhotoSet.Primary },
            new() { SourcePath = "p3", FileName = "f3", FileSizeBytes = 1, PerceptualHash = "FFFF", Set = PhotoSet.Primary }
        };
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(photos);
        var svc = new NearDuplicateService(repo.Object);
        var groups = await svc.GetNearDuplicatesAsync(maxHammingDistance: 1);
        Assert.Single(groups);
        Assert.Equal(2, groups[0].Photos.Count);
    }
}
