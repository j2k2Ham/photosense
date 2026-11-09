using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Places;

/// <summary>
/// Names the nearest town to a position, and the landmark or area a position is at, from lists bundled
/// with the application, so that where a picture was taken never has to be sent to an online service.
/// The lists are extracts of GeoNames data, licensed CC BY 4.0, https://www.geonames.org/: "cities1000"
/// (places of 1,000 people or more) and a selection of sights, parks and districts.
/// </summary>
internal sealed class GeoNamesPlaceResolver : IPlaceNameResolver
{
    /// <summary>The list of towns built into the program.</summary>
    public const string Towns = "PhotoSense.Infrastructure.Places.places.tsv.gz";
    /// <summary>The list of landmarks built into the program.</summary>
    public const string Landmarks = "PhotoSense.Infrastructure.Places.landmarks.tsv.gz";
    /// <summary>Within this distance the picture is said to be in the place; beyond it, near the place.</summary>
    public const double InsideKm = 3;
    /// <summary>Beyond this distance no place is named.</summary>
    public const double NearKm = 80;
    private const double KmPerDegree = 111.2;

    private readonly Lazy<Index> _index;
    private readonly Lazy<LandmarkIndex> _landmarks;

    /// <summary>Names places from the lists built into the program.</summary>
    public GeoNamesPlaceResolver()
        : this(() => OpenBundledList(typeof(GeoNamesPlaceResolver).Assembly), () => OpenBundledList(typeof(GeoNamesPlaceResolver).Assembly, Landmarks)) { }

    /// <summary>Names places from the lists given, read the first time a place is asked for.</summary>
    /// <param name="openList">Opens tab-separated lines of name, region, country code, latitude, longitude.</param>
    /// <param name="openLandmarks">Opens tab-separated lines of name, latitude, longitude, reach in metres; none are known without it.</param>
    public GeoNamesPlaceResolver(Func<TextReader> openList, Func<TextReader>? openLandmarks = null)
    {
        _index = new Lazy<Index>(() => Index.Load(openList));
        _landmarks = new Lazy<LandmarkIndex>(() => LandmarkIndex.Load(openLandmarks));
    }

    /// <inheritdoc />
    public string? Describe(double latitude, double longitude)
    {
        if (NearestTown(latitude, longitude) is not { } town) return null;
        var name = _index.Value.NameOf(town.Index);
        return town.Km <= InsideKm ? name : $"Near {name}";
    }

    /// <inheritdoc />
    public PlaceMatch? Locate(double latitude, double longitude)
    {
        if (NearestTown(latitude, longitude) is not { } town) return null;
        var towns = _index.Value;
        var landmark = _landmarks.Value.At(latitude, longitude);
        return new PlaceMatch(towns.Town[town.Index], towns.Region[town.Index], towns.Country[town.Index],
            towns.Latitude[town.Index], towns.Longitude[town.Index], landmark?.Name, landmark?.Latitude, landmark?.Longitude);
    }

    private (int Index, double Km)? NearestTown(double latitude, double longitude)
    {
        if (double.IsNaN(latitude) || double.IsNaN(longitude) || Math.Abs(latitude) > 90 || Math.Abs(longitude) > 180) return null;
        var index = _index.Value;
        var eastWest = EastWest(latitude);

        // Places are filed by whole degree; look through every cell that can hold one within reach.
        int row = Index.Row(latitude), column = Index.Column(longitude);
        int rows = (int)Math.Ceiling(NearKm / KmPerDegree), columns = Math.Min(180, (int)Math.Ceiling(NearKm / (KmPerDegree * eastWest)));
        int best = -1;
        double bestKm = double.MaxValue;
        for (int r = Math.Max(0, row - rows); r <= Math.Min(179, row + rows); r++)
            for (int c = column - columns; c <= column + columns; c++)
                foreach (var i in index.Cell(r, ((c % 360) + 360) % 360))
                {
                    var km = Km(latitude, longitude, index.Latitude[i], index.Longitude[i], eastWest);
                    if (km < bestKm) (best, bestKm) = (i, km);
                }

        return best < 0 || bestKm > NearKm ? null : (best, bestKm);
    }

    // How much shorter a degree of longitude is than one of latitude, this far from the equator.
    private static double EastWest(double latitude) => Math.Max(0.01, Math.Cos(latitude * Math.PI / 180));

    private static double Km(double latitude, double longitude, double otherLatitude, double otherLongitude, double eastWest)
    {
        double north = (otherLatitude - latitude) * KmPerDegree;
        double east = Math.Abs(otherLongitude - longitude);
        east = Math.Min(east, 360 - east) * KmPerDegree * eastWest;
        return Math.Sqrt(north * north + east * east);
    }

    /// <summary>Opens a list built into <paramref name="assembly"/>, saying so plainly when a build left it out.</summary>
    public static TextReader OpenBundledList(Assembly assembly, string resource = Towns)
    {
        var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The place list ({resource}) is missing from the build.");
        return new StreamReader(new GZipStream(stream, CompressionMode.Decompress));
    }

    private static bool TryPosition(string latitude, string longitude, out float lat, out float lon)
    {
        lon = 0;
        return float.TryParse(latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out lat)
            && float.TryParse(longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out lon);
    }

    private sealed class Index
    {
        /// <summary>Each town's name, by its number.</summary>
        public readonly List<string> Town = [];
        /// <summary>Each town's state, province or county.</summary>
        public readonly List<string> Region = [];
        /// <summary>Each town's country code.</summary>
        public readonly List<string> Country = [];
        /// <summary>Where each town is: degrees north.</summary>
        public readonly List<float> Latitude = [];
        /// <summary>Where each town is: degrees east.</summary>
        public readonly List<float> Longitude = [];
        private readonly List<int>?[] _cells = new List<int>?[180 * 360];

        /// <summary>The band of one degree of latitude a position is in.</summary>
        public static int Row(double latitude) => Math.Clamp((int)Math.Floor(latitude) + 90, 0, 179);
        /// <summary>The band of one degree of longitude a position is in.</summary>
        public static int Column(double longitude) => Math.Clamp((int)Math.Floor(longitude) + 180, 0, 359);
        /// <summary>The towns in one square of a degree.</summary>
        public IReadOnlyList<int> Cell(int row, int column) => _cells[row * 360 + column] ?? (IReadOnlyList<int>)[];

        // "Buxton, North Carolina, US"; the region is left out where the list has none.
        /// <summary>A town in words, as the Clean up screens name it.</summary>
        public string NameOf(int i) => string.Join(", ", new[] { Town[i], Region[i], Country[i] }.Where(p => p.Length > 0));

        /// <summary>Reads the list of towns, each filed under the square of a degree it is in.</summary>
        public static Index Load(Func<TextReader> open)
        {
            var index = new Index();
            using var reader = open();
            while (reader.ReadLine() is { } line)
            {
                var parts = line.Split('\t');
                if (parts.Length < 5 || !TryPosition(parts[3], parts[4], out var latitude, out var longitude)) continue;
                index.Town.Add(parts[0]);
                index.Region.Add(parts[1]);
                index.Country.Add(parts[2]);
                index.Latitude.Add(latitude);
                index.Longitude.Add(longitude);
                (index._cells[Row(latitude) * 360 + Column(longitude)] ??= []).Add(index.Town.Count - 1);
            }
            return index;
        }
    }

    /// <summary>
    /// Sights, parks and districts, each a point with the distance its name reaches. The list gives a
    /// feature one point however large it is, so the reach is short: within it the picture was taken at
    /// the place rather than somewhere near it.
    /// </summary>
    private sealed class LandmarkIndex
    {
        // Cells a twentieth of a degree across: about 5.5 km north to south, several times the longest reach.
        private const int PerDegree = 20;
        private const int Columns = 360 * PerDegree;
        private readonly List<string> _name = [];
        private readonly List<float> _latitude = [];
        private readonly List<float> _longitude = [];
        private readonly List<float> _reachKm = [];
        private readonly Dictionary<long, List<int>> _cells = [];
        private double _longestReachKm;

        private static int Row(double latitude) => (int)Math.Floor((latitude + 90) * PerDegree);
        private static int Column(double longitude) => Math.Clamp((int)Math.Floor((longitude + 180) * PerDegree), 0, Columns - 1);
        private static long Cell(int row, int column) => (long)row * Columns + column;

        /// <summary>Reads the list of landmarks; none when there is no list.</summary>
        public static LandmarkIndex Load(Func<TextReader>? open)
        {
            var index = new LandmarkIndex();
            if (open is null) return index;
            using var reader = open();
            while (reader.ReadLine() is { } line)
            {
                var parts = line.Split('\t');
                if (parts.Length < 4 || !TryPosition(parts[1], parts[2], out var latitude, out var longitude)
                    || !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var reachMetres)) continue;
                index._name.Add(parts[0]);
                index._latitude.Add(latitude);
                index._longitude.Add(longitude);
                index._reachKm.Add(reachMetres / 1000);
                index._longestReachKm = Math.Max(index._longestReachKm, reachMetres / 1000);
                var cell = Cell(Row(latitude), Column(longitude));
                if (!index._cells.TryGetValue(cell, out var held)) index._cells[cell] = held = [];
                held.Add(index._name.Count - 1);
            }
            return index;
        }

        /// <summary>The nearest landmark whose reach takes in the position.</summary>
        public (string Name, double Latitude, double Longitude)? At(double latitude, double longitude)
        {
            var eastWest = EastWest(latitude);
            int row = Row(latitude), column = Column(longitude);
            var cellKm = KmPerDegree / PerDegree;
            int rows = (int)Math.Ceiling(_longestReachKm / cellKm), columns = Math.Min(Columns / 2, (int)Math.Ceiling(_longestReachKm / (cellKm * eastWest)));
            int best = -1;
            double bestKm = double.MaxValue;
            for (int r = row - rows; r <= row + rows; r++)
                for (int c = column - columns; c <= column + columns; c++)
                {
                    if (!_cells.TryGetValue(Cell(r, ((c % Columns) + Columns) % Columns), out var held)) continue;
                    foreach (var i in held)
                    {
                        var km = Km(latitude, longitude, _latitude[i], _longitude[i], eastWest);
                        if (km <= _reachKm[i] && km < bestKm) (best, bestKm) = (i, km);
                    }
                }
            return best < 0 ? null : (_name[best], _latitude[best], _longitude[best]);
        }
    }
}
