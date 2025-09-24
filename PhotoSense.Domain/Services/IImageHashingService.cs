namespace PhotoSense.Domain.Services;

public interface IImageHashingService
{
    Task<string> ComputeHashAsync(Stream imageStream, CancellationToken ct = default);
    Task<string> ComputePerceptualHashAsync(Stream imageStream, CancellationToken ct = default);
}
