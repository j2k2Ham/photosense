using System.Globalization;

namespace PhotoSense.Domain.ValueObjects;

/// <summary>
/// Fix dates and places, as Jamie set it (2026-11-18): which dates a camera got wrong, which place a photo with none
/// can be given, and how a copy on the computer is named by its date.
/// </summary>
public static class DetailRules
{
    /// <summary>The years a camera whose clock was never set dates its photos in, on January 1.</summary>
    public static readonly IReadOnlySet<int> DefaultYears = new HashSet<int> { 1970, 1980, 2000 };

    /// <summary>The longest time between a photo with no place and the one whose place it is given.</summary>
    public static readonly TimeSpan Nearby = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Whether a photo's date is one a camera's clock gave it: January 1 of a year a clock starts at, or later than
    /// a day from <paramref name="now"/>.
    /// </summary>
    public static bool IsWrongDate(DateTime taken, DateTime now)
        => (taken is { Month: 1, Day: 1 } && DefaultYears.Contains(taken.Year)) || taken > now.AddDays(1);

    /// <summary>
    /// For each photo in <paramref name="placeless"/>, the photo in <paramref name="placed"/> taken nearest in time to
    /// it and no more than <see cref="Nearby"/> away; those with none are left out.
    /// </summary>
    /// <typeparam name="T">A photo.</typeparam>
    /// <param name="placeless">Photos with a time and no place.</param>
    /// <param name="placed">Photos with a time and a place.</param>
    /// <param name="when">When a photo was taken.</param>
    public static IReadOnlyList<(T Photo, T Nearest)> NearestPlaced<T>(IEnumerable<T> placeless, IReadOnlyList<T> placed, Func<T, DateTime> when)
    {
        var ordered = placed.OrderBy(when).ToList();
        var times = ordered.Select(when).ToArray();
        var found = new List<(T, T)>();
        foreach (var photo in placeless)
        {
            var at = when(photo);
            var index = Array.BinarySearch(times, at);
            if (index < 0) index = ~index;
            // The nearest is just before or just after where it would go.
            var nearest = new[] { index - 1, index }.Where(i => i >= 0 && i < times.Length).OrderBy(i => (times[i] - at).Duration()).Cast<int?>().FirstOrDefault();
            if (nearest is { } i && (times[i] - at).Duration() <= Nearby) found.Add((photo, ordered[i]));
        }
        return found;
    }

    /// <summary>A date as EXIF writes it: "2026:03:02 14:31:07".</summary>
    public static string ExifDate(DateTime taken) => taken.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// A latitude or longitude as EXIF writes it, as its own degrees, minutes and seconds with its reference:
    /// ("45/1,40/1,4692/100", "N"). <paramref name="positive"/> and <paramref name="negative"/> are the references
    /// north and south, or east and west.
    /// </summary>
    public static (string Value, string Reference) ExifDegrees(double degrees, string positive, string negative)
    {
        var hundredths = (long)Math.Round(Math.Abs(degrees) * 360_000);
        return ($"{hundredths / 360_000}/1,{hundredths / 6000 % 60}/1,{hundredths % 6000}/100", degrees < 0 ? negative : positive);
    }

    /// <summary>A file's name by when it was taken, and where when asked: "2026-03-02 14.31.07.HEIC", "2026-03-02 14.31.07 Big Sky.HEIC".</summary>
    public static string NameByDate(DateTime taken, string? place, string extension)
        => $"{taken.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture)}{(string.IsNullOrWhiteSpace(place) ? "" : " " + place.Trim())}{extension}";

    /// <summary>The name, or, when it is taken, the same with -2, -3 and so on at the end of its stem: "2026-03-02 14.31.07-2.HEIC".</summary>
    public static string Numbered(string name, Func<string, bool> taken)
    {
        if (!taken(name)) return name;
        string stem = Path.GetFileNameWithoutExtension(name), extension = Path.GetExtension(name);
        for (var n = 2; ; n++)
        {
            var numbered = $"{stem}-{n}{extension}";
            if (!taken(numbered)) return numbered;
        }
    }
}
