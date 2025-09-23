namespace PhotoSense.Core.Domain.Services;

public interface IImageHashingService
{
    Task<string> ComputeHashAsync(Stream imageStream, CancellationToken ct = default);
    // Optionally produce perceptual hash for near-duplicate detection
    Task<string> ComputePerceptualHashAsync(Stream imageStream, CancellationToken ct = default);
}
