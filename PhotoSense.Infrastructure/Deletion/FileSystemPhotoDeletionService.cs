using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Domain.Events;

namespace PhotoSense.Infrastructure.Deletion;

public class FileSystemPhotoDeletionService : IPhotoDeletionService
{
    private readonly IPhotoRepository _repo;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly ICompanionFileFinder _companions;
    public FileSystemPhotoDeletionService(IPhotoRepository repo, IIntegrationEventPublisher publisher, ICompanionFileFinder companions)
    { _repo = repo; _publisher = publisher; _companions = companions; }

    public async Task<RemovalResult> DeleteAsync(PhotoId id, bool deleteFile, CancellationToken ct = default)
    {
        var photo = await _repo.GetAsync(id, ct);
        if (photo is null) return new RemovalResult(RemovalOutcome.NotFound);

        string? heldAt = null;
        var moved = new List<string>();
        var file = new FileInfo(photo.SourcePath);
        if (deleteFile && file.Exists)
        {
            // The decision to remove this file was made from the scan; if the file has changed since, it no longer holds.
            if (file.Length != photo.FileSizeBytes || (photo.FileModifiedUtc is { } scanned && file.LastWriteTimeUtc != scanned))
                return new RemovalResult(RemovalOutcome.Changed);

            // Worked out while the file is still in place: its sidecars and Live Photo video, if nothing else uses them.
            var companions = _companions.FindFor(photo.SourcePath);
            try
            {
                heldAt = Hold(photo.SourcePath, photo.ScanRoot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new RemovalResult(RemovalOutcome.Failed, Error: ex.Message);
            }

            foreach (var companion in companions)
            {
                try { Hold(companion, photo.ScanRoot); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; } // left where it is; the photo's removal stands
                moved.Add(companion);
                // A Live Photo video is scanned in its own right; its record goes with its file.
                if (await _repo.GetByPathAsync(companion, ct) is { } record) await _repo.DeleteAsync(record.Id, ct);
            }
        }

        await _repo.DeleteAsync(id, ct);
        await _publisher.PublishAsync(new PhotoDeletedEvent(Guid.NewGuid(), DateTime.UtcNow, id, photo.SourcePath), ct);
        return new RemovalResult(RemovalOutcome.Removed, heldAt) { Companions = moved };
    }

    // Removed files keep their place relative to the scanned folder, so putting one back is a matter of moving it up.
    private static string Hold(string path, string? scanRoot)
    {
        var source = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(source)!;
        var root = string.IsNullOrWhiteSpace(scanRoot) ? directory : Path.GetFullPath(scanRoot);
        var relative = Path.GetRelativePath(root, source);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            root = directory;
            relative = Path.GetFileName(source);
        }

        var target = Path.Combine(root, PhotoStorageOptions.RemovedFolderName, relative);
        var stem = Path.Combine(Path.GetDirectoryName(target)!, Path.GetFileNameWithoutExtension(target));
        for (int n = 2; File.Exists(target); n++)
            target = $"{stem} ({n}){Path.GetExtension(source)}";

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(source, target);
        return target;
    }
}
