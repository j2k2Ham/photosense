using PhotoSense.Domain.Entities;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Domain.Repositories;

public interface IPhotoRepository
{
    /// <summary>Changes whenever a photo is added, updated or deleted; lets callers cache derived results.</summary>
    long Version { get; }
    Task AddOrUpdateAsync(Photo photo, CancellationToken ct = default);
    Task<Photo?> GetAsync(PhotoId id, CancellationToken ct = default);
    /// <summary>The record for a file path, if that file has been scanned. One file has at most one record.</summary>
    Task<Photo?> GetByPathAsync(string path, CancellationToken ct = default);
    Task<IReadOnlyList<Photo>> GetByHashAsync(string hash, CancellationToken ct = default);
    Task<IReadOnlyList<Photo>> GetAllAsync(CancellationToken ct = default);
    Task DeleteAsync(PhotoId id, CancellationToken ct = default);
    /// <summary>Forgets every record, as though nothing had been scanned. Returns how many there were.</summary>
    Task<int> ClearAsync(CancellationToken ct = default);
}
