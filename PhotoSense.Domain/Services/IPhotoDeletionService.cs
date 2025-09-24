using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Domain.Services;

public interface IPhotoDeletionService
{
    Task DeleteAsync(PhotoId id, bool deleteFile, CancellationToken ct = default);
}
