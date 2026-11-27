// Prototype for issue #55: decoder-steady measures, through Magick.NET, WIC as on the branch, and WIC corrected as decided
// in issue #54 (bias added back; nclx 12/1/6 as Display P3 with the sRGB curve; nclx 2/2/6 as it is).
// Every picture is decoded whole and reduced by Trovolve's own Pixels to 512, as PictureAnalyzer does.
// Usage: Steady <out.jsonl> @<list.txt> [limit]   (each line <label>\t<full path>); a file already in out.jsonl is skipped.
// Per decoder it keeps: the 32x32 signature and pHash as today, and four sharpness measures on the 512-pixel picture:
//   lap     today's Laplacian variance in the sharpest of 4x4 tiles
//   lap256  the same after a further reduction to 256
//   ten     Tenengrad (mean squared Sobel magnitude) in the sharpest tile
//   tenn    Tenengrad divided by the tile's own luma variance, the sharpest tile (tiles flatter than variance 4 left out)
using System.Security.Cryptography;
using System.Text.Json;
using Trovolve.Desktop.Imaging;
using Trovolve.Domain.Services;
using Trovolve.Domain.ValueObjects;
using Trovolve.Infrastructure.Imaging;
using Trovolve.Tests;

var outPath = args[0];
var work = File.ReadAllLines(args[1][1..]).Where(l => l.Length > 0).Select(l => l.Split('\t')[1]).Distinct().ToList();
var seen = File.Exists(outPath) ? File.ReadLines(outPath).Select(l => JsonDocument.Parse(l).RootElement.GetProperty("file").GetString()).ToHashSet() : [];
work = work.Where(w => !seen.Contains(w)).ToList();
if (args.Length > 2) work = work.Take(int.Parse(args[2])).ToList();
Console.WriteLine($"{work.Count} to measure");
var magick = new MagickPictureDecoder();
var wic = new WicPictureDecoder();
using var writer = new StreamWriter(outPath, append: true);
var gate = new object();
int done = 0;
await Parallel.ForEachAsync(work, new ParallelOptions { MaxDegreeOfParallelism = 6 }, async (path, ct) =>
{
    var bytes = await File.ReadAllBytesAsync(path, ct);
    var rec = new Dictionary<string, object?> { ["file"] = path, ["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)) };
    var format = PictureSignature.Of(bytes.AsSpan(0, Math.Min(bytes.Length, PictureSignature.Needed)))!.Value;
    rec["fmt"] = format.ToString();
    try
    {
        var t = System.Diagnostics.Stopwatch.StartNew();
        rec["magick"] = Measure(await magick.DecodeAsync(bytes, format, ct), t);
        t.Restart();
        var raw = await wic.DecodeAsync(bytes, format, ct);
        rec["wic"] = Measure(raw, t);
        if (format == PictureFormat.Heic)
        {
            var colour = Heif.PrimaryColour(bytes);
            rec["colour"] = colour;
            var nclx = colour is "nclx 12/1/6" or "nclx 2/2/6";
            var wide = colour == "nclx 12/1/6";
            rec["wicc"] = Measure(Correct.Picture(raw, nclx, wide, p3: wide), t);
        }
    }
    catch (Exception ex) { rec["error"] = ex.GetType().Name + ": " + ex.Message; }
    var json = JsonSerializer.Serialize(rec);
    lock (gate) { writer.WriteLine(json); if (++done % 100 == 0) { writer.Flush(); Console.WriteLine(done); } }
});
Console.WriteLine("done");

static object Measure(DecodedPicture whole, System.Diagnostics.Stopwatch t)
{
    var work = Reduce(whole, ImageFingerprint.WorkEdge);
    var signature = Pixels.Resize(work.Rgb, work.Width, work.Height, ImageFingerprint.SignatureEdge, ImageFingerprint.SignatureEdge);
    var half = Reduce(work, ImageFingerprint.WorkEdge / 2);
    var luma = Luma(work);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var (ten, tenn) = Tenengrad(luma, work.Width, work.Height);
    var tenMs = sw.Elapsed.TotalMilliseconds;
    return new
    {
        w = whole.Width, h = whole.Height,
        sig = Convert.ToHexString(signature), hash = ImageFingerprint.PerceptualHash(signature).ToString("x16"),
        lap = ImageFingerprint.Sharpness(work.Rgb, work.Width, work.Height),
        lap256 = ImageFingerprint.Sharpness(half.Rgb, half.Width, half.Height),
        ten, tenn, tenMs = Math.Round(tenMs, 2), ms = t.ElapsedMilliseconds,
    };
}

static DecodedPicture Reduce(DecodedPicture p, int maxEdge)
{
    if (Math.Max(p.Width, p.Height) <= maxEdge) return p;
    var (across, down) = DecodedPicture.Fit(p.Width, p.Height, maxEdge);
    return new DecodedPicture(across, down, Pixels.Resize(p.Rgb, p.Width, p.Height, across, down));
}

static double[] Luma(DecodedPicture p)
{
    var luma = new double[p.Width * p.Height];
    for (var i = 0; i < luma.Length; i++) luma[i] = 0.299 * p.Rgb[i * 3] + 0.587 * p.Rgb[i * 3 + 1] + 0.114 * p.Rgb[i * 3 + 2];
    return luma;
}

// Tenengrad in the sharpest of the same 4x4 tiles today's measure uses; and divided by the tile's own luma variance.
static (double Ten, double TenNorm) Tenengrad(double[] l, int width, int height)
{
    const int tiles = ImageFingerprint.SharpnessTiles;
    if (width < tiles * 3 || height < tiles * 3) return (0, 0);
    double best = 0, bestNorm = 0;
    for (var ty = 0; ty < tiles; ty++)
        for (var tx = 0; tx < tiles; tx++)
        {
            int x0 = Math.Max(1, tx * width / tiles), x1 = Math.Min(width - 1, (tx + 1) * width / tiles);
            int y0 = Math.Max(1, ty * height / tiles), y1 = Math.Min(height - 1, (ty + 1) * height / tiles);
            double grad = 0, sum = 0, squares = 0;
            for (var y = y0; y < y1; y++)
                for (var x = x0; x < x1; x++)
                {
                    var a = y * width + x;
                    var gx = l[a - width + 1] + 2 * l[a + 1] + l[a + width + 1] - l[a - width - 1] - 2 * l[a - 1] - l[a + width - 1];
                    var gy = l[a + width - 1] + 2 * l[a + width] + l[a + width + 1] - l[a - width - 1] - 2 * l[a - width] - l[a - width + 1];
                    grad += gx * gx + gy * gy;
                    sum += l[a]; squares += l[a] * l[a];
                }
            var n = (double)(x1 - x0) * (y1 - y0);
            var ten = grad / n;
            var variance = squares / n - (sum / n) * (sum / n);
            best = Math.Max(best, ten);
            if (variance >= 4) bestNorm = Math.Max(bestNorm, ten / variance);
        }
    return (best, bestNorm);
}
