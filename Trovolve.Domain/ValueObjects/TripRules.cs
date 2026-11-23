namespace Trovolve.Domain.ValueObjects;

/// <summary>
/// What makes a trip, as Jamie set it (2026-11-18): photos taken more than 30 miles from home are away; those taken away
/// are one trip until a photo at home, or more than two days without one away; and a trip has at least ten photos and
/// videos, so a day trip with that many counts.
/// </summary>
public static class TripRules
{
    /// <summary>How far from home a photo has to be taken to be away, in miles.</summary>
    public const double AwayMiles = 30;

    /// <summary>The same, in kilometres, as distances are worked out.</summary>
    public const double AwayKm = AwayMiles * 1.609344;

    /// <summary>The fewest photos and videos a trip has.</summary>
    public const int Fewest = 10;

    /// <summary>The longest time between two photos taken away that keeps them in one trip.</summary>
    public static readonly TimeSpan LongestGap = TimeSpan.FromDays(2);

    private const double EarthKm = 6371.0088;

    /// <summary>How far apart two positions are along the earth, in kilometres.</summary>
    public static double Km(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        static double Radians(double degrees) => degrees * Math.PI / 180;
        var dLat = Radians(latitude2 - latitude1);
        var dLon = Radians(longitude2 - longitude1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Radians(latitude1)) * Math.Cos(Radians(latitude2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>
    /// The trips among <paramref name="photos"/>, earliest first, each its photos in the order they were taken. Only
    /// photos that say where and when they were taken are given.
    /// </summary>
    /// <typeparam name="T">A photo.</typeparam>
    /// <param name="photos">Photos with a place and a time.</param>
    /// <param name="at">Where a photo was taken.</param>
    /// <param name="when">When it was taken.</param>
    /// <param name="homeLatitude">Where home is: degrees north.</param>
    /// <param name="homeLongitude">Where home is: degrees east.</param>
    public static IReadOnlyList<IReadOnlyList<T>> Find<T>(IEnumerable<T> photos, Func<T, (double Latitude, double Longitude)> at, Func<T, DateTime> when,
        double homeLatitude, double homeLongitude)
    {
        var trips = new List<IReadOnlyList<T>>();
        List<T>? trip = null;
        var last = DateTime.MinValue;
        foreach (var photo in photos.OrderBy(when))
        {
            var (latitude, longitude) = at(photo);
            if (Km(homeLatitude, homeLongitude, latitude, longitude) <= AwayKm)
            {
                // Back home: the trip is over.
                trip = null;
                continue;
            }
            if (trip is null || when(photo) - last > LongestGap)
            {
                trip = [];
                trips.Add(trip);
            }
            trip.Add(photo);
            last = when(photo);
        }
        return [.. trips.Where(t => t.Count >= Fewest)];
    }
}
