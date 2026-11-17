using System.Text.Json.Serialization;

namespace PhotoSense.Contracts.Organizing;

/// <summary>A photo or video of Swipe to clean: of today's set, or marked to delete on a day before.</summary>
public sealed class SwipeCardDto
{
    /// <summary>Its id, as Library knows it: its details are in Library's listing, its preview at organize/files/{id}/thumbnail.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    /// <summary>What it takes, a Live Photo's video included.</summary>
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
    /// <summary>Every file of it is on the computer, checked.</summary>
    [JsonPropertyName("onComputer")] public bool OnComputer { get; init; }
    /// <summary>Marked to delete (true), kept (false), or not swiped yet (absent).</summary>
    [JsonPropertyName("delete")] public bool? Delete { get; init; }
    /// <summary>Marked to delete, then tapped on the summary to be kept after all.</summary>
    [JsonPropertyName("spared")] public bool Spared { get; init; }
}

/// <summary>A day of the last week, and whether something was swiped on it.</summary>
public sealed class SwipeDayDto
{
    /// <summary>The day: "2026-11-17".</summary>
    [JsonPropertyName("date")] public string Date { get; init; } = string.Empty;
    /// <summary>Something was swiped on it.</summary>
    [JsonPropertyName("swiped")] public bool Swiped { get; init; }
}

/// <summary>What a deletion at the end of a session did.</summary>
public sealed class SwipeFreedDto
{
    /// <summary>How many went to the phone's Recently Deleted or bin.</summary>
    [JsonPropertyName("deleted")] public int Deleted { get; init; }
    /// <summary>What they took.</summary>
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
    /// <summary>How many stay marked for another day.</summary>
    [JsonPropertyName("left")] public int Left { get; init; }
}

/// <summary>How Swipe to clean stands today.</summary>
public sealed class SwipeStateDto
{
    /// <summary>Today: "2026-11-17".</summary>
    [JsonPropertyName("date")] public string Date { get; init; } = string.Empty;
    /// <summary>Today's set, in the order shown, with what was decided of each.</summary>
    [JsonPropertyName("set")] public IReadOnlyList<SwipeCardDto> Set { get; init; } = [];
    /// <summary>Photos marked to delete on days before and not deleted yet.</summary>
    [JsonPropertyName("earlier")] public IReadOnlyList<SwipeCardDto> Earlier { get; init; } = [];
    /// <summary>The last seven days, today last.</summary>
    [JsonPropertyName("week")] public IReadOnlyList<SwipeDayDto> Week { get; init; } = [];
    /// <summary>How many days in a row something was swiped.</summary>
    [JsonPropertyName("streak")] public int Streak { get; init; }
    /// <summary>What today's last deletion deleted; absent before one.</summary>
    [JsonPropertyName("freed")] public SwipeFreedDto? Freed { get; init; }
    /// <summary>How many photos and videos have never been swiped, today's set aside.</summary>
    [JsonPropertyName("unswiped")] public int Unswiped { get; init; }
}

/// <summary>What is decided of a photo of today's set.</summary>
public sealed class SwipeDecideRequest
{
    /// <summary>The photo, by Library's id.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    /// <summary>Marks it to delete; otherwise it is kept.</summary>
    [JsonPropertyName("delete")] public bool Delete { get; init; }
}

/// <summary>A photo marked to delete kept after all on the summary, or marked again.</summary>
public sealed class SwipeSpareRequest
{
    /// <summary>The photo, by Library's id.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    /// <summary>Keeps it; otherwise it is marked to delete again.</summary>
    [JsonPropertyName("spared")] public bool Spared { get; init; }
}

/// <summary>How many of those marked to delete go now.</summary>
public sealed class SwipeDeleteRequest
{
    /// <summary>The most to delete, the largest first; none for all of them.</summary>
    [JsonPropertyName("atMost")] public int? AtMost { get; init; }
}
