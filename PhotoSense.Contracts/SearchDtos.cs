using System.Text.Json.Serialization;
using PhotoSense.Contracts.Duplicates;

namespace PhotoSense.Contracts.Cleaning;

/// <summary>A part of a search: something in the photo, a place, a month or a year.</summary>
public sealed class SearchPartDto
{
    /// <summary>"inPhoto", "place", "month" or "year".</summary>
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    /// <summary>The words, as written.</summary>
    [JsonPropertyName("text")] public string Text { get; init; } = string.Empty;
}

/// <summary>What a search found.</summary>
public sealed class SearchResultDto
{
    /// <summary>The search, in its parts.</summary>
    [JsonPropertyName("parts")] public IReadOnlyList<SearchPartDto> Parts { get; init; } = [];
    /// <summary>What matches, latest first, up to 500.</summary>
    [JsonPropertyName("photos")] public IReadOnlyList<PhotoItemDto> Photos { get; init; } = [];
    /// <summary>How many match in all.</summary>
    [JsonPropertyName("total")] public int Total { get; init; }
    /// <summary>How many of those that match are videos; the rest are pictures.</summary>
    [JsonPropertyName("videos")] public int Videos { get; init; }
    /// <summary>It named things in photos, which only Pro searches: the free plan matched its date and place alone.</summary>
    [JsonPropertyName("proNeeded")] public bool ProNeeded { get; init; }
}

/// <summary>What the viewer shows beside a picture or video: its camera, and whether it is a favorite.</summary>
public sealed class PhotoDetailsDto
{
    /// <summary>The camera it was taken with, as it says; null when not known.</summary>
    [JsonPropertyName("camera")] public string? Camera { get; init; }
    /// <summary>It is a favorite in the phone's own photos app.</summary>
    [JsonPropertyName("favorite")] public bool Favorite { get; init; }
}

/// <summary>Marks a picture or video a favorite, or takes the mark away.</summary>
public sealed class FavoriteRequest
{
    /// <summary>The picture or video, as Library shows its path.</summary>
    [JsonPropertyName("path")] public string Path { get; init; } = string.Empty;
    /// <summary>Whether it is to be a favorite.</summary>
    [JsonPropertyName("favorite")] public bool Favorite { get; init; }
}

/// <summary>A picture or video to share, and what to leave out of the copy.</summary>
public sealed class SharePhotoRequest
{
    /// <summary>The picture or video, as Library shows its path.</summary>
    [JsonPropertyName("path")] public string Path { get; init; } = string.Empty;
    /// <summary>Leave out where it was taken.</summary>
    [JsonPropertyName("removeLocation")] public bool RemoveLocation { get; init; } = true;
    /// <summary>Leave out the camera's make, model, lens and exposure.</summary>
    [JsonPropertyName("removeCamera")] public bool RemoveCamera { get; init; }
}
