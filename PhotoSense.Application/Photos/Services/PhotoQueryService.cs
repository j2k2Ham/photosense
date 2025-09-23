using PhotoSense.Application.Photos.Interfaces;
using PhotoSense.Core.Domain.Entities;
using PhotoSense.Core.Domain.Repositories;
using PhotoSense.Core.Domain.ValueObjects;

namespace PhotoSense.Application.Photos.Services;

public class PhotoQueryService : IPhotoQueryService
{
    private readonly IPhotoRepository _repo;
    public PhotoQueryService(IPhotoRepository repo) => _repo = repo;
    public Task<IReadOnlyList<Photo>> GetAllAsync(CancellationToken ct = default) => _repo.GetAllAsync(ct);
    public Task<Photo?> GetAsync(PhotoId id, CancellationToken ct = default) => _repo.GetAsync(id, ct);
}
