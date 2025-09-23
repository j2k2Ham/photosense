using PhotoSense.Core.Domain.Repositories;
using PhotoSense.Core.Domain.Services;
using PhotoSense.Core.Domain.ValueObjects;
using PhotoSense.Core.Domain.Events;

namespace PhotoSense.Infrastructure.Deletion;

public class FileSystemPhotoDeletionService : IPhotoDeletionService
{
    private readonly IPhotoRepository _repo;
    private readonly IIntegrationEventPublisher _publisher;
    public FileSystemPhotoDeletionService(IPhotoRepository repo, IIntegrationEventPublisher publisher)
    { _repo = repo; _publisher = publisher; }

    public async Task DeleteAsync(PhotoId id, bool deleteFile, CancellationToken ct = default)
    {
        var photo = await _repo.GetAsync(id, ct);
        if (photo is null) return;
        string? path = photo.SourcePath;
        await _repo.DeleteAsync(id, ct);
        if (deleteFile && File.Exists(path))
        {
            try { File.Delete(path); } catch { /* swallow for now */ }
        }
        await _publisher.PublishAsync(new PhotoDeletedEvent(Guid.NewGuid(), DateTime.UtcNow, id, path), ct);
    }
}
