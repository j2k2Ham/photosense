using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;

namespace PhotoSense.Application.Scanning.Services;

public class ScanExecutionService : IScanExecutionService
{
    private readonly IPhotoRepository _repo;
    private readonly IImageHashingService _hash;
    private readonly IPhotoMetadataExtractor _meta;
    private readonly IScanProgressStore _progress;
    private readonly IScanLogSink? _log;

    public ScanExecutionService(IPhotoRepository repo, IImageHashingService hash, IPhotoMetadataExtractor meta, IScanProgressStore progress, IScanLogSink? log = null)
    { _repo = repo; _hash = hash; _meta = meta; _progress = progress; _log = log; }

    public Task<int> CountAsync(string path, bool recursive)
    {
        var count = EnumerateFiles(path, recursive).Count();
        return Task.FromResult(count);
    }

    public async Task ProcessAsync(IEnumerable<string> files, PhotoSet set, string instanceId, CancellationToken ct = default)
    {
        int i = 0; var list = files.ToList();
        foreach (var file in list)
        {
            ct.ThrowIfCancellationRequested();
            await using var fs = File.OpenRead(file);
            var hash = await _hash.ComputeHashAsync(fs, ct);
            fs.Position = 0;
            var perceptual = await _hash.ComputePerceptualHashAsync(fs, ct);
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
            await _meta.ExtractAsync(photo, fs, ct);
            await _repo.AddOrUpdateAsync(photo, ct);
            _progress.IncrementProcessed(instanceId, set == PhotoSet.Primary);
            if (++i % 10 == 0) _log?.Log(instanceId, "Info", $"Processed {i}/{list.Count} files ({Path.GetFileName(file)})");
        }
    }

    public static IEnumerable<string> EnumerateFiles(string root, bool recursive)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) yield break;
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var file in Directory.EnumerateFiles(root, "*.*", option))
        {
            var ext = Path.GetExtension(file);
            if (ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".webp", StringComparison.OrdinalIgnoreCase))
                yield return file;
        }
    }
}
