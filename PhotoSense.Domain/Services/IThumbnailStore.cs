namespace PhotoSense.Domain.Services;

/// <summary>Cache of small JPEG previews, keyed by content hash so identical files share one.</summary>
public interface IThumbnailStore
{
    Task SaveAsync(string contentHash, byte[] jpeg, CancellationToken ct = default);
    Task<byte[]?> GetAsync(string contentHash, CancellationToken ct = default);
}
