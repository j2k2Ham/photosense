namespace Trovolve.Domain.Services;

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

    /// <summary>The longest side a picture is reduced to for its <see cref="Sharpness"/> to be measured.</summary>
    public const int SharpnessEdge = 512;

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

    /// <summary>The tiles along each side that <see cref="Sharpness"/> measures the picture in.</summary>
    public const int SharpnessTiles = 4;

    /// <summary>
    /// How sharp a picture is: how much the edges of its brightness vary (the variance of its Laplacian) in its sharpest
    /// part, one of <see cref="SharpnessTiles"/> by <see cref="SharpnessTiles"/> tiles, measured on RGB pixels at
    /// <see cref="SharpnessEdge"/> on the long side. A shaken or unfocused picture is soft all over, so even its
    /// sharpest part is low; a portrait with its background softened on purpose keeps a sharp subject, and is not.
    /// Every decoder hands over the same reduction, so the number means the same whichever measured it.
    /// </summary>
    /// <param name="rgb">The pixels, three bytes each, row by row.</param>
    /// <param name="width">Pixels across.</param>
    /// <param name="height">Pixels down.</param>
    /// <returns>The variance in the sharpest tile; 0 for a picture too small to measure.</returns>
    public static double Sharpness(ReadOnlySpan<byte> rgb, int width, int height)
    {
        if (width < SharpnessTiles * 3 || height < SharpnessTiles * 3) return 0;
        var luma = new double[width * height];
        for (int i = 0; i < luma.Length; i++)
            luma[i] = 0.299 * rgb[i * 3] + 0.587 * rgb[i * 3 + 1] + 0.114 * rgb[i * 3 + 2];
        double sharpest = 0;
        for (int ty = 0; ty < SharpnessTiles; ty++)
            for (int tx = 0; tx < SharpnessTiles; tx++)
            {
                // Each tile's own pixels, less the picture's outer edge, which has no neighbour on one side.
                int x0 = Math.Max(1, tx * width / SharpnessTiles), x1 = Math.Min(width - 1, (tx + 1) * width / SharpnessTiles);
                int y0 = Math.Max(1, ty * height / SharpnessTiles), y1 = Math.Min(height - 1, (ty + 1) * height / SharpnessTiles);
                double sum = 0, squares = 0;
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        var at = y * width + x;
                        var edge = 4 * luma[at] - luma[at - 1] - luma[at + 1] - luma[at - width] - luma[at + width];
                        sum += edge;
                        squares += edge * edge;
                    }
                var n = (double)(x1 - x0) * (y1 - y0);
                var mean = sum / n;
                sharpest = Math.Max(sharpest, squares / n - mean * mean);
            }
        return sharpest;
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
