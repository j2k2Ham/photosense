using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.ValueObjects;
using System.Collections.Concurrent;

namespace PhotoSense.Tests;

/// <summary>Scans' records kept in memory only, standing in for the database.</summary>
/// <remarks>Each method answers at once; its token is checked first, as nothing it does takes long enough to stop part way.</remarks>
internal sealed class InMemoryPhotoRepository : IPhotoRepository
{
    private readonly ConcurrentDictionary<PhotoId, Photo> _store = new();
    private readonly object _writeLock = new();
    private long _version;

    /// <inheritdoc />
    public long Version => Interlocked.Read(ref _version);

    /// <inheritdoc />
    public Task AddOrUpdateAsync(Photo photo, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
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

    /// <inheritdoc />
    public Task DeleteAsync(PhotoId id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _store.TryRemove(id, out _);
        Interlocked.Increment(ref _version);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> ClearAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        int removed;
        lock (_writeLock)
        {
            removed = _store.Count;
            _store.Clear();
        }
        Interlocked.Increment(ref _version);
        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Photo>> GetAllAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<Photo>>(_store.Values.ToList());
    }

    /// <inheritdoc />
    public Task<Photo?> GetAsync(PhotoId id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _store.TryGetValue(id, out var photo);
        return Task.FromResult(photo);
    }

    /// <inheritdoc />
    public Task<Photo?> GetByPathAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var key = PhotoPath.Key(path);
        return Task.FromResult(_store.Values.FirstOrDefault(p => PhotoPath.Key(p.SourcePath) == key));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Photo>> GetByHashAsync(string hash, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var res = _store.Values.Where(p => string.Equals(p.ContentHash, hash, StringComparison.OrdinalIgnoreCase)).ToList();
        return Task.FromResult<IReadOnlyList<Photo>>(res);
    }
}
