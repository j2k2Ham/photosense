using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;

namespace PhotoSense.Application.Scanning.Services;

public class PhotoIngestionService : IPhotoIngestionService
{
    private readonly IPhotoRepository _repo;
    private readonly IImageHashingService _hash;
    private readonly IPhotoMetadataExtractor _meta;

    public PhotoIngestionService(IPhotoRepository repo, IImageHashingService hash, IPhotoMetadataExtractor meta)
    { _repo = repo; _hash = hash; _meta = meta; }

    public async Task<bool> IngestAsync(string filePath, PhotoSet set, CancellationToken ct = default)
    {
        if (!File.Exists(filePath)) return false;
        await using var fs = File.OpenRead(filePath);
        var contentHash = await _hash.ComputeHashAsync(fs, ct);
        fs.Position = 0;
        var perceptual = await _hash.ComputePerceptualHashAsync(fs, ct);
        fs.Position = 0;
        var photo = new Photo
        {
            SourcePath = filePath,
            FileName = Path.GetFileName(filePath),
            FileSizeBytes = fs.Length,
            ContentHash = contentHash,
            PerceptualHash = perceptual,
            Set = set
        };
        fs.Position = 0;
        await _meta.ExtractAsync(photo, fs, ct);
        await _repo.AddOrUpdateAsync(photo, ct);
        return true;
    }
}
