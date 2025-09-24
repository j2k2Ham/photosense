using System.Security.Cryptography;
using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Hashing;

public class Sha256ImageHashingService : IImageHashingService
{
    public async Task<string> ComputeHashAsync(Stream imageStream, CancellationToken ct = default)
    {
        using var sha = SHA256.Create();
        imageStream.Position = 0;
        var hash = await sha.ComputeHashAsync(imageStream, ct);
        return Convert.ToHexString(hash);
    }

    public async Task<string> ComputePerceptualHashAsync(Stream imageStream, CancellationToken ct = default)
    {
        // Placeholder: simple reuse of SHA256. Replace with pHash/aHash/dHash algorithm later.
        return await ComputeHashAsync(imageStream, ct);
    }
}
