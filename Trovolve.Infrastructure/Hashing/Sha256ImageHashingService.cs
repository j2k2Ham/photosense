using System.Security.Cryptography;
using Trovolve.Domain.Services;

namespace Trovolve.Infrastructure.Hashing;

/// <summary>Tells identical files by the SHA-256 hash of their bytes.</summary>
public sealed class Sha256ImageHashingService : IImageHashingService
{
    /// <summary>The SHA-256 hash of the whole stream, from its start, in upper-case hex.</summary>
    public async Task<string> ComputeHashAsync(Stream imageStream, CancellationToken ct)
    {
        imageStream.Position = 0;
        var hash = await SHA256.HashDataAsync(imageStream, ct);
        return Convert.ToHexString(hash);
    }
}
