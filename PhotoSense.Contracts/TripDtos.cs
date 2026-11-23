using System.Text.Json.Serialization;
using PhotoSense.Contracts.Duplicates;

namespace PhotoSense.Contracts.Organize;

/// <summary>A town, home or one that could be.</summary>
public sealed class TownDto
{
    /// <summary>The town.</summary>
    [JsonPropertyName("town")] public string Town { get; init; } = string.Empty;
    /// <summary>The state, province or county it is in.</summary>
    [JsonPropertyName("region")] public string Region { get; init; } = string.Empty;
    /// <summary>The country it is in.</summary>
    [JsonPropertyName("country")] public string Country { get; init; } = string.Empty;
    /// <summary>Where it is: degrees north.</summary>
    [JsonPropertyName("latitude")] public double Latitude { get; init; }
    /// <summary>Where it is: degrees east.</summary>
    [JsonPropertyName("longitude")] public double Longitude { get; init; }
    /// <summary>The days photos were taken there; 0 where it does not matter.</summary>
    [JsonPropertyName("days")] public int Days { get; init; }
}

/// <summary>A trip, as the list and the map show it.</summary>
public sealed class TripDto
{
    /// <summary>Tells it from the others.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    /// <summary>What it is called: "Glacier National Park", "Big Sky, Montana", "Anaconda and Butte".</summary>
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    /// <summary>The folder it becomes on the computer: "2026-07 Glacier National Park".</summary>
    [JsonPropertyName("folder")] public string Folder { get; init; } = string.Empty;
    /// <summary>The day it started, "2026-07-18", by the photos' own clocks.</summary>
    [JsonPropertyName("start")] public string Start { get; init; } = string.Empty;
    /// <summary>The day it ended.</summary>
    [JsonPropertyName("end")] public string End { get; init; } = string.Empty;
    /// <summary>Its pictures and videos.</summary>
    [JsonPropertyName("photos")] public int Photos { get; init; }
    /// <summary>How many of them are videos.</summary>
    [JsonPropertyName("videos")] public int Videos { get; init; }
    /// <summary>The middle of where they were taken: degrees north.</summary>
    [JsonPropertyName("latitude")] public double Latitude { get; init; }
    /// <summary>The middle of where they were taken: degrees east.</summary>
    [JsonPropertyName("longitude")] public double Longitude { get; init; }
    /// <summary>The picture shown for it.</summary>
    [JsonPropertyName("cover")] public PhotoItemDto Cover { get; init; } = new();
    /// <summary>The computer has made its folder.</summary>
    [JsonPropertyName("folderMade")] public bool FolderMade { get; init; }
    /// <summary>PhotoSense's own name for it, which <see cref="Name"/> is unless the person named it.</summary>
    [JsonPropertyName("suggested")] public string Suggested { get; init; } = string.Empty;
}

/// <summary>The trip folder being made on the computer, or the last one asked for.</summary>
public sealed class TripFolderDto
{
    /// <summary>The trip.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    /// <summary>"running", "ready" once made, or "failed".</summary>
    [JsonPropertyName("stage")] public string Stage { get; init; } = string.Empty;
    /// <summary>Why it was not made.</summary>
    [JsonPropertyName("problem")] public string? Problem { get; init; }
    /// <summary>Where the computer made it: "Pictures/PhotoSense/2026-07 Glacier National Park".</summary>
    [JsonPropertyName("made")] public string? Made { get; init; }
}

/// <summary>Trips: home, the trips, the latest first, and the computer their folders are made on.</summary>
public sealed class TripsDto
{
    /// <summary>Home; null when no photo says where it was taken.</summary>
    [JsonPropertyName("home")] public TownDto? Home { get; init; }
    /// <summary>The person chose home; otherwise PhotoSense worked it out.</summary>
    [JsonPropertyName("homeChosen")] public bool HomeChosen { get; init; }
    /// <summary>The trips, the latest first.</summary>
    [JsonPropertyName("trips")] public IReadOnlyList<TripDto> Trips { get; init; } = [];
    /// <summary>The computer the phone backs up to, where trip folders are made; null when it has been paired with none.</summary>
    [JsonPropertyName("computer")] public string? Computer { get; init; }
    /// <summary>Where the computer makes trip folders, as it last said ("Pictures/PhotoSense"); null until it has said.</summary>
    [JsonPropertyName("tripsFolder")] public string? TripsFolder { get; init; }
    /// <summary>The trip folder being made, or the last one asked for.</summary>
    [JsonPropertyName("folder")] public TripFolderDto? Folder { get; init; }
}

/// <summary>A day of a trip, with what was taken on it.</summary>
public sealed class TripDayDto
{
    /// <summary>The day, "2026-07-18".</summary>
    [JsonPropertyName("date")] public string Date { get; init; } = string.Empty;
    /// <summary>What was taken that day, in order.</summary>
    [JsonPropertyName("photos")] public IReadOnlyList<PhotoItemDto> Photos { get; init; } = [];
}

/// <summary>A trip with its days.</summary>
public sealed class TripDetailDto
{
    /// <summary>The trip.</summary>
    [JsonPropertyName("trip")] public TripDto Trip { get; init; } = new();
    /// <summary>Its days, the first first.</summary>
    [JsonPropertyName("days")] public IReadOnlyList<TripDayDto> Days { get; init; } = [];
}

/// <summary>A trip's name as the person wants it; with none, PhotoSense's own again.</summary>
public sealed class TripNameRequest
{
    /// <summary>The name; null or empty for PhotoSense's own.</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }
}

/// <summary>The home chosen; with no town, PhotoSense works it out again.</summary>
public sealed class HomeRequest
{
    /// <summary>The town chosen; null to let PhotoSense work it out.</summary>
    [JsonPropertyName("town")] public TownDto? Town { get; init; }
}
