using System.Security.Cryptography;
using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Hashing;

public class Sha256ImageHashingService : IImageHashingService
{
    public async Task<string> ComputeHashAsync(Stream imageStream, CancellationToken ct = default)
    {
        imageStream.Position = 0;
        var hash = await SHA256.HashDataAsync(imageStream, ct);
        return Convert.ToHexString(hash);
    }
}
