namespace Trovolve.Infrastructure.Imaging;

/// <summary>
/// The quality a JPEG was saved at, read from its luminance quantization table: the quality whose table, as libjpeg and
/// most encoders scale the standard one, is nearest. Of two copies of a picture, the one saved at the higher quality
/// is kept (<c>PhotoRanking</c>).
/// </summary>
public static class JpegQuality
{
    /// <summary>The quality, 1 to 100, or null when the bytes are no JPEG or have no luminance table before the picture's data.</summary>
    /// <param name="jpeg">The file's bytes.</param>
    public static int? Estimate(ReadOnlySpan<byte> jpeg)
    {
        if (LuminanceTable(jpeg) is not { } table) return null;
        int best = 0, nearest = int.MaxValue;
        // From the finest down, so that of qualities with the same table the highest is said.
        for (var quality = 100; quality >= 1; quality--)
        {
            var scaled = JpegWriter.LuminanceTable(quality);
            var distance = 0;
            for (var k = 0; k < 64; k++) distance += Math.Abs(scaled[JpegWriter.ZigZag[k]] - table[k]);
            if (distance < nearest) (best, nearest) = (quality, distance);
        }
        return best;
    }

    // The luminance table (table 0), in the order the file keeps it, from the segments before the picture's data.
    private static int[]? LuminanceTable(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8) return null;
        var at = 2;
        while (at + 4 <= jpeg.Length && jpeg[at] == 0xFF)
        {
            var marker = jpeg[at + 1];
            // The start of the picture's data, or its end: no table comes after.
            if (marker is 0xDA or 0xD9) return null;
            var end = at + 2 + (jpeg[at + 2] << 8 | jpeg[at + 3]);
            if (marker == 0xDB)
                for (var p = at + 4; p + 65 <= end && end <= jpeg.Length;)
                {
                    var wide = jpeg[p] >> 4 != 0;
                    var id = jpeg[p] & 0x0F;
                    var size = wide ? 128 : 64;
                    if (p + 1 + size > end) break;
                    if (id == 0)
                    {
                        var table = new int[64];
                        for (var k = 0; k < 64; k++) table[k] = wide ? jpeg[p + 1 + k * 2] << 8 | jpeg[p + 2 + k * 2] : jpeg[p + 1 + k];
                        return table;
                    }
                    p += 1 + size;
                }
            at = end;
        }
        return null;
    }
}
