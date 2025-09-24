using Moq;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Core.Domain.Repositories;
using PhotoSense.Core.Domain.Entities;
using Xunit;

namespace PhotoSense.Tests.Application;

public class NearDuplicateServiceBoundaryTests
{
    [Fact]
    public async Task Boundary_HammingDistance_Includes_When_Equal()
    {
        var repo = new Mock<IPhotoRepository>();
        var baseHash = new string('A', 16);
        // Create second hash differing in exactly 1 char
        var otherHash = baseHash.Substring(0, 15) + 'B';
        var photos = new List<Photo>
        {
            new() { SourcePath = "p1", FileName = "f1", FileSizeBytes = 1, PerceptualHash = baseHash, Set = PhotoSet.Primary },
            new() { SourcePath = "p2", FileName = "f2", FileSizeBytes = 1, PerceptualHash = otherHash, Set = PhotoSet.Primary }
        };
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(photos);
        var svc = new NearDuplicateService(repo.Object);
        var groups = await svc.GetNearDuplicatesAsync(maxHammingDistance: 1);
        Assert.Single(groups);
    }

    [Fact]
    public async Task Boundary_HammingDistance_Excludes_When_Greater()
    {
        var repo = new Mock<IPhotoRepository>();
        var baseHash = new string('A', 16);
        var otherHash = baseHash.Substring(0, 14) + "BB"; // difference of 2
        var photos = new List<Photo>
        {
            new() { SourcePath = "p1", FileName = "f1", FileSizeBytes = 1, PerceptualHash = baseHash, Set = PhotoSet.Primary },
            new() { SourcePath = "p2", FileName = "f2", FileSizeBytes = 1, PerceptualHash = otherHash, Set = PhotoSet.Primary }
        };
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(photos);
        var svc = new NearDuplicateService(repo.Object);
        var groups = await svc.GetNearDuplicatesAsync(maxHammingDistance: 1);
        Assert.Empty(groups);
    }
}
