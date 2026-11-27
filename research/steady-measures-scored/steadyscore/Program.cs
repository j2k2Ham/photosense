// Scores decoder-steady measures (issue #55) on the ground truth, per decoder, as counts only.
// Usage: SteadyScore <truth.json> <measures-steady.jsonl> [randomPairs]
// Matching is today's PhotoMatcher (nomination by pHash, the shape check, the edited-render guard, capture time), with the
// signature difference swapped for each measure and its own limits: LIMITS_<measure>=same,unconfirmed,similar.
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Trovolve.Domain.Entities;
using Trovolve.Domain.Matching;
using Trovolve.Domain.ValueObjects;

var truth = JsonDocument.Parse(File.ReadAllText(args[0])).RootElement;
var randomPairs = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 20_000;
var measures = new Dictionary<string, JsonElement>();
foreach (var line in File.ReadLines(args[1])) { var r = JsonDocument.Parse(line).RootElement; measures[r.GetProperty("sha256").GetString()!] = r; }
var files = truth.GetProperty("files");
string[] decoders = ["magick", "wic", "wicc"];
string[] signatureMeasures = (Environment.GetEnvironmentVariable("SCORE_MEASURES") ?? "rgb,aff,grad,aff+grad,aff/grad").Split(',');
string[] sharpnessMeasures = ["lap", "lap256", "ten", "tenn"];
var detail = Environment.GetEnvironmentVariable("SCORE_DETAIL");

(double Same, double Unconfirmed, double Similar) Limits(string m)
{
    var s = Environment.GetEnvironmentVariable("LIMITS_" + m.Replace("+", "_"));
    if (s is null) return (0.5, 0.2, 2.0);
    var p = s.Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
    return (p[0], p[1], p[2]);
}

string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

DateTime? Taken(JsonElement meta)
{
    if (Str(meta, "taken") is not { } t || !DateTime.TryParseExact(t, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var taken)) return null;
    if (Str(meta, "subsec")?.Trim() is { Length: > 0 } sub && sub.All(char.IsAsciiDigit))
        taken = taken.AddTicks(long.Parse(sub.Length > 7 ? sub[..7] : sub.PadRight(7, '0'), CultureInfo.InvariantCulture));
    return taken;
}

var cache = new Dictionary<(string, string), Pic?>();
Pic? PicOf(string sha, string decoder)
{
    if (cache.TryGetValue((sha, decoder), out var hit)) return hit;
    Pic? pic = null;
    if (measures.TryGetValue(sha, out var m) && (m.TryGetProperty(decoder, out var d) || (decoder == "wicc" && m.TryGetProperty("wic", out d))))
    {
        var f = files.GetProperty(sha);
        var path = f.GetProperty("path").GetString()!;
        pic = new Pic(Path.GetFileName(path), d.GetProperty("w").GetInt32(), d.GetProperty("h").GetInt32(),
            ulong.Parse(d.GetProperty("hash").GetString()!, NumberStyles.HexNumber), Convert.FromHexString(d.GetProperty("sig").GetString()!),
            Taken(f.GetProperty("meta")),
            sharpnessMeasures.ToDictionary(s => s, s => d.GetProperty(s).GetDouble()));
    }
    return cache[(sha, decoder)] = pic;
}

// Today's PhotoMatcher.Compare with the difference measured another way. Null: not matched.
(MatchKind Kind, double Difference)? Judge(Pic a, Pic b, string measure)
{
    var (same, unconfirmed, similar) = Limits(measure);
    if (BitOperations.PopCount(a.Hash ^ b.Hash) > PhotoMatcher.CandidateBits) return null;
    double ra = (double)a.W / a.H, rb = (double)b.W / b.H;
    if (Math.Abs(ra - rb) / Math.Max(ra, rb) > PhotoMatcher.AspectTolerance) return null;
    if (measure == "aff/grad")
    {
        var gradient = Difference(a, b, "grad");
        if (gradient > similar) return null;
        var luma = Difference(a, b, "aff");
        return (IsSame(a, b, luma, same, unconfirmed) ? MatchKind.SamePicture : MatchKind.Similar, luma);
    }
    var difference = Difference(a, b, measure);
    if (difference > similar) return null;
    return (IsSame(a, b, difference, same, unconfirmed) ? MatchKind.SamePicture : MatchKind.Similar, difference);
}

static bool IsSame(Pic a, Pic b, double difference, double same, double unconfirmed)
{
    if (PhotoNaming.IsEditedRender(a.Name) != PhotoNaming.IsEditedRender(b.Name)) return false;
    if (a.Taken is { } ta && b.Taken is { } tb)
    {
        var tolerance = ta.Millisecond != 0 && tb.Millisecond != 0 ? TimeSpan.FromMilliseconds(10) : TimeSpan.FromSeconds(1);
        return (ta - tb).Duration() <= tolerance && difference <= same;
    }
    if (a.Taken.HasValue != b.Taken.HasValue) return difference <= same;
    return difference <= unconfirmed;
}

static double Difference(Pic a, Pic b, string measure) => measure switch
{
    "rgb" => PhotoMatcher.SignatureDifference(a.Sig, b.Sig),
    "aff" => Normalised(a.Luma, b.Luma),
    "grad" => Normalised(a.Gradient, b.Gradient),
    "aff/grad" => Normalised(a.Luma, b.Luma),
    _ => Math.Max(Normalised(a.Luma, b.Luma), Normalised(a.Gradient, b.Gradient)),
};

// Each picture's values with its own mean and contrast taken out, the difference given back in grey levels at the pair's
// mean contrast (issue #45's affLuma and gradNorm).
static double Normalised(double[] x, double[] y)
{
    var (mx, sx) = Stats(x); var (my, sy) = Stats(y);
    if (sx < 1e-6 || sy < 1e-6) return Math.Abs(mx - my);
    double total = 0;
    for (var i = 0; i < x.Length; i++) total += Math.Abs((x[i] - mx) / sx - (y[i] - my) / sy);
    return total / x.Length * (sx + sy) / 2;
}

static (double Mean, double Sd) Stats(double[] v)
{
    var mean = v.Average();
    return (mean, Math.Sqrt(v.Sum(t => (t - mean) * (t - mean)) / v.Length));
}

string Q(List<double> xs, bool low = false)
{
    if (xs.Count == 0) return "none";
    xs.Sort();
    return low ? string.Create(CultureInfo.InvariantCulture, $"min {xs[0]:0.00}, p10 {xs[(int)(xs.Count * 0.1)]:0.00}, median {xs[xs.Count / 2]:0.00}")
               : string.Create(CultureInfo.InvariantCulture, $"median {xs[xs.Count / 2]:0.00}, p90 {xs[(int)(xs.Count * 0.9)]:0.00}, max {xs[^1]:0.00}");
}

string KindOf(JsonElement p)
{
    var kind = Str(p, "kind") ?? "?";
    if (kind == "heic-jpeg") return p.TryGetProperty("panorama", out var pano) && pano.ValueKind == JsonValueKind.True ? "heic-jpeg panorama" : "heic-jpeg";
    return kind.StartsWith("made") ? "made copies" : $"{kind}, {Str(p, "moment")}";
}

var known = new HashSet<(string, string)>();
foreach (var p in truth.GetProperty("pairs").EnumerateArray())
{ string a = p.GetProperty("a").GetString()!, b = p.GetProperty("b").GetString()!; known.Add((a, b)); known.Add((b, a)); }

foreach (var decoder in decoders)
    foreach (var measure in signatureMeasures)
    {
        Console.WriteLine($"\n=== {decoder}, {measure}, limits {Limits(measure)} ===");
        var rows = new SortedDictionary<string, Row>();
        int sameTotal = 0, sameFound = 0, similarCalledSame = 0, similarTotal = 0;
        foreach (var p in truth.GetProperty("pairs").EnumerateArray())
        {
            if (p.TryGetProperty("doubtful", out _)) continue;
            string a = p.GetProperty("a").GetString()!, b = p.GetProperty("b").GetString()!;
            if (PicOf(a, decoder) is not { } pa || PicOf(b, decoder) is not { } pb) continue;
            var t = p.GetProperty("truth").GetString()!;
            var key = $"{t} | {KindOf(p)}";
            var row = rows.TryGetValue(key, out var r) ? r : new Row();
            var v = Judge(pa, pb, measure);
            row.N++;
            if (BitOperations.PopCount(pa.Hash ^ pb.Hash) <= PhotoMatcher.CandidateBits) row.Diff.Add(Difference(pa, pb, measure));
            if (v is null) row.None++; else if (v.Value.Kind == MatchKind.SamePicture) row.Same++; else row.Similar++;
            rows[key] = row;
            if (t == "same") { sameTotal++; if (v?.Kind == MatchKind.SamePicture) sameFound++; }
            else { similarTotal++; if (v?.Kind == MatchKind.SamePicture) similarCalledSame++; }
            if (detail == $"{decoder},{measure}" && (t == "same") != (v?.Kind == MatchKind.SamePicture))
                Console.WriteLine($"  [{key}] {v?.Kind.ToString() ?? "none"} {v?.Difference:0.00}: taken {pa.Taken is not null}/{pb.Taken is not null}");
        }
        foreach (var (key, r) in rows)
            Console.WriteLine($"{key}: {r.N} -> {r.Same} / {r.Similar} / {r.None}; difference of nominated {Q(r.Diff, key.StartsWith("similar"))}");

        var shas = files.EnumerateObject().Select(o => o.Name).Where(s => PicOf(s, decoder) is not null).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var random = new Random(41);
        int tried = 0, rSame = 0, rSimilar = 0;
        while (tried < randomPairs)
        {
            string a = shas[random.Next(shas.Count)], b = shas[random.Next(shas.Count)];
            if (a == b || known.Contains((a, b))) continue;
            var pa = PicOf(a, decoder)!; var pb = PicOf(b, decoder)!;
            if (pa.Taken is { } ta && pb.Taken is { } tb && (ta - tb).Duration() < TimeSpan.FromMinutes(10)) continue;
            tried++;
            var v = Judge(pa, pb, measure);
            if (v?.Kind == MatchKind.SamePicture) rSame++; else if (v is not null) rSimilar++;
        }
        Console.WriteLine($"TOTAL duplicates found {sameFound} of {sameTotal}; similar called duplicate {similarCalledSame} of {similarTotal}; random {tried} -> duplicate {rSame} / similar {rSimilar}");
    }

// The cost of comparing: every pair the hash nominates among the set's pictures, through each measure.
{
    var pics = files.EnumerateObject().Select(o => PicOf(o.Name, "wicc")).Where(p => p is not null).Select(p => p!).ToList();
    var nominated = new List<(Pic, Pic)>();
    for (var i = 0; i < pics.Count; i++) for (var j = i + 1; j < pics.Count; j++)
        if (BitOperations.PopCount(pics[i].Hash ^ pics[j].Hash) <= PhotoMatcher.CandidateBits) nominated.Add((pics[i], pics[j]));
    foreach (var m in new[] { "rgb", "aff", "grad" })
    {
        foreach (var (a, b) in nominated.Take(100)) Difference(a, b, m);
        var sw = System.Diagnostics.Stopwatch.StartNew(); double sink = 0;
        for (var round = 0; round < 20; round++) foreach (var (a, b) in nominated) sink += Difference(a, b, m);
        Console.WriteLine($"compare {m}: {nominated.Count} nominated pairs of {pics.Count} pictures, {sw.Elapsed.TotalMilliseconds * 1000 / (20.0 * nominated.Count):0.0} µs a pair ({sink:0})");
    }
}

// Sharpness: how steady each measure is across decoders, and how well it finds Jamie's blurry shots.
Console.WriteLine("\n=== sharpness ===");
var allShas = measures.Keys.Where(s => files.TryGetProperty(s, out _)).ToList();
foreach (var s in sharpnessMeasures)
{
    foreach (var other in new[] { "wic", "wicc" })
    {
        var ratios = allShas.Select(sha => (PicOf(sha, "magick"), PicOf(sha, other))).Where(x => x.Item1 is not null && x.Item2 is not null && x.Item1.Sharp[s] > 0)
            .Select(x => x.Item2!.Sharp[s] / x.Item1!.Sharp[s]).ToList();
        ratios.Sort();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{s}: {other}/magick same file, ratio p10 {ratios[(int)(ratios.Count * 0.1)]:0.000}, median {ratios[ratios.Count / 2]:0.000}, p90 {ratios[(int)(ratios.Count * 0.9)]:0.000}, min {ratios[0]:0.000}, max {ratios[^1]:0.000}"));
    }
    var singles = truth.GetProperty("singles").EnumerateArray().Select(x => (Sha: x.GetProperty("file").GetString()!, Blurry: x.GetProperty("truth").GetString() == "blurry")).ToList();
    double? chosen = null;
    foreach (var decoder in decoders)
    {
        var values = singles.Select(x => (Pic: PicOf(x.Sha, decoder), x.Blurry)).Where(x => x.Pic is not null).Select(x => (V: x.Pic!.Sharp[s], x.Blurry)).ToList();
        int blurry = values.Count(v => v.Blurry), sharp = values.Count - blurry;
        // The limit that best separates blurry from sharp (Youden: caught share less flagged share), set on Magick.NET.
        var candidates = values.Select(v => v.V).Distinct().OrderBy(v => v).ToList();
        double bestLimit = 0, bestScore = double.MinValue;
        foreach (var c in candidates)
        {
            var limit = c * 1.000001;
            var score = (double)values.Count(v => v.Blurry && v.V < limit) / blurry - (double)values.Count(v => !v.Blurry && v.V < limit) / sharp;
            if (score > bestScore) (bestScore, bestLimit) = (score, limit);
        }
        chosen ??= bestLimit;
        string At(double limit) => $"{values.Count(v => v.Blurry && v.V < limit)} of {blurry} caught, {values.Count(v => !v.Blurry && v.V < limit)} of {sharp} sharp flagged";
        // How well the measure ranks blurry under sharp at all (the area under the curve, 1 is perfect).
        double pairsBelow = values.Where(v => v.Blurry).Sum(b => values.Where(v => !v.Blurry).Sum(t => b.V < t.V ? 1 : b.V == t.V ? 0.5 : 0));
        foreach (var allowed in new[] { 0, 2, 4, 8 })
        {
            var limit = candidates.Select(c => c * 1.000001).Where(l => values.Count(v => !v.Blurry && v.V < l) <= allowed).DefaultIfEmpty(0).Max();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {s} {decoder} with at most {allowed} sharp flagged: limit {limit:0.###} -> {At(limit)}"));
        }
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{s} {decoder}: AUC {pairsBelow / blurry / sharp:0.000}; best limit {bestLimit:0.###} -> {At(bestLimit)}; at Magick's best ({chosen:0.###}) -> {At(chosen.Value)}{(s == "lap" ? $"; at 400 -> {At(400)}" : "")}"));
    }
}

sealed class Row { public int N, Same, Similar, None; public List<double> Diff = []; }

sealed class Pic(string name, int w, int h, ulong hash, byte[] sig, DateTime? taken, Dictionary<string, double> sharp)
{
    public string Name = name; public int W = w, H = h; public ulong Hash = hash; public byte[] Sig = sig; public DateTime? Taken = taken;
    public Dictionary<string, double> Sharp = sharp;
    public double[] Luma = [.. Enumerable.Range(0, sig.Length / 3).Select(i => 0.299 * sig[i * 3] + 0.587 * sig[i * 3 + 1] + 0.114 * sig[i * 3 + 2])];
    public double[] Gradient => gradient ??= Gradients();
    double[]? gradient;
    double[] Gradients()
    {
        const int n = 32;
        var g = new List<double>(2 * n * (n - 1));
        for (var y = 0; y < n; y++) for (var x = 0; x < n - 1; x++) g.Add(Luma[y * n + x + 1] - Luma[y * n + x]);
        for (var y = 0; y < n - 1; y++) for (var x = 0; x < n; x++) g.Add(Luma[(y + 1) * n + x] - Luma[y * n + x]);
        return [.. g];
    }
}
