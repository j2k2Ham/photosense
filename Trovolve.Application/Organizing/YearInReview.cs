using Trovolve.Domain.Entities;
using Trovolve.Domain.Repositories;
using Trovolve.Domain.Services;

namespace Trovolve.Application.Organizing;

/// <summary>A trip of the year, as Year in review lists it.</summary>
/// <param name="Name">What the trip is called.</param>
/// <param name="Month">The month it started, 1 to 12.</param>
/// <param name="Photos">Its pictures and videos.</param>
public sealed record YearTrip(string Name, int Month, int Photos);

/// <summary>A town photos were taken in during the year, with how many.</summary>
/// <param name="Town">The town, as the place names have it.</param>
/// <param name="Photos">How many pictures and videos were taken there.</param>
public sealed record YearPlace(string Town, int Photos);

/// <summary>
/// A year in photos, as Year in review shows it: what was taken, where, when most, and what was cleaned up. Nothing in it
/// is a photo.
/// </summary>
/// <param name="Year">The year.</param>
/// <param name="Photos">Pictures taken that year.</param>
/// <param name="Videos">Videos taken that year; a Live Photo's video goes with its picture.</param>
/// <param name="Trips">How many trips started that year.</param>
/// <param name="TopTrips">The three trips with the most photos, in the order they were taken.</param>
/// <param name="Months">How many were taken in each month, January first.</param>
/// <param name="BusiestMonth">The month with the most, 1 to 12; 0 when none was taken.</param>
/// <param name="BusiestPlace">Where most of the busiest month's were taken, and how many; null when none had a place.</param>
/// <param name="Freed">Bytes freed from the device that year, as noted since growth step 7.</param>
/// <param name="Removed">Photos and videos removed that year.</param>
/// <param name="LongestStreak">The most days in a row something was swiped.</param>
/// <param name="Sessions">The days something was swiped.</param>
public sealed record YearReview(int Year, int Photos, int Videos, int Trips, IReadOnlyList<YearTrip> TopTrips, IReadOnlyList<int> Months, int BusiestMonth,
    YearPlace? BusiestPlace, long Freed, int Removed, int LongestStreak, int Sessions);

/// <summary>
/// Year in review, built on the device from what the scans recorded and what was cleaned up: offered from December 1
/// for the year ending, and through January for the year just ended (Jamie, 2026-11-17). Its places are the year's
/// trips (Jamie, 2026-11-17: towns until Trips came).
/// </summary>
public sealed class YearInReview
{
    private readonly IPhotoRepository _repo;
    private readonly IPlaceNameResolver _places;
    private readonly IPlanStore _plans;
    private readonly ISwipeStore _swipes;
    private readonly Trips _trips;
    private readonly TimeProvider _time;

    /// <summary>Year in review of what <paramref name="repo"/> recorded.</summary>
    /// <param name="repo">What the scans recorded.</param>
    /// <param name="places">The towns positions are near.</param>
    /// <param name="plans">Each day's removals, and since growth step 7 what each day cleaned up.</param>
    /// <param name="swipes">The days something was swiped.</param>
    /// <param name="trips">The trips, those that started in the year among them.</param>
    /// <param name="time">The clock, whose local date says which year is due.</param>
    public YearInReview(IPhotoRepository repo, IPlaceNameResolver places, IPlanStore plans, ISwipeStore swipes, Trips trips, TimeProvider time)
    {
        _trips = trips;
        _repo = repo;
        _places = places;
        _plans = plans;
        _swipes = swipes;
        _time = time;
    }

    /// <summary>The year Year in review is offered for today: this year in December, last year in January; otherwise none.</summary>
    public int? Due()
    {
        var today = _time.GetLocalNow();
        return today.Month switch { 12 => today.Year, 1 => today.Year - 1, _ => null };
    }

    /// <summary>The year in photos.</summary>
    public async Task<YearReview> ReviewAsync(int year, CancellationToken ct)
    {
        var taken = (await _repo.GetAllAsync(ct))
            // A Live Photo's video is the picture's, counted with it.
            .Where(p => !(p.IsVideo && p.LivePhotoId is not null) && WhenTaken(p).Year == year)
            .ToList();
        var months = new int[12];
        foreach (var photo in taken) months[WhenTaken(photo).Month - 1]++;
        var busiest = taken.Count == 0 ? 0 : Array.IndexOf(months, months.Max()) + 1;
        var towns = taken.Select(p => (Photo: p, Town: TownOf(p))).Where(t => t.Town is not null).ToList();
        var inBusiest = Ranked(towns.Where(t => WhenTaken(t.Photo).Month == busiest).Select(t => t.Town!));

        var cleaned = (await _plans.CleanedInAsync(year, ct)).ToDictionary(d => d.Day);
        var counted = await _plans.RemovedInAsync(year, ct);
        // Each day as fully as it is known: the record kept since growth step 7, or the free plan's count before it.
        var removed = cleaned.Keys.Union(counted.Keys).Sum(day => Math.Max(cleaned.GetValueOrDefault(day)?.Removed ?? 0, counted.GetValueOrDefault(day)));
        var swiped = (await _swipes.DaysAsync(ct)).Where(d => d.Year == year).Order().ToList();
        var trips = (await _trips.FindAsync(ct)).Found.Where(t => t.Start.Year == year).ToList();
        var top = trips.OrderByDescending(t => t.Photos.Count).Take(3).OrderBy(t => t.Start).Select(t => new YearTrip(t.Name, t.Start.Month, t.Photos.Count));
        return new YearReview(year, taken.Count(p => !p.IsVideo), taken.Count(p => p.IsVideo), trips.Count, [.. top], months, busiest,
            inBusiest.FirstOrDefault(), cleaned.Values.Sum(d => d.Freed), removed, LongestRun(swiped), swiped.Count);
    }

    // When it was taken, or else last changed; a photo with neither is in no year.
    private static DateTime WhenTaken(Photo photo) => photo.TakenOn ?? photo.FileModifiedUtc ?? DateTime.MinValue;

    private string? TownOf(Photo photo) => photo.Latitude is { } latitude && photo.Longitude is { } longitude ? _places.Locate(latitude, longitude)?.Town : null;

    // Most first; a tie goes by name.
    private static List<YearPlace> Ranked(IEnumerable<string> towns)
        => [.. towns.GroupBy(t => t).Select(g => new YearPlace(g.Key, g.Count())).OrderByDescending(p => p.Photos).ThenBy(p => p.Town, StringComparer.Ordinal)];

    // The most days in a row among those given, in order.
    private static int LongestRun(List<DateOnly> days)
    {
        int longest = 0, run = 0;
        for (var i = 0; i < days.Count; i++)
        {
            run = i > 0 && days[i].DayNumber == days[i - 1].DayNumber + 1 ? run + 1 : 1;
            longest = Math.Max(longest, run);
        }
        return longest;
    }
}
