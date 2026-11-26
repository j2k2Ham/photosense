namespace Trovolve.Infrastructure.Imaging;

/// <summary>
/// Pictures resized here, the same way whatever system decoded them, so that the measures taken from them are alike on
/// every computer.
/// </summary>
public static class Pixels
{
    /// <summary>
    /// The picture at another size, each pixel the average of the area of the picture it covers: a picture reduced loses
    /// detail evenly, with no pixel skipped. Larger, each pixel takes the colour of the one it falls in.
    /// </summary>
    /// <param name="rgb">The pixels, three bytes each, row by row.</param>
    /// <param name="width">Its width.</param>
    /// <param name="height">Its height.</param>
    /// <param name="toWidth">The width wanted.</param>
    /// <param name="toHeight">The height wanted.</param>
    public static byte[] Resize(ReadOnlySpan<byte> rgb, int width, int height, int toWidth, int toHeight)
    {
        if (width == toWidth && height == toHeight) return rgb.ToArray();
        var across = Spans(width, toWidth);
        var down = Spans(height, toHeight);

        // Each row narrowed first, then each column shortened.
        var narrowed = new float[height * toWidth * 3];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < toWidth; x++)
            {
                var (first, weights) = across[x];
                float r = 0, g = 0, b = 0;
                for (var k = 0; k < weights.Length; k++)
                {
                    var at = (y * width + first + k) * 3;
                    r += rgb[at] * weights[k];
                    g += rgb[at + 1] * weights[k];
                    b += rgb[at + 2] * weights[k];
                }
                var to = (y * toWidth + x) * 3;
                (narrowed[to], narrowed[to + 1], narrowed[to + 2]) = (r, g, b);
            }

        var resized = new byte[toWidth * toHeight * 3];
        for (var y = 0; y < toHeight; y++)
        {
            var (first, weights) = down[y];
            for (var x = 0; x < toWidth * 3; x++)
            {
                float value = 0;
                for (var k = 0; k < weights.Length; k++) value += narrowed[(first + k) * toWidth * 3 + x] * weights[k];
                resized[y * toWidth * 3 + x] = (byte)Math.Clamp(MathF.Round(value), 0, 255);
            }
        }
        return resized;
    }

    // For each pixel of the new size, the first pixel of the old it covers and how much of each it takes, adding up to one.
    private static (int First, float[] Weights)[] Spans(int from, int to)
    {
        var scale = (double)from / to;
        var spans = new (int, float[])[to];
        for (var i = 0; i < to; i++)
        {
            double start = i * scale, end = (i + 1) * scale;
            var first = (int)Math.Floor(start);
            var last = Math.Min(from - 1, (int)Math.Ceiling(end) - 1);
            var weights = new float[last - first + 1];
            for (var k = first; k <= last; k++) weights[k - first] = (float)((Math.Min(end, k + 1) - Math.Max(start, k)) / (end - start));
            spans[i] = (first, weights);
        }
        return spans;
    }
}
