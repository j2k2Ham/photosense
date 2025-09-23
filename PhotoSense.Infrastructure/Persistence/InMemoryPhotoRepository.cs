using PhotoSense.Core.Domain.Entities;
using PhotoSense.Core.Domain.Repositories;
using PhotoSense.Core.Domain.ValueObjects;
using System.Collections.Concurrent;

namespace PhotoSense.Infrastructure.Persistence;

public class InMemoryPhotoRepository : IPhotoRepository
{
    private readonly ConcurrentDictionary<PhotoId, Photo> _store = new();

    public Task AddOrUpdateAsync(Photo photo, CancellationToken ct = default)
    {
        _store[photo.Id] = photo;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(PhotoId id, CancellationToken ct = default)
    {
        _store.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Photo>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Photo>>(_store.Values.ToList());

    public Task<Photo?> GetAsync(PhotoId id, CancellationToken ct = default)
    {
        _store.TryGetValue(id, out var photo);
        return Task.FromResult(photo);
    }

    public Task<IReadOnlyList<Photo>> GetByHashAsync(string hash, CancellationToken ct = default)
    {
        var res = _store.Values.Where(p => string.Equals(p.ContentHash, hash, StringComparison.OrdinalIgnoreCase)).ToList();
        return Task.FromResult<IReadOnlyList<Photo>>(res);
    }
}
