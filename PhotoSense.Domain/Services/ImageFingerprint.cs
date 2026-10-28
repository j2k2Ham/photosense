namespace PhotoSense.Domain.Services;

/// <summary>
/// How a picture is measured to be compared, whichever decoder unpacked it: Magick.NET on a computer, the
/// system's own on a phone. Both reduce it to the same grid and hash it the same way.
/// </summary>
public static class ImageFingerprint
{
    /// <summary>The cells along each side of the colour grid a picture is reduced to.</summary>
    public const int SignatureEdge = 32;

    /// <summary>The longest side a picture is decoded at to be measured.</summary>
    public const int WorkEdge = 512;

    /// <summary>The longest side of the preview kept for each picture.</summary>
    public const int ThumbnailEdge = 320;

    // Low-frequency DCT terms 1..8 of a 32-sample row; term 0 (overall brightness) is left out of the hash.
    private static readonly double[,] Cosines = BuildCosines();

    /// <summary>64-bit DCT hash of a 32x32 RGB grid: one bit per low-frequency term, set when above the median.</summary>
    public static ulong PerceptualHash(ReadOnlySpan<byte> rgb)
    {
        const int n = SignatureEdge;
        Span<double> luma = stackalloc double[n * n];
        for (int i = 0; i < luma.Length; i++)
            luma[i] = 0.299 * rgb[i * 3] + 0.587 * rgb[i * 3 + 1] + 0.114 * rgb[i * 3 + 2];

        Span<double> terms = stackalloc double[64];
        for (int u = 0; u < 8; u++)
            for (int v = 0; v < 8; v++)
            {
                double sum = 0;
                for (int y = 0; y < n; y++)
                {
                    double cy = Cosines[u, y];
                    for (int x = 0; x < n; x++) sum += luma[y * n + x] * cy * Cosines[v, x];
                }
                terms[u * 8 + v] = sum;
            }

        Span<double> sorted = stackalloc double[64];
        terms.CopyTo(sorted);
        sorted.Sort();
        var median = sorted[32];
        ulong hash = 0;
        for (int i = 0; i < 64; i++)
            if (terms[i] > median) hash |= 1UL << i;
        return hash;
    }

    private static double[,] BuildCosines()
    {
        var table = new double[8, SignatureEdge];
        for (int u = 0; u < 8; u++)
            for (int x = 0; x < SignatureEdge; x++)
                table[u, x] = Math.Cos((2 * x + 1) * (u + 1) * Math.PI / (2 * SignatureEdge));
        return table;
    }
}
