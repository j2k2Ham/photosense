using PhotoSense.Domain.Entities;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Domain.Repositories;

/// <summary>What scans have recorded about each picture and video they found.</summary>
public interface IPhotoRepository
{
    /// <summary>Changes whenever a photo is added, updated or deleted; lets callers cache derived results.</summary>
    long Version { get; }
    /// <summary>Records a photo, replacing any record with the same id.</summary>
    Task AddOrUpdateAsync(Photo photo, CancellationToken ct = default);
    /// <summary>The record with that id, or null when there is none.</summary>
    Task<Photo?> GetAsync(PhotoId id, CancellationToken ct = default);
    /// <summary>The record for a file path, if that file has been scanned. One file has at most one record.</summary>
    Task<Photo?> GetByPathAsync(string path, CancellationToken ct = default);
    /// <summary>Every record of a file with these exact bytes.</summary>
    Task<IReadOnlyList<Photo>> GetByHashAsync(string hash, CancellationToken ct = default);
    /// <summary>Every record.</summary>
    Task<IReadOnlyList<Photo>> GetAllAsync(CancellationToken ct = default);
    /// <summary>Forgets one record. The file itself is not touched.</summary>
    Task DeleteAsync(PhotoId id, CancellationToken ct = default);
    /// <summary>Forgets every record, as though nothing had been scanned. Returns how many there were.</summary>
    Task<int> ClearAsync(CancellationToken ct = default);
}
