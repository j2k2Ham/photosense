namespace PhotoSense.Domain.Services;

/// <summary>Cache of small JPEG previews, keyed by content hash so identical files share one.</summary>
public interface IThumbnailStore
{
    /// <summary>Keeps the preview of the file with this hash, replacing any kept before.</summary>
    Task SaveAsync(string contentHash, byte[] jpeg, CancellationToken ct = default);
    /// <summary>The preview of the file with this hash, or null when there is none.</summary>
    Task<byte[]?> GetAsync(string contentHash, CancellationToken ct = default);
    /// <summary>Discards every preview; they are made again by the next scan.</summary>
    Task ClearAsync(CancellationToken ct = default);
}
