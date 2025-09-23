using PhotoSense.Core.Domain.Entities;
using PhotoSense.Core.Domain.ValueObjects;

namespace PhotoSense.Application.Photos.Interfaces;

public interface IPhotoQueryService
{
    Task<IReadOnlyList<Photo>> GetAllAsync(CancellationToken ct = default);
    Task<Photo?> GetAsync(PhotoId id, CancellationToken ct = default);
}
