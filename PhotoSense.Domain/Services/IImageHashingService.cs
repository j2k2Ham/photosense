namespace PhotoSense.Domain.Services;

public interface IImageHashingService
{
    /// <summary>Hash of the file's bytes; equal hashes mean identical files.</summary>
    Task<string> ComputeHashAsync(Stream imageStream, CancellationToken ct = default);
}
