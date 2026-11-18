using System.Globalization;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Application.Organizing;

/// <summary>Home, for Trips: photos taken far from it are away.</summary>
/// <param name="Town">The town.</param>
/// <param name="Chosen">The person chose it; otherwise it is the town photographed on the most different days.</param>
public sealed record TripHome(HomeTown Town, bool Chosen);

/// <summary>A town the person could choose as home, and on how many different days photos were taken there.</summary>
/// <param name="Town">The town.</param>
/// <param name="Days">The days photos were taken there.</param>
public sealed record TownChoice(HomeTown Town, int Days);

/// <summary>A day of a trip, with what was taken on it.</summary>
/// <param name="Day">The day, as the photos' own clocks had it.</param>
/// <param name="Photos">What was taken that day, in order.</param>
public sealed record TripDay(DateOnly Day, IReadOnlyList<Photo> Photos);

/// <summary>A trip: photos and videos taken away from home, with no gap of more than two days between them.</summary>
/// <param name="Id">Tells it from the others: when it started, "20260718093112".</param>
/// <param name="Name">What it is called: a landmark ("Glacier National Park"), a town and its region ("Big Sky, Montana"), or two towns ("Anaconda and Butte").</param>
/// <param name="Folder">The folder it becomes on the computer: "2026-07 Glacier National Park".</param>
/// <param name="Start">When its first photo was taken, by the photo's own clock.</param>
/// <param name="End">When its last was taken.</param>
/// <param name="Photos">Its pictures and videos, in the order they were taken; a Live Photo's video goes with its picture.</param>
/// <param name="Latitude">The middle of where they were taken: degrees north.</param>
/// <param name="Longitude">The middle of where they were taken: degrees east.</param>
/// <param name="Cover">The picture shown for it: its sharpest, or its first when none was measured.</param>
public sealed record Trip(string Id, string Name, string Folder, DateTime Start, DateTime End, IReadOnlyList<Photo> Photos, double Latitude, double Longitude, Photo Cover)
{
    /// <summary>How many of its photos are videos.</summary>
    public int Videos => Photos.Count(p => p.IsVideo);

    /// <summary>Its days, each with what was taken on it.</summary>
    public IReadOnlyList<TripDay> Days => [.. Photos.GroupBy(p => DateOnly.FromDateTime(Trips.WhenTaken(p)!.Value)).Select(g => new TripDay(g.Key, [.. g]))];
}

/// <summary>The trips found, and the home they are away from.</summary>
/// <param name="Home">Home; null when no photo says where it was taken.</param>
/// <param name="Found">The trips, the latest first.</param>
public sealed record TripsFound(TripHome? Home, IReadOnlyList<Trip> Found);

/// <summary>
/// Trips, built on the phone from where and when its photos were taken (<see cref="TripRules"/>): away from home, which
/// is the town photographed on the most different days unless the person chose another.
/// </summary>
public sealed class Trips
{
    private readonly IPhotoRepository _repo;
    private readonly IPlaceNameResolver _places;
    private readonly ITripStore _store;

    // A photo with where and when it was taken.
    private sealed record Placed(Photo Photo, PlaceMatch At, DateTime When);

    /// <summary>Trips among what <paramref name="repo"/> recorded.</summary>
    /// <param name="repo">What the scans recorded.</param>
    /// <param name="places">The towns and landmarks positions are at.</param>
    /// <param name="store">The home the person chose.</param>
    public Trips(IPhotoRepository repo, IPlaceNameResolver places, ITripStore store)
    {
        _repo = repo;
        _places = places;
        _store = store;
    }

    /// <summary>When a photo was taken, or else last changed; null when it says neither.</summary>
    public static DateTime? WhenTaken(Photo photo) => photo.TakenOn ?? photo.FileModifiedUtc;

    /// <summary>The trips, the latest first, and home.</summary>
    public async Task<TripsFound> FindAsync(CancellationToken ct)
    {
        var placed = await PlacedAsync(ct);
        var home = await HomeAsync(placed, ct);
        if (home is null) return new TripsFound(null, []);
        var trips = TripRules.Find(placed, p => (p.Photo.Latitude!.Value, p.Photo.Longitude!.Value), p => p.When, home.Town.Latitude, home.Town.Longitude)
            .Select(Made).Reverse().ToList();
        // Two trips of one month to one place take their day too, so that each has a folder of its own.
        var clashing = trips.GroupBy(t => t.Folder, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).SelectMany(g => g).ToHashSet();
        return new TripsFound(home, [.. trips.Select(t => clashing.Contains(t) ? t with { Folder = $"{t.Start:yyyy-MM-dd}{t.Folder[7..]}" } : t)]);
    }

    /// <summary>The towns photos were taken in, those on the most days first: what the person can choose as home.</summary>
    public async Task<IReadOnlyList<TownChoice>> TownsAsync(CancellationToken ct) => Ranked(await PlacedAsync(ct));

    /// <summary>Makes <paramref name="town"/> home, or with null lets PhotoSense work it out again.</summary>
    public Task ChooseHomeAsync(HomeTown? town, CancellationToken ct) => _store.SaveHomeAsync(town, ct);

    private async Task<TripHome?> HomeAsync(List<Placed> placed, CancellationToken ct)
    {
        if (await _store.HomeAsync(ct) is { } chosen) return new TripHome(chosen, true);
        return Ranked(placed).FirstOrDefault() is { } top ? new TripHome(top.Town, false) : null;
    }

    // Every picture and video with a place and a time; a Live Photo's video goes with its picture.
    private async Task<List<Placed>> PlacedAsync(CancellationToken ct)
        => [.. (await _repo.GetAllAsync(ct))
            .Where(p => !(p.IsVideo && p.LivePhotoId is not null) && p.Latitude is not null && p.Longitude is not null && WhenTaken(p) is not null)
            .Select(p => (Photo: p, At: _places.Locate(p.Latitude!.Value, p.Longitude!.Value)))
            .Where(p => p.At is not null)
            .Select(p => new Placed(p.Photo, p.At!, WhenTaken(p.Photo)!.Value))];

    // The towns, those photographed on the most different days first; a tie goes by name.
    private static List<TownChoice> Ranked(List<Placed> placed)
        => [.. placed.GroupBy(p => (p.At.Town, p.At.Region, p.At.Country))
            .Select(g => new TownChoice(new HomeTown(g.Key.Town, g.Key.Region, g.Key.Country, g.First().At.Latitude, g.First().At.Longitude), g.Select(p => p.When.Date).Distinct().Count()))
            .OrderByDescending(t => t.Days).ThenBy(t => t.Town.Town, StringComparer.Ordinal).ThenBy(t => t.Town.Region, StringComparer.Ordinal)];

    private static Trip Made(IReadOnlyList<Placed> trip)
    {
        var (name, shortName) = NameOf(trip);
        var start = trip[0].When;
        var photos = trip.Select(p => p.Photo).ToList();
        var cover = photos.Where(p => !p.IsVideo).MaxBy(p => p.Sharpness ?? -1) ?? photos[0];
        return new Trip(start.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), name, $"{start:yyyy-MM} {shortName}", start, trip[^1].When, photos,
            photos.Average(p => p.Latitude!.Value), photos.Average(p => p.Longitude!.Value), cover);
    }

    // The landmark or town most were taken at; two towns when the second has at least a quarter of them too.
    private static (string Name, string Short) NameOf(IReadOnlyList<Placed> trip)
    {
        var names = trip.GroupBy(p => p.At.Landmark ?? p.At.Town)
            .Select(g => (Name: g.Key, Count: g.Count(), Landmark: g.First().At.Landmark is not null, g.First().At.Region))
            .OrderByDescending(n => n.Count).ThenBy(n => n.Name, StringComparer.Ordinal).ToList();
        var top = names[0];
        if (top.Landmark) return (top.Name, top.Name);
        if (names.Count > 1 && !names[1].Landmark && names[1].Count * 4 >= trip.Count)
        {
            var both = $"{top.Name} and {names[1].Name}";
            return (both, both);
        }
        return (top.Region.Length > 0 ? $"{top.Name}, {top.Region}" : top.Name, top.Name);
    }
}
