using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using Moq;

namespace PhotoSense.Benchmarks;

public class NearDuplicateBenchmarks
{
    private NearDuplicateService _service = null!;

    [Params(200, 400)] public int N;

    [GlobalSetup]
    public void Setup()
    {
        var photos = new List<Photo>();
        // Build N/2 pairs that should cluster with distance 1
        var baseHash = new string('A', 32);
        for (int i = 0; i < N; i++)
        {
            var variation = baseHash.Substring(0, 31) + (i % 2 == 0 ? 'A' : 'B');
            photos.Add(new Photo { SourcePath = $"p{i}", FileName = $"f{i}.jpg", FileSizeBytes = 1, PerceptualHash = variation, Set = PhotoSet.Primary });
        }
        var repo = new Mock<IPhotoRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(photos);
        _service = new NearDuplicateService(repo.Object);
    }

    [Benchmark]
    public async Task Cluster()
    {
        await _service.GetNearDuplicatesAsync(maxHammingDistance: 1);
    }
}

public static class Program
{
    public static void Main(string[] args) => BenchmarkRunner.Run<NearDuplicateBenchmarks>();
}
