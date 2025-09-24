using PhotoSense.Application.Photos.Interfaces;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Application.Photos.Services;

public class PhotoQueryService : IPhotoQueryService
{
    private readonly IPhotoRepository _repo;
    public PhotoQueryService(IPhotoRepository repo) => _repo = repo;
    public Task<IReadOnlyList<Photo>> GetAllAsync(CancellationToken ct = default) => _repo.GetAllAsync(ct);
    public Task<Photo?> GetAsync(PhotoId id, CancellationToken ct = default) => _repo.GetAsync(id, ct);
}
