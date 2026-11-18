using System.Text.Json.Serialization;

namespace PhotoSense.Contracts.Cleaning;

/// <summary>A town photos were taken in during the year, with how many.</summary>
public sealed class YearPlaceDto
{
    /// <summary>The town.</summary>
    [JsonPropertyName("town")] public string Town { get; init; } = string.Empty;
    /// <summary>How many pictures and videos were taken there.</summary>
    [JsonPropertyName("photos")] public int Photos { get; init; }
}

/// <summary>Year in review: the year due now, or null when none is, and that year in photos.</summary>
public sealed class YearReviewDto
{
    /// <summary>The year offered now (December for the year ending, January for the year just ended); null the rest of the year.</summary>
    [JsonPropertyName("year")] public int? Year { get; init; }
    /// <summary>Pictures taken that year.</summary>
    [JsonPropertyName("photos")] public int Photos { get; init; }
    /// <summary>Videos taken that year.</summary>
    [JsonPropertyName("videos")] public int Videos { get; init; }
    /// <summary>How many towns they were taken in.</summary>
    [JsonPropertyName("places")] public int Places { get; init; }
    /// <summary>The three towns with the most, most first.</summary>
    [JsonPropertyName("topPlaces")] public IReadOnlyList<YearPlaceDto> TopPlaces { get; init; } = [];
    /// <summary>How many were taken in each month, January first.</summary>
    [JsonPropertyName("months")] public IReadOnlyList<int> Months { get; init; } = [];
    /// <summary>The month with the most, 1 to 12; 0 when none was taken.</summary>
    [JsonPropertyName("busiestMonth")] public int BusiestMonth { get; init; }
    /// <summary>Where most of the busiest month's were taken; null when none had a place.</summary>
    [JsonPropertyName("busiestPlace")] public YearPlaceDto? BusiestPlace { get; init; }
    /// <summary>Bytes freed from the phone that year.</summary>
    [JsonPropertyName("freed")] public long Freed { get; init; }
    /// <summary>Photos and videos removed that year.</summary>
    [JsonPropertyName("removed")] public int Removed { get; init; }
    /// <summary>The most days in a row something was swiped.</summary>
    [JsonPropertyName("longestStreak")] public int LongestStreak { get; init; }
    /// <summary>The days something was swiped.</summary>
    [JsonPropertyName("sessions")] public int Sessions { get; init; }
}

/// <summary>A picture to hand to the phone's share sheet.</summary>
public sealed class ShareRequest
{
    /// <summary>The picture, a PNG, in base 64.</summary>
    [JsonPropertyName("image")] public string Image { get; init; } = string.Empty;
    /// <summary>What it is called, as the app it goes to sees it: "PhotoSense.png".</summary>
    [JsonPropertyName("name")] public string Name { get; init; } = "PhotoSense.png";
}
