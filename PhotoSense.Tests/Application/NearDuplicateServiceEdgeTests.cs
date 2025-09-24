using Moq;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using Xunit;

namespace PhotoSense.Tests.Application;

public class NearDuplicateServiceEdgeTests
{
    [Fact]
    public async Task Returns_Empty_When_No_Photos()
    {
        var repo = new Mock<IPhotoRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Photo>());
        var svc = new NearDuplicateService(repo.Object);
        var groups = await svc.GetNearDuplicatesAsync();
        Assert.Empty(groups);
    }

    [Fact]
    public async Task Skips_Already_Visited_Photos()
    {
        // Two clusters of identical hashes; ensures second occurrence of each representative is skipped by visited check.
        var photos = new List<Photo>();
        // Cluster 1 (3 photos)
        photos.Add(new Photo { SourcePath = "c1a", FileName = "c1a", FileSizeBytes = 1, PerceptualHash = "AAAAAAAA", Set = PhotoSet.Primary });
        photos.Add(new Photo { SourcePath = "c1b", FileName = "c1b", FileSizeBytes = 1, PerceptualHash = "AAAAAAAA", Set = PhotoSet.Primary });
        photos.Add(new Photo { SourcePath = "c1c", FileName = "c1c", FileSizeBytes = 1, PerceptualHash = "AAAAAAAA", Set = PhotoSet.Primary });
        // Cluster 2 (2 photos)
        photos.Add(new Photo { SourcePath = "c2a", FileName = "c2a", FileSizeBytes = 1, PerceptualHash = "BBBBBBBB", Set = PhotoSet.Primary });
        photos.Add(new Photo { SourcePath = "c2b", FileName = "c2b", FileSizeBytes = 1, PerceptualHash = "BBBBBBBB", Set = PhotoSet.Primary });
        var repo = new Mock<IPhotoRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(photos);
        var svc = new NearDuplicateService(repo.Object);
        var groups = await svc.GetNearDuplicatesAsync(maxHammingDistance:0);
        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.Photos.Count == 3);
        Assert.Contains(groups, g => g.Photos.Count == 2);
        // Ensure no duplicate group for same representative hash
        Assert.Equal(2, groups.Select(g => g.RepresentativeHash).Distinct().Count());
    }
}
