using System.Text.Json.Serialization;
using Trovolve.Contracts.Duplicates;

namespace Trovolve.Contracts.Organize;

/// <summary>A kind of fix, as Fix dates and places shows it: how many, a few of them, and what they would get.</summary>
public sealed class FixGroupDto
{
    /// <summary>How many photos and videos, a Live Photo once.</summary>
    [JsonPropertyName("count")] public int Count { get; init; }
    /// <summary>The first few, to show.</summary>
    [JsonPropertyName("photos")] public IReadOnlyList<PhotoItemDto> Photos { get; init; } = [];
    /// <summary>For wrong dates: the date most of them have, "2000-01-01"; null otherwise.</summary>
    [JsonPropertyName("date")] public string? Date { get; init; }
    /// <summary>For places: the place most of them would get; null otherwise.</summary>
    [JsonPropertyName("place")] public string? Place { get; init; }
    /// <summary>How many would get it.</summary>
    [JsonPropertyName("placeCount")] public int PlaceCount { get; init; }
    /// <summary>How many the last fix of this kind changed, which Undo puts back; 0 when there is nothing to undo.</summary>
    [JsonPropertyName("undo")] public int Undo { get; init; }
}

/// <summary>The phone's copies on the computer, and how they are named there.</summary>
public sealed class ComputerNamesDto
{
    /// <summary>The computer.</summary>
    [JsonPropertyName("computer")] public string Computer { get; init; } = string.Empty;
    /// <summary>How many of the phone's files it holds.</summary>
    [JsonPropertyName("files")] public int Files { get; init; }
    /// <summary>How they are named there: "asOnPhone", "date" or "datePlace".</summary>
    [JsonPropertyName("style")] public string Style { get; init; } = string.Empty;
    /// <summary>A file's name as on the phone, for the example: "IMG_4821.HEIC"; null when the phone has none with a date.</summary>
    [JsonPropertyName("now")] public string? Now { get; init; }
    /// <summary>The same named by date: "2026-03-02 14.31.07.HEIC".</summary>
    [JsonPropertyName("after")] public string? After { get; init; }
    /// <summary>Renaming asked from the phone: "running", "ready" or "failed"; null when it was not asked since Trovolve started.</summary>
    [JsonPropertyName("stage")] public string? Stage { get; init; }
    /// <summary>Why the computer did not rename them.</summary>
    [JsonPropertyName("problem")] public string? Problem { get; init; }
    /// <summary>How many the computer renamed.</summary>
    [JsonPropertyName("renamed")] public int? Renamed { get; init; }
}

/// <summary>Fix dates and places: what needs a date or a place, and the computer's file names.</summary>
public sealed class FixesDto
{
    /// <summary>How many photos and videos need a date or a place, each once.</summary>
    [JsonPropertyName("total")] public int Total { get; init; }
    /// <summary>Photos a camera dated on its default, or in the future.</summary>
    [JsonPropertyName("dates")] public FixGroupDto Dates { get; init; } = new();
    /// <summary>Photos with no place that can be given one.</summary>
    [JsonPropertyName("places")] public FixGroupDto Places { get; init; } = new();
    /// <summary>The copies on the computer; null when the phone has been paired with none.</summary>
    [JsonPropertyName("names")] public ComputerNamesDto? Names { get; init; }
}

/// <summary>The date the photos with wrong dates should have.</summary>
public sealed class FixDatesRequest
{
    /// <summary>The date, "2026-03-02".</summary>
    [JsonPropertyName("date")] public string Date { get; init; } = string.Empty;
}

/// <summary>Which fix to undo, or how to name copies.</summary>
public sealed class FixKindRequest
{
    /// <summary>For Undo: "dates" or "places". For names: "asOnPhone", "date" or "datePlace".</summary>
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
}

/// <summary>How many a fix, or its Undo, changed.</summary>
public sealed class FixResultDto
{
    /// <summary>How many photos and videos, a Live Photo once.</summary>
    [JsonPropertyName("changed")] public int Changed { get; init; }
}

/// <summary>On a computer: what renaming a phone's copies by date would make of their names.</summary>
public sealed class RenamingDto
{
    /// <summary>How many would be renamed.</summary>
    [JsonPropertyName("count")] public int Count { get; init; }
    /// <summary>The first few, each its name now and after.</summary>
    [JsonPropertyName("rows")] public IReadOnlyList<RenamedFileDto> Rows { get; init; } = [];
}

/// <summary>A copy's name now and after renaming by date.</summary>
public sealed class RenamedFileDto
{
    /// <summary>"IMG_4821.HEIC".</summary>
    [JsonPropertyName("now")] public string Now { get; init; } = string.Empty;
    /// <summary>"2026-03-02 14.31.07.HEIC".</summary>
    [JsonPropertyName("after")] public string After { get; init; } = string.Empty;
}
