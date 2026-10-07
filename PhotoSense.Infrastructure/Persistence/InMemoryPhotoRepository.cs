using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.ValueObjects;
using System.Collections.Concurrent;

namespace PhotoSense.Infrastructure.Persistence;

public class InMemoryPhotoRepository : IPhotoRepository
{
    private readonly ConcurrentDictionary<PhotoId, Photo> _store = new();
    private readonly object _writeLock = new();
    private long _version;

    public long Version => Interlocked.Read(ref _version);

    public Task AddOrUpdateAsync(Photo photo, CancellationToken ct = default)
    {
        var key = PhotoPath.Key(photo.SourcePath);
        lock (_writeLock)
        {
            // A file has one record: a different record for the same path is replaced, not joined.
            foreach (var other in _store.Values.Where(p => p.Id != photo.Id && PhotoPath.Key(p.SourcePath) == key).ToList())
                _store.TryRemove(other.Id, out _);
            _store[photo.Id] = photo;
        }
        Interlocked.Increment(ref _version);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(PhotoId id, CancellationToken ct = default)
    {
        _store.TryRemove(id, out _);
        Interlocked.Increment(ref _version);
        return Task.CompletedTask;
    }

    public Task<int> ClearAsync(CancellationToken ct = default)
    {
        int removed;
        lock (_writeLock)
        {
            removed = _store.Count;
            _store.Clear();
        }
        Interlocked.Increment(ref _version);
        return Task.FromResult(removed);
    }

    public Task<IReadOnlyList<Photo>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Photo>>(_store.Values.ToList());

    public Task<Photo?> GetAsync(PhotoId id, CancellationToken ct = default)
    {
        _store.TryGetValue(id, out var photo);
        return Task.FromResult(photo);
    }

    public Task<Photo?> GetByPathAsync(string path, CancellationToken ct = default)
    {
        var key = PhotoPath.Key(path);
        return Task.FromResult(_store.Values.FirstOrDefault(p => PhotoPath.Key(p.SourcePath) == key));
    }

    public Task<IReadOnlyList<Photo>> GetByHashAsync(string hash, CancellationToken ct = default)
    {
        var res = _store.Values.Where(p => string.Equals(p.ContentHash, hash, StringComparison.OrdinalIgnoreCase)).ToList();
        return Task.FromResult<IReadOnlyList<Photo>>(res);
    }
}
