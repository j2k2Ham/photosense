using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using PhotoSense.Core.Domain.Repositories;
using PhotoSense.Core.Domain.Entities;
using PhotoSense.Core.Domain.Services;
using PhotoSense.Infrastructure.Scanning;

namespace PhotoSense.Functions.Scanning;

public class ScanOrchestrator
{
    private readonly IPhotoRepository _repo;
    private readonly IImageHashingService _hashing;
    private readonly IPhotoMetadataExtractor _metadata;

    public ScanOrchestrator(IPhotoRepository repo, IImageHashingService hashing, IPhotoMetadataExtractor metadata)
    {
        _repo = repo; _hashing = hashing; _metadata = metadata;
    }

    [Function(nameof(RunScanAsync))]
    public async Task RunScanAsync([OrchestrationTrigger] TaskOrchestrationContext ctx)
    {
        var input = ctx.GetInput<(string primary, string secondary, bool recursive)>();
        if (input == default) return;

        var (primary, secondary, recursive) = input;
        await ctx.CallActivityAsync(nameof(ScanLocationActivity), (primary, PhotoSet.Primary, recursive));
        await ctx.CallActivityAsync(nameof(ScanLocationActivity), (secondary, PhotoSet.Secondary, recursive));
    }

    [Function(nameof(ScanLocationActivity))]
    public async Task ScanLocationActivity([ActivityTrigger] (string path, PhotoSet set, bool recursive) payload)
    {
        var (path, set, recursive) = payload;
        foreach (var file in FileSystemImageEnumerator.Enumerate(path, recursive))
        {
            await using var fs = File.OpenRead(file);
            var hash = await _hashing.ComputeHashAsync(fs);
            fs.Position = 0;
            var perceptual = await _hashing.ComputePerceptualHashAsync(fs);
            fs.Position = 0;
            var photo = new Photo
            {
                SourcePath = file,
                FileName = Path.GetFileName(file),
                FileSizeBytes = fs.Length,
                ContentHash = hash,
                PerceptualHash = perceptual,
                Set = set
            };
            fs.Position = 0;
            await _metadata.ExtractAsync(photo, fs);
            await _repo.AddOrUpdateAsync(photo);
        }
    }
}
