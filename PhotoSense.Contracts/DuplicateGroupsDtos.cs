using System.Text.Json.Serialization;

namespace PhotoSense.Contracts.Duplicates;

public sealed class PhotoItemDto
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("fileName")] public string FileName { get; init; } = string.Empty;
    [JsonPropertyName("sourcePath")] public string SourcePath { get; init; } = string.Empty;
    /// <summary>Folder the file is in.</summary>
    [JsonPropertyName("folder")] public string Folder { get; init; } = string.Empty;
    [JsonPropertyName("fileSizeBytes")] public long FileSizeBytes { get; init; }
    [JsonPropertyName("width")] public int Width { get; init; }
    [JsonPropertyName("height")] public int Height { get; init; }
    [JsonPropertyName("format")] public string? Format { get; init; }
    /// <summary>A video: matched only as an identical file, and shown without a picture.</summary>
    [JsonPropertyName("isVideo")] public bool IsVideo { get; init; }
    [JsonPropertyName("durationSeconds")] public double? DurationSeconds { get; init; }
    /// <summary>Capture time as recorded by the camera: local time where the picture was taken, no zone.</summary>
    [JsonPropertyName("takenOn")] public DateTime? TakenOn { get; init; }
    [JsonPropertyName("cameraModel")] public string? CameraModel { get; init; }
    [JsonPropertyName("latitude")] public double? Latitude { get; init; }
    [JsonPropertyName("longitude")] public double? Longitude { get; init; }
    /// <summary>The nearest town to where it was taken, such as "Buxton, North Carolina, US" or "Near Butte, Montana, US".</summary>
    [JsonPropertyName("placeName")] public string? PlaceName { get; init; }
    [JsonPropertyName("set")] public string? Set { get; init; }
    /// <summary>The user has marked this photo to keep; bulk removal skips it.</summary>
    [JsonPropertyName("kept")] public bool Kept { get; init; }
}

public sealed class GroupMemberDto
{
    [JsonPropertyName("photo")] public PhotoItemDto Photo { get; init; } = new();
    /// <summary>identical, samePicture or similar.</summary>
    [JsonPropertyName("match")] public string Match { get; init; } = string.Empty;
    /// <summary>Why the group's keeper was preferred over this photo.</summary>
    [JsonPropertyName("keeperReason")] public string KeeperReason { get; init; } = string.Empty;
}

public sealed class DuplicateGroupDto
{
    [JsonPropertyName("key")] public string Key { get; init; } = string.Empty;
    /// <summary>The best copy: the one that stays.</summary>
    [JsonPropertyName("keeper")] public PhotoItemDto Keeper { get; init; } = new();
    [JsonPropertyName("members")] public IReadOnlyList<GroupMemberDto> Members { get; init; } = [];
    /// <summary>Bytes a removal of this group's duplicates would take off the folder.</summary>
    [JsonPropertyName("reclaimableBytes")] public long ReclaimableBytes { get; init; }
}

public sealed class DuplicateGroupsPageDto
{
    /// <summary>duplicates (safe to remove in bulk) or similar (review only).</summary>
    [JsonPropertyName("mode")] public string Mode { get; init; } = string.Empty;
    [JsonPropertyName("page")] public int Page { get; init; }
    [JsonPropertyName("pageSize")] public int PageSize { get; init; }
    /// <summary>Groups matching the filter.</summary>
    [JsonPropertyName("total")] public int Total { get; init; }
    [JsonPropertyName("totalPages")] public int TotalPages { get; init; }
    /// <summary>Files a bulk removal would take, across every duplicate group (not only this page).</summary>
    [JsonPropertyName("removableCount")] public int RemovableCount { get; init; }
    [JsonPropertyName("reclaimableBytes")] public long ReclaimableBytes { get; init; }
    [JsonPropertyName("items")] public IReadOnlyList<DuplicateGroupDto> Items { get; init; } = [];
}

public sealed class BulkRemovalResultDto
{
    [JsonPropertyName("removed")] public int Removed { get; init; }
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
    [JsonPropertyName("skipped")] public int Skipped { get; init; }
    /// <summary>Sidecars and Live Photo videos moved along with the files they belonged to.</summary>
    [JsonPropertyName("companions")] public int Companions { get; init; }
    [JsonPropertyName("problems")] public IReadOnlyList<string> Problems { get; init; } = [];
}
