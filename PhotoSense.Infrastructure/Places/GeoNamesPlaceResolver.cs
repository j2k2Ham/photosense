using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Places;

/// <summary>
/// Names the nearest town to a position from a list bundled with the application, so that where a
/// picture was taken never has to be sent to an online service.
/// The list is the GeoNames "cities1000" extract (places of 1,000 people or more), licensed CC BY 4.0,
/// https://www.geonames.org/.
/// </summary>
public sealed class GeoNamesPlaceResolver : IPlaceNameResolver
{
    private const string Resource = "PhotoSense.Infrastructure.Places.places.tsv.gz";
    /// <summary>Within this distance the picture is said to be in the place; beyond it, near the place.</summary>
    public const double InsideKm = 3;
    /// <summary>Beyond this distance no place is named.</summary>
    public const double NearKm = 80;
    private const double KmPerDegree = 111.2;

    private readonly Lazy<Index> _index;

    public GeoNamesPlaceResolver() : this(() => OpenBundledList(typeof(GeoNamesPlaceResolver).Assembly)) { }

    /// <param name="openList">Opens tab-separated lines of name, region, country code, latitude, longitude.</param>
    public GeoNamesPlaceResolver(Func<TextReader> openList) => _index = new Lazy<Index>(() => Index.Load(openList));

    public string? Describe(double latitude, double longitude)
    {
        if (double.IsNaN(latitude) || double.IsNaN(longitude) || Math.Abs(latitude) > 90 || Math.Abs(longitude) > 180) return null;
        var index = _index.Value;
        var eastWest = Math.Max(0.01, Math.Cos(latitude * Math.PI / 180));

        // Places are filed by whole degree; look through every cell that can hold one within reach.
        int row = Index.Row(latitude), column = Index.Column(longitude);
        int rows = (int)Math.Ceiling(NearKm / KmPerDegree), columns = Math.Min(180, (int)Math.Ceiling(NearKm / (KmPerDegree * eastWest)));
        int best = -1;
        double bestKm = double.MaxValue;
        for (int r = Math.Max(0, row - rows); r <= Math.Min(179, row + rows); r++)
            for (int c = column - columns; c <= column + columns; c++)
                foreach (var i in index.Cell(r, ((c % 360) + 360) % 360))
                {
                    double north = (index.Latitude[i] - latitude) * KmPerDegree;
                    double east = Math.Abs(index.Longitude[i] - longitude);
                    east = Math.Min(east, 360 - east) * KmPerDegree * eastWest;
                    var km = Math.Sqrt(north * north + east * east);
                    if (km < bestKm) (best, bestKm) = (i, km);
                }

        if (best < 0 || bestKm > NearKm) return null;
        return bestKm <= InsideKm ? index.Name[best] : $"Near {index.Name[best]}";
    }

    /// <summary>Opens the list built into <paramref name="assembly"/>, saying so plainly when a build left it out.</summary>
    public static TextReader OpenBundledList(Assembly assembly)
    {
        var stream = assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"The place list ({Resource}) is missing from the build.");
        return new StreamReader(new GZipStream(stream, CompressionMode.Decompress));
    }

    private sealed class Index
    {
        public readonly List<string> Name = [];
        public readonly List<float> Latitude = [];
        public readonly List<float> Longitude = [];
        private readonly List<int>?[] _cells = new List<int>?[180 * 360];

        public static int Row(double latitude) => Math.Clamp((int)Math.Floor(latitude) + 90, 0, 179);
        public static int Column(double longitude) => Math.Clamp((int)Math.Floor(longitude) + 180, 0, 359);
        public IReadOnlyList<int> Cell(int row, int column) => _cells[row * 360 + column] ?? (IReadOnlyList<int>)[];

        public static Index Load(Func<TextReader> open)
        {
            var index = new Index();
            using var reader = open();
            while (reader.ReadLine() is { } line)
            {
                var parts = line.Split('\t');
                if (parts.Length < 5
                    || !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
                    || !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)) continue;
                // "Buxton, North Carolina, US"; the region is left out where the list has none.
                index.Name.Add(string.Join(", ", new[] { parts[0], parts[1], parts[2] }.Where(p => p.Length > 0)));
                index.Latitude.Add(latitude);
                index.Longitude.Add(longitude);
                (index._cells[Row(latitude) * 360 + Column(longitude)] ??= []).Add(index.Name.Count - 1);
            }
            return index;
        }
    }
}
