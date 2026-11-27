// Copied from tools/wicfix (issue #54): the corrected WIC rendering and the HEIF colour box reader.
using System.Buffers.Binary;
using System.Text;
using Trovolve.Domain.Services;
using Trovolve.Domain.ValueObjects;

sealed class Given(DecodedPicture picture) : IPictureDecoder
{
    public Task<DecodedPicture> DecodeAsync(byte[] bytes, PictureFormat format, CancellationToken ct) => Task.FromResult(picture);
}

static class Correct
{
    // WIC's HEIF decoding against libheif (issue #43): R -0.8, G -0.25, B -1.1 grey levels.
    static readonly double[] Bias = [0.8, 0.25, 1.1];

    // An 8x8 Bayer matrix: rounding against it keeps a block's mean, fraction and all.
    static readonly double[] Bayer = [.. new[] {
        0, 32, 8, 40, 2, 34, 10, 42, 48, 16, 56, 24, 50, 18, 58, 26, 12, 44, 4, 36, 14, 46, 6, 38, 60, 28, 52, 20, 62, 30, 54, 22,
        3, 35, 11, 43, 1, 33, 9, 41, 51, 19, 59, 27, 49, 17, 57, 25, 15, 47, 7, 39, 13, 45, 5, 37, 63, 31, 55, 23, 61, 29, 53, 21 }
        .Select(v => (v + 0.5) / 64)];

    static readonly double[,] SrgbToP3 = { { 0.8224621, 0.1775380, 0 }, { 0.0331941, 0.9668058, 0 }, { 0.0170827, 0.0723974, 0.9105199 } };
    static readonly double[,] P3ToSrgb = { { 1.2249401, -0.2249404, 0 }, { -0.0420569, 1.0420571, 0 }, { -0.0196376, -0.0786361, 1.0982735 } };
    static double SrgbToLinear(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    static double LinearToSrgb(double l) => l <= 0.0031308 ? 12.92 * l : 1.055 * Math.Pow(l, 1 / 2.4) - 0.055;
    static double Bt709Oetf(double l) => l < 0.018 ? 4.5 * l : 1.099 * Math.Pow(l, 0.45) - 0.099;

    public static double ClippedShare(DecodedPicture p)
    {
        long clipped = 0;
        for (var i = 0; i < p.Rgb.Length; i += 3)
            if (p.Rgb[i] is 0 or 255 || p.Rgb[i + 1] is 0 or 255 || p.Rgb[i + 2] is 0 or 255) clipped++;
        return Math.Round(clipped * 3.0 / p.Rgb.Length, 4);
    }

    public static DecodedPicture Picture(DecodedPicture p, bool nclx, bool wide, bool p3)
    {
        var output = new byte[p.Rgb.Length];
        var cache = new Dictionary<int, (double R, double G, double B)>();
        for (var y = 0; y < p.Height; y++)
            for (var x = 0; x < p.Width; x++)
            {
                var i = (y * p.Width + x) * 3;
                double r, g, b;
                if (!nclx) (r, g, b) = (p.Rgb[i] + Bias[0], p.Rgb[i + 1] + Bias[1], p.Rgb[i + 2] + Bias[2]);
                else
                {
                    var key = p.Rgb[i] << 16 | p.Rgb[i + 1] << 8 | p.Rgb[i + 2];
                    if (!cache.TryGetValue(key, out var v)) cache[key] = v = Nclx(p.Rgb[i], p.Rgb[i + 1], p.Rgb[i + 2], wide, p3);
                    (r, g, b) = v;
                }
                var t = Bayer[(y & 7) * 8 + (x & 7)];
                output[i] = Round(r, t); output[i + 1] = Round(g, t); output[i + 2] = Round(b, t);
            }
        return new DecodedPicture(p.Width, p.Height, output);
    }

    static byte Round(double v, double threshold)
    {
        var f = Math.Floor(v);
        return (byte)Math.Clamp(v - f >= threshold ? f + 1 : f, 0, 255);
    }

    // Undoes WIC's literal reading (BT.709 curve, P3 to sRGB, the sRGB curve): back to the stream's values, which libheif
    // shows as they are; or, for p3, those values read as Display P3 with the sRGB curve and converted to sRGB.
    static (double, double, double) Nclx(byte R, byte G, byte B, bool wide, bool p3)
    {
        double[] s = [SrgbToLinear((R + Bias[0]) / 255), SrgbToLinear((G + Bias[1]) / 255), SrgbToLinear((B + Bias[2]) / 255)];
        double[] v = [.. (wide ? Mul(SrgbToP3, s) : s).Select(l => Bt709Oetf(Math.Clamp(l, 0, 1)))];
        if (!p3) return (v[0] * 255, v[1] * 255, v[2] * 255);
        var srgb = Mul(P3ToSrgb, [.. v.Select(SrgbToLinear)]);
        return (LinearToSrgb(Math.Clamp(srgb[0], 0, 1)) * 255, LinearToSrgb(Math.Clamp(srgb[1], 0, 1)) * 255, LinearToSrgb(Math.Clamp(srgb[2], 0, 1)) * 255);
    }

    static double[] Mul(double[,] m, double[] v) =>
        [m[0, 0] * v[0] + m[0, 1] * v[1] + m[0, 2] * v[2], m[1, 0] * v[0] + m[1, 1] * v[1] + m[1, 2] * v[2], m[2, 0] * v[0] + m[2, 1] * v[1] + m[2, 2] * v[2]];
}

// The primary item's colour boxes, read from the HEIF boxes (meta: pitm, iprp/ipco, ipma).
static class Heif
{
    public static string PrimaryColour(byte[] f)
    {
        var meta = Find(f, 0, f.Length, "meta") ?? throw new InvalidDataException("no meta");
        var start = meta.Body + 4; // a full box
        var pitm = Find(f, start, meta.End, "pitm")!.Value;
        var primary = f[pitm.Body] == 0 ? BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(pitm.Body + 4)) : (int)BinaryPrimitives.ReadUInt32BigEndian(f.AsSpan(pitm.Body + 4));
        var iprp = Find(f, start, meta.End, "iprp")!.Value;
        var ipco = Find(f, iprp.Body, iprp.End, "ipco")!.Value;
        var props = new List<(string Type, int Body, int End)>();
        for (var at = ipco.Body; at < ipco.End;) { var b = Box(f, at); props.Add(b); at = b.End; }
        var ipma = Find(f, iprp.Body, iprp.End, "ipma")!.Value;
        int version = f[ipma.Body], flags = f[ipma.Body + 3], pos = ipma.Body + 4;
        var count = (int)BinaryPrimitives.ReadUInt32BigEndian(f.AsSpan(pos)); pos += 4;
        var found = new List<string>();
        for (var e = 0; e < count; e++)
        {
            int id;
            if (version < 1) { id = BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(pos)); pos += 2; }
            else { id = (int)BinaryPrimitives.ReadUInt32BigEndian(f.AsSpan(pos)); pos += 4; }
            int n = f[pos++];
            for (var k = 0; k < n; k++)
            {
                int index;
                if ((flags & 1) != 0) { index = BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(pos)) & 0x7fff; pos += 2; }
                else { index = f[pos++] & 0x7f; }
                if (id != primary || index == 0) continue;
                var p = props[index - 1];
                if (p.Type != "colr") continue;
                var kind = Encoding.ASCII.GetString(f, p.Body, 4);
                found.Add(kind == "nclx"
                    ? $"nclx {BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(p.Body + 4))}/{BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(p.Body + 6))}/{BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(p.Body + 8))}"
                    : "icc");
            }
        }
        return found.Count == 0 ? "none" : string.Join("+", found.Order());
    }

    static (string Type, int Body, int End) Box(byte[] f, int at)
    {
        long size = BinaryPrimitives.ReadUInt32BigEndian(f.AsSpan(at));
        var type = Encoding.ASCII.GetString(f, at + 4, 4);
        var body = at + 8;
        if (size == 1) { size = (long)BinaryPrimitives.ReadUInt64BigEndian(f.AsSpan(at + 8)); body += 8; }
        else if (size == 0) size = f.Length - at;
        return (type, body, (int)(at + size));
    }

    static (string Type, int Body, int End)? Find(byte[] f, int from, int to, string type)
    {
        for (var at = from; at + 8 <= to;) { var b = Box(f, at); if (b.Type == type) return b; if (b.End <= at) break; at = b.End; }
        return null;
    }
}
