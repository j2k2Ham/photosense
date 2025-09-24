using PhotoSense.Domain.Entities;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Application.Photos.Interfaces;

public interface IPhotoQueryService
{
    Task<IReadOnlyList<Photo>> GetAllAsync(CancellationToken ct = default);
    Task<Photo?> GetAsync(PhotoId id, CancellationToken ct = default);
}
