using PhotoSense.Core.Domain.Entities;
using PhotoSense.Core.Domain.ValueObjects;

namespace PhotoSense.Core.Domain.Repositories;

public interface IPhotoRepository
{
    Task AddOrUpdateAsync(Photo photo, CancellationToken ct = default);
    Task<Photo?> GetAsync(PhotoId id, CancellationToken ct = default);
    Task<IReadOnlyList<Photo>> GetByHashAsync(string hash, CancellationToken ct = default);
    Task<IReadOnlyList<Photo>> GetAllAsync(CancellationToken ct = default);
    Task DeleteAsync(PhotoId id, CancellationToken ct = default);
}
