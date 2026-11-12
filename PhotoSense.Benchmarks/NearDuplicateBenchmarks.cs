using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Entities;

namespace PhotoSense.Benchmarks;

/// <summary>How long grouping takes over a library of shots each saved twice: as HEIC and as a JPEG conversion.</summary>
/// <remarks>Public, as BenchmarkDotNet runs only public benchmarks.</remarks>
public class NearDuplicateBenchmarks
{
    private List<Photo> _photos = null!;

    /// <summary>How many photos the library holds.</summary>
    [Params(2000, 8000)] public int N;

    /// <summary>Makes the library once, before the runs.</summary>
    [GlobalSetup]
    public void Setup()
    {
        // N/2 shots, each present as an original and a converted copy, with unrelated hashes between shots.
        var random = new Random(1);
        var taken = new DateTime(2024, 1, 1, 12, 0, 0, 500);
        _photos = new List<Photo>(N);
        for (int i = 0; i < N / 2; i++)
        {
            var hash = ((ulong)random.NextInt64()).ToString("X16");
            var signature = new byte[32 * 32 * 3];
            random.NextBytes(signature);
            foreach (var (extension, format) in new[] { ("HEIC", "HEIC"), ("JPG", "JPEG") })
                _photos.Add(new Photo
                {
                    SourcePath = $"p{i}.{extension}", FileName = $"p{i}.{extension}", FileSizeBytes = 1, ContentHash = $"{i}{extension}",
                    PerceptualHash = hash, Signature = signature, Width = 4032, Height = 3024, Format = format,
                    TakenOn = taken.AddSeconds(i), Set = PhotoSet.Primary
                });
        }
    }

    /// <summary>Groups the library, and says how many groups of copies it found.</summary>
    [Benchmark]
    public int Analyze() => DuplicateAnalysisService.Analyze(_photos).Duplicates.Count;
}

/// <summary>Runs the benchmarks.</summary>
public static class Program
{
    /// <summary>Runs every benchmark and prints how long each took.</summary>
    public static void Main(string[] args) => BenchmarkRunner.Run<NearDuplicateBenchmarks>();
}
