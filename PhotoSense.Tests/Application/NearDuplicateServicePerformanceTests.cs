using Moq;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using Xunit;
using System.Diagnostics;

namespace PhotoSense.Tests.Application;

public class NearDuplicateServicePerformanceTests
{
    [Fact]
    public async Task Large_Input_Clusters_Within_Reasonable_Time()
    {
        // This is not a strict perf benchmark, just ensures algorithm handles O(n^2) reasonably for moderate n.
        const int clusterSize = 100; // total photos ~200 (two clusters) keeps runtime low.
        var photos = new List<Photo>();
        string base1 = new string('A', 32);
        string base2 = new string('B', 32);
        for (int i = 0; i < clusterSize; i++)
        {
            // Introduce slight variation (toggle last char) so they are all near duplicates (distance 1)
            photos.Add(new Photo { SourcePath = $"a{i}", FileName = $"a{i}.jpg", FileSizeBytes = 1, PerceptualHash = base1.Substring(0,31) + (i % 2 == 0 ? 'A' : 'C'), Set = PhotoSet.Primary });
            photos.Add(new Photo { SourcePath = $"b{i}", FileName = $"b{i}.jpg", FileSizeBytes = 1, PerceptualHash = base2.Substring(0,31) + (i % 2 == 0 ? 'B' : 'C'), Set = PhotoSet.Primary });
        }
        var repo = new Mock<IPhotoRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(photos);
        var svc = new NearDuplicateService(repo.Object);
        var sw = Stopwatch.StartNew();
        var groups = await svc.GetNearDuplicatesAsync(maxHammingDistance:2);
        sw.Stop();
        Assert.Equal(2, groups.Count); // two clusters
        Assert.True(groups.All(g => g.Photos.Count >= clusterSize));
        // Guardrail: should complete fast (heuristic limit 2 seconds in CI env)
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"Clustering took too long: {sw.Elapsed}");
    }
}
