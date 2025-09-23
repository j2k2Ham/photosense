using PhotoSense.Core.Domain.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PhotoSense.Infrastructure.Hashing;

public class PerceptualHashingService : IImageHashingService
{
    public async Task<string> ComputeHashAsync(Stream imageStream, CancellationToken ct = default)
    {
        // Standard SHA256 fallback (reuse existing logic could be injected instead). For brevity compute simple hash.
        imageStream.Position = 0;
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = await sha.ComputeHashAsync(imageStream, ct);
        return Convert.ToHexString(bytes);
    }

    public async Task<string> ComputePerceptualHashAsync(Stream imageStream, CancellationToken ct = default)
    {
        // Simple aHash implementation (resize to 8x8 grayscale and compare to average)
        imageStream.Position = 0;
        using var image = await Image.LoadAsync<Rgba32>(imageStream, ct);
        image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(8, 8), Sampler = KnownResamplers.Bicubic, Mode = ResizeMode.Stretch })
                               .Grayscale());
        var pixels = new List<byte>(64);
        double total = 0;
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                var p = image[x, y];
                byte l = (byte)((p.R + p.G + p.B) / 3);
                pixels.Add(l); total += l;
            }
        }
        var avg = total / 64.0;
        var bits = pixels.Select(p => p < avg ? '0' : '1').ToArray();
        var hash = string.Concat(Enumerable.Range(0, 16).Select(idx => Convert.ToInt32(new string(bits, idx * 4, 4), 2).ToString("X")));
        return hash;
    }
}
