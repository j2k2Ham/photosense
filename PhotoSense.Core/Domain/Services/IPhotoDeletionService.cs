using PhotoSense.Core.Domain.ValueObjects;

namespace PhotoSense.Core.Domain.Services;

public interface IPhotoDeletionService
{
    Task DeleteAsync(PhotoId id, bool deleteFile, CancellationToken ct = default);
}
