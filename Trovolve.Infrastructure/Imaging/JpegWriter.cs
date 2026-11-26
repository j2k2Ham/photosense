namespace Trovolve.Infrastructure.Imaging;

/// <summary>
/// Writes pictures as baseline JPEG, which every browser and system shows: Trovolve's previews, and pictures a browser
/// cannot show itself, such as HEIC. Written here rather than by each system's own encoder, so that a preview is the
/// same on every computer. Colour at full resolution (4:4:4), with the standard tables of the JPEG standard's Annex K.
/// </summary>
public static class JpegWriter
{
    // The quantization tables of Annex K, in natural order, which a quality scales.
    private static readonly byte[] LuminanceBase =
    [
        16, 11, 10, 16, 24, 40, 51, 61, 12, 12, 14, 19, 26, 58, 60, 55, 14, 13, 16, 24, 40, 57, 69, 56, 14, 17, 22, 29, 51, 87, 80, 62,
        18, 22, 37, 56, 68, 109, 103, 77, 24, 35, 55, 64, 81, 104, 113, 92, 49, 64, 78, 87, 103, 121, 120, 101, 72, 92, 95, 98, 112, 100, 103, 99,
    ];
    private static readonly byte[] ChrominanceBase =
    [
        17, 18, 24, 47, 99, 99, 99, 99, 18, 21, 26, 66, 99, 99, 99, 99, 24, 26, 56, 99, 99, 99, 99, 99, 47, 66, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99,
    ];

    /// <summary>Where each coefficient, in the order a JPEG writes them, is in a block read row by row.</summary>
    public static readonly byte[] ZigZag =
    [
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
    ];

    // The Huffman tables of Annex K: how many codes of each length, then the values they stand for.
    private static readonly byte[] DcLuminanceCounts = [0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] DcChrominanceCounts = [0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0];
    private static readonly byte[] DcValues = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];
    private static readonly byte[] AcLuminanceCounts = [0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 0x7d];
    private static readonly byte[] AcLuminanceValues =
    [
        0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12, 0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07, 0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xa1, 0x08,
        0x23, 0x42, 0xb1, 0xc1, 0x15, 0x52, 0xd1, 0xf0, 0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0a, 0x16, 0x17, 0x18, 0x19, 0x1a, 0x25, 0x26, 0x27, 0x28,
        0x29, 0x2a, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59,
        0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7a, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
        0x8a, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7, 0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4, 0xb5, 0xb6,
        0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3, 0xc4, 0xc5, 0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2, 0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda, 0xe1, 0xe2,
        0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9, 0xea, 0xf1, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8, 0xf9, 0xfa,
    ];
    private static readonly byte[] AcChrominanceCounts = [0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 0x77];
    private static readonly byte[] AcChrominanceValues =
    [
        0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21, 0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71, 0x13, 0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91,
        0xa1, 0xb1, 0xc1, 0x09, 0x23, 0x33, 0x52, 0xf0, 0x15, 0x62, 0x72, 0xd1, 0x0a, 0x16, 0x24, 0x34, 0xe1, 0x25, 0xf1, 0x17, 0x18, 0x19, 0x1a, 0x26,
        0x27, 0x28, 0x29, 0x2a, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58,
        0x59, 0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7a, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
        0x88, 0x89, 0x8a, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7, 0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4,
        0xb5, 0xb6, 0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3, 0xc4, 0xc5, 0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2, 0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda,
        0xe2, 0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9, 0xea, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8, 0xf9, 0xfa,
    ];

    private static readonly Code[] DcLuminance = Codes(DcLuminanceCounts, DcValues);
    private static readonly Code[] DcChrominance = Codes(DcChrominanceCounts, DcValues);
    private static readonly Code[] AcLuminance = Codes(AcLuminanceCounts, AcLuminanceValues);
    private static readonly Code[] AcChrominance = Codes(AcChrominanceCounts, AcChrominanceValues);

    // The cosines of the forward DCT, each with its scale: Cosine[u * 8 + x] = C(u) / 2 × cos((2x + 1)uπ / 16).
    private static readonly double[] Cosine = MakeCosines();

    private readonly record struct Code(ushort Bits, byte Length);

    /// <summary>A luminance table of Annex K scaled to a quality, as libjpeg and most encoders scale it.</summary>
    /// <param name="quality">1 to 100: 50 is the table as given, higher is finer.</param>
    public static byte[] LuminanceTable(int quality) => Scaled(LuminanceBase, quality);

    /// <summary>The picture as a baseline JPEG.</summary>
    /// <param name="rgb">Its pixels, three bytes each (red, green, blue), row by row.</param>
    /// <param name="width">Its width, 1 to 65,535.</param>
    /// <param name="height">Its height, 1 to 65,535.</param>
    /// <param name="quality">1 to 100.</param>
    public static byte[] Write(ReadOnlySpan<byte> rgb, int width, int height, int quality)
    {
        var luminance = Scaled(LuminanceBase, quality);
        var chrominance = Scaled(ChrominanceBase, quality);
        var output = new MemoryStream();
        output.Write([0xFF, 0xD8]);
        // JFIF, version 1.1, with no density and no thumbnail of its own.
        output.Write([0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0]);
        output.Write([0xFF, 0xDB, 0, 132, 0]);
        foreach (var k in ZigZag) output.WriteByte(luminance[k]);
        output.WriteByte(1);
        foreach (var k in ZigZag) output.WriteByte(chrominance[k]);
        output.Write([0xFF, 0xC0, 0, 17, 8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 3, 1, 0x11, 0, 2, 0x11, 1, 3, 0x11, 1]);
        var tables = new (byte Id, byte[] Counts, byte[] Values)[]
        {
            (0x00, DcLuminanceCounts, DcValues), (0x10, AcLuminanceCounts, AcLuminanceValues),
            (0x01, DcChrominanceCounts, DcValues), (0x11, AcChrominanceCounts, AcChrominanceValues),
        };
        var length = 2 + tables.Sum(t => 17 + t.Values.Length);
        output.Write([0xFF, 0xC4, (byte)(length >> 8), (byte)length]);
        foreach (var (id, counts, values) in tables)
        {
            output.WriteByte(id);
            output.Write(counts);
            output.Write(values);
        }
        output.Write([0xFF, 0xDA, 0, 12, 3, 1, 0x00, 2, 0x11, 3, 0x11, 0, 63, 0]);

        var bits = new BitWriter(output);
        Span<double> y = stackalloc double[64], cb = stackalloc double[64], cr = stackalloc double[64];
        int lastY = 0, lastCb = 0, lastCr = 0;
        for (var top = 0; top < height; top += 8)
            for (var left = 0; left < width; left += 8)
            {
                Block(rgb, width, height, left, top, y, cb, cr);
                lastY = Encode(bits, y, luminance, lastY, DcLuminance, AcLuminance);
                lastCb = Encode(bits, cb, chrominance, lastCb, DcChrominance, AcChrominance);
                lastCr = Encode(bits, cr, chrominance, lastCr, DcChrominance, AcChrominance);
            }
        bits.Finish();
        output.Write([0xFF, 0xD9]);
        return output.ToArray();
    }

    // The 8 × 8 block at (left, top) in the JPEG's colours (YCbCr), centred on nothing; a block past the picture's edge
    // repeats its last row and column, as encoders do, so that the edge shows no stray colour.
    private static void Block(ReadOnlySpan<byte> rgb, int width, int height, int left, int top, Span<double> y, Span<double> cb, Span<double> cr)
    {
        for (var row = 0; row < 8; row++)
            for (var column = 0; column < 8; column++)
            {
                var at = (Math.Min(top + row, height - 1) * width + Math.Min(left + column, width - 1)) * 3;
                double r = rgb[at], g = rgb[at + 1], b = rgb[at + 2];
                var i = row * 8 + column;
                y[i] = 0.299 * r + 0.587 * g + 0.114 * b - 128;
                cb[i] = -0.168736 * r - 0.331264 * g + 0.5 * b;
                cr[i] = 0.5 * r - 0.418688 * g - 0.081312 * b;
            }
    }

    // Transforms, quantizes and writes one block, returning its DC value, from which the next block's is told.
    private static int Encode(BitWriter bits, ReadOnlySpan<double> block, byte[] table, int lastDc, Code[] dc, Code[] ac)
    {
        Span<double> rows = stackalloc double[64];
        Span<int> quantized = stackalloc int[64];
        for (var y = 0; y < 8; y++)
            for (var u = 0; u < 8; u++)
            {
                double sum = 0;
                for (var x = 0; x < 8; x++) sum += Cosine[u * 8 + x] * block[y * 8 + x];
                rows[y * 8 + u] = sum;
            }
        for (var v = 0; v < 8; v++)
            for (var u = 0; u < 8; u++)
            {
                double sum = 0;
                for (var y = 0; y < 8; y++) sum += Cosine[v * 8 + y] * rows[y * 8 + u];
                quantized[v * 8 + u] = (int)Math.Round(sum / table[v * 8 + u]);
            }

        var value = quantized[0];
        Write(bits, dc, value - lastDc, 0);
        var run = 0;
        for (var k = 1; k < 64; k++)
        {
            var coefficient = quantized[ZigZag[k]];
            if (coefficient == 0) { run++; continue; }
            // Sixteen zeros in a row are one code of their own.
            for (; run >= 16; run -= 16) bits.Put(ac[0xF0]);
            Write(bits, ac, coefficient, run);
            run = 0;
        }
        // The rest of the block is zeros: the end of the block says so.
        if (run > 0) bits.Put(ac[0x00]);
        return value;
    }

    // A value as its size, preceded by the zeros before it, then its bits.
    private static void Write(BitWriter bits, Code[] table, int value, int run)
    {
        var magnitude = Math.Abs(value);
        var size = magnitude == 0 ? 0 : 32 - int.LeadingZeroCount(magnitude);
        bits.Put(table[(run << 4) | size]);
        if (size > 0) bits.Put(new Code((ushort)(value >= 0 ? value : value + (1 << size) - 1), (byte)size));
    }

    private static Code[] Codes(byte[] counts, byte[] values)
    {
        var codes = new Code[256];
        int code = 0, next = 0;
        for (var length = 1; length <= 16; length++)
        {
            for (var i = 0; i < counts[length - 1]; i++) codes[values[next++]] = new Code((ushort)code++, (byte)length);
            code <<= 1;
        }
        return codes;
    }

    private static byte[] Scaled(byte[] table, int quality)
    {
        quality = Math.Clamp(quality, 1, 100);
        var scale = quality < 50 ? 5000 / quality : 200 - quality * 2;
        return [.. table.Select(t => (byte)Math.Clamp((t * scale + 50) / 100, 1, 255))];
    }

    private static double[] MakeCosines()
    {
        var cosines = new double[64];
        for (var u = 0; u < 8; u++)
            for (var x = 0; x < 8; x++)
                cosines[u * 8 + x] = (u == 0 ? Math.Sqrt(0.5) : 1) / 2 * Math.Cos((2 * x + 1) * u * Math.PI / 16);
        return cosines;
    }

    /// <summary>Writes codes bit by bit, a zero after every 0xFF so that no marker appears in the picture's data.</summary>
    private sealed class BitWriter(Stream output)
    {
        private int _buffer;
        private int _count;

        public void Put(Code code)
        {
            for (var i = code.Length - 1; i >= 0; i--)
            {
                _buffer = (_buffer << 1) | ((code.Bits >> i) & 1);
                if (++_count == 8) Flush();
            }
        }

        // The last byte filled out with ones, as the standard asks.
        public void Finish()
        {
            while (_count != 0) Put(new Code(1, 1));
        }

        private void Flush()
        {
            output.WriteByte((byte)_buffer);
            if (_buffer == 0xFF) output.WriteByte(0);
            (_buffer, _count) = (0, 0);
        }
    }
}
