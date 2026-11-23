using System.Text.Json.Serialization;
using Trovolve.Contracts.Duplicates;

namespace Trovolve.Contracts.Cleaning;

/// <summary>How many of a category of Clean up there are, and what they take.</summary>
public sealed class CategorySummaryDto
{
    /// <summary>"largeVideos", "screenshots", "bursts", "blurry", "messaging" or "duplicates".</summary>
    [JsonPropertyName("category")] public string Category { get; init; } = string.Empty;
    /// <summary>How many pictures or videos; for bursts, the shots that would go.</summary>
    [JsonPropertyName("count")] public int Count { get; init; }
    /// <summary>What they take.</summary>
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
}

/// <summary>What Clean up's categories hold.</summary>
public sealed class CategoryReportDto
{
    /// <summary>Each category, largest first.</summary>
    [JsonPropertyName("categories")] public IReadOnlyList<CategorySummaryDto> Categories { get; init; } = [];
    /// <summary>Pictures no scan has measured the sharpness of yet.</summary>
    [JsonPropertyName("unmeasured")] public int Unmeasured { get; init; }
    /// <summary>The phone's storage, all of it; absent on a computer.</summary>
    [JsonPropertyName("storageTotal")] public long? StorageTotal { get; init; }
    /// <summary>The phone's storage free; absent on a computer.</summary>
    [JsonPropertyName("storageFree")] public long? StorageFree { get; init; }
}

/// <summary>A burst: every shot, in the order taken, and the one kept.</summary>
public sealed class BurstDto
{
    /// <summary>The shot kept: the sharpest.</summary>
    [JsonPropertyName("keeper")] public Guid Keeper { get; init; }
    /// <summary>Every shot, in the order taken.</summary>
    [JsonPropertyName("shots")] public IReadOnlyList<PhotoItemDto> Shots { get; init; } = [];
}

/// <summary>The pictures and videos chosen to delete from Clean up's categories.</summary>
public sealed class CategoryDeleteRequest
{
    /// <summary>The photos, by their ids.</summary>
    [JsonPropertyName("ids")] public IReadOnlyList<Guid> Ids { get; init; } = [];
}

/// <summary>What deleting from Clean up's categories did.</summary>
public sealed class CategoryRemovalDto
{
    /// <summary>How many went.</summary>
    [JsonPropertyName("deleted")] public int Deleted { get; init; }
    /// <summary>What they took.</summary>
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
    /// <summary>How many were left, as changed since the scan.</summary>
    [JsonPropertyName("skipped")] public int Skipped { get; init; }
    /// <summary>Why each left was left.</summary>
    [JsonPropertyName("problems")] public IReadOnlyList<string> Problems { get; init; } = [];
    /// <summary>On a computer, what Undo puts them back by; absent on a phone, where they went to its own bin.</summary>
    [JsonPropertyName("batchId")] public Guid? BatchId { get; init; }
}
