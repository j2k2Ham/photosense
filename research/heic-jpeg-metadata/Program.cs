// Reads every HEIC and JPEG under a folder (read only) and writes, one JSON line per file:
// its metadata tags (raw values) and, with "pixels", a small grey signature and a 64-bit dHash.
// Usage: Probe <folder> <out.jsonl> [pixels]
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using ImageMagick;
using MetadataExtractor;
using Directory = MetadataExtractor.Directory;

var root = args[0];
var outPath = args[1];
var pixels = args.Length > 2 && args[2] == "pixels";
var files = System.IO.Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
    .Where(f => args.Length > 2 && args[2] == "mov"
        ? f.EndsWith(".mov", StringComparison.OrdinalIgnoreCase)
        : f.EndsWith(".heic", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
    .ToList();
Console.WriteLine($"{files.Count} files");
var lines = new ConcurrentBag<string>();
int done = 0;
Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = pixels ? 8 : 4 }, f =>
{
    var rec = new Dictionary<string, object?>
    {
        ["path"] = Path.GetRelativePath(root, f),
        ["size"] = new FileInfo(f).Length,
    };
    var tags = new List<object>();
    try
    {
        IReadOnlyList<Directory> dirs;
        using (var s = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            dirs = ImageMetadataReader.ReadMetadata(s);
        foreach (var x in dirs.OfType<MetadataExtractor.Formats.Xmp.XmpDirectory>())
            foreach (var p in x.GetXmpProperties())
                tags.Add(new { d = "XMPP", id = 0, n = p.Key, v = (string?)p.Value, desc = (string?)null });
        foreach (var d in dirs)
            foreach (var t in d.Tags)
            {
                var raw = d.GetObject(t.Type);
                tags.Add(new { d = d.Name, id = t.Type, n = t.Name, v = Show(raw), desc = t.Description });
            }
    }
    catch (Exception ex) { rec["metaError"] = ex.GetType().Name; }
    rec["tags"] = tags;
    if (pixels)
    {
        try
        {
            using var img = new MagickImage(f);
            img.AutoOrient();
            rec["w"] = (int)img.Width; rec["h"] = (int)img.Height;
            img.ColorSpace = ColorSpace.sRGB;
            img.Grayscale();
            using var sig = img.Clone();
            sig.Resize(new MagickGeometry(32, 32) { IgnoreAspectRatio = true });
            rec["sig"] = Convert.ToHexString(sig.GetPixels().ToByteArray(PixelMapping.RGB)!.Where((_, i) => i % 3 == 0).ToArray());
            using var dh = img.Clone();
            dh.Resize(new MagickGeometry(9, 8) { IgnoreAspectRatio = true });
            var g = dh.GetPixels().ToByteArray(PixelMapping.RGB)!.Where((_, i) => i % 3 == 0).ToArray();
            ulong h = 0;
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) h = (h << 1) | (g[y * 9 + x] < g[y * 9 + x + 1] ? 1UL : 0UL);
            rec["dhash"] = h.ToString("x16");
        }
        catch (Exception ex) { rec["pixError"] = ex.GetType().Name; }
    }
    lines.Add(JsonSerializer.Serialize(rec));
    var n = Interlocked.Increment(ref done);
    if (n % 250 == 0) Console.WriteLine(n);
});
File.WriteAllLines(outPath, lines);
Console.WriteLine("done");

static string? Show(object? o) => o switch
{
    null => null,
    byte[] b => "hex:" + Convert.ToHexString(b.Length > 512 ? b[..512] : b),
    Array a => string.Join(",", a.Cast<object>().Select(x => x?.ToString())),
    _ => o.ToString(),
};
