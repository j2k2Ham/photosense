using System.Globalization;

namespace Trovolve.Domain.Matching;

/// <summary>What a part of a search is: something in the photo, a place, a month or a year.</summary>
public enum SearchPartKind
{
    /// <summary>Something in the photo, matched against the phone's labels: part of Pro.</summary>
    InPhoto,
    /// <summary>A town, region or landmark a photo was taken at.</summary>
    Place,
    /// <summary>A month of any year.</summary>
    Month,
    /// <summary>A year.</summary>
    Year,
}

/// <summary>A part of a search as the person wrote it, and what it is.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Text">The words, as written.</param>
public sealed record SearchPart(SearchPartKind Kind, string Text);

/// <summary>
/// A search split into its parts: places first, the longest name known first, then months and years, and what is left,
/// less the little words, as things in the photo. "dog beach Glacier National Park July 2023" is a dog and a beach, at
/// Glacier National Park, in July, of 2023.
/// </summary>
/// <param name="Parts">The parts, in the order written.</param>
public sealed record SearchQuery(IReadOnlyList<SearchPart> Parts)
{
    private static readonly string[] Months = CultureInfo.InvariantCulture.DateTimeFormat.MonthNames[..12];
    private static readonly HashSet<string> Little = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "at", "by", "during", "for", "from", "in", "my", "near", "of", "on", "over", "photo", "photos", "picture", "pictures", "taken", "the", "to", "under", "video", "videos", "with",
    };

    /// <summary>The words of things in the photo, in lower case.</summary>
    public IReadOnlyList<string> Words => [.. Parts.Where(p => p.Kind == SearchPartKind.InPhoto).Select(p => p.Text.ToLowerInvariant())];

    /// <summary>The places, in lower case.</summary>
    public IReadOnlyList<string> Places => [.. Parts.Where(p => p.Kind == SearchPartKind.Place).Select(p => p.Text.ToLowerInvariant())];

    /// <summary>The month, 1 to 12, or null.</summary>
    public int? Month => Parts.FirstOrDefault(p => p.Kind == SearchPartKind.Month) is { } month ? MonthOf(month.Text) : null;

    /// <summary>The year, or null.</summary>
    public int? Year => Parts.FirstOrDefault(p => p.Kind == SearchPartKind.Year) is { } year ? int.Parse(year.Text, CultureInfo.InvariantCulture) : null;

    /// <summary>Splits <paramref name="text"/> into its parts, the places named as <paramref name="places"/> name them.</summary>
    /// <param name="text">What the person wrote.</param>
    /// <param name="places">The names of the places the photos were taken at, any case.</param>
    public static SearchQuery Parse(string text, IReadOnlyCollection<string> places)
    {
        var known = new HashSet<string>(places.Select(p => p.ToLowerInvariant()), StringComparer.Ordinal);
        var longest = known.Count == 0 ? 0 : known.Max(p => p.Split(' ').Length);
        var words = text.Split((char[])[' ', ',', '.', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var parts = new List<SearchPart>();
        for (var at = 0; at < words.Length;)
        {
            // The longest place name starting here.
            var place = Enumerable.Range(1, Math.Min(longest, words.Length - at)).Reverse()
                .Select(n => string.Join(' ', words, at, n)).FirstOrDefault(name => known.Contains(name.ToLowerInvariant()));
            if (place is not null)
            {
                parts.Add(new SearchPart(SearchPartKind.Place, place));
                at += place.Split(' ').Length;
                continue;
            }
            var word = words[at++];
            if (MonthOf(word) is not null) parts.Add(new SearchPart(SearchPartKind.Month, word));
            else if (word.Length == 4 && int.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out var year) && year is >= 1900 and <= 2100) parts.Add(new SearchPart(SearchPartKind.Year, word));
            else if (!Little.Contains(word)) parts.Add(new SearchPart(SearchPartKind.InPhoto, word));
        }
        return new SearchQuery(parts);
    }

    // "July", "jul": the month's number.
    private static int? MonthOf(string word)
    {
        var at = Array.FindIndex(Months, m => m.Equals(word, StringComparison.OrdinalIgnoreCase) || (word.Length == 3 && m.StartsWith(word, StringComparison.OrdinalIgnoreCase)));
        return at < 0 ? null : at + 1;
    }

    /// <summary>Whether <paramref name="labels"/> name <paramref name="word"/>: the same word, one or many ("dog", "dogs"), or a word of a longer label ("retriever").</summary>
    public static bool Names(IEnumerable<string> labels, string word)
        => labels.Any(label => label.Split(' ').Any(part => part == word || part + "s" == word || part == word + "s"));
}
