namespace PhotoSense.Domain.Services;

/// <summary>Tells identical files apart from different ones by their bytes alone.</summary>
public interface IImageHashingService
{
    /// <summary>Hash of the file's bytes; equal hashes mean identical files.</summary>
    Task<string> ComputeHashAsync(Stream imageStream, CancellationToken ct = default);
}
