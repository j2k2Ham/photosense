using System.Text.Json.Serialization;

namespace PhotoSense.Contracts.Duplicates;

/// <summary>A photo a scan recorded, as the Clean up screens show it.</summary>
public sealed class PhotoItemDto
{
    /// <summary>The photo's record, by which requests name it.</summary>
    [JsonPropertyName("id")] public Guid Id { get; init; }
    /// <summary>The file's name.</summary>
    [JsonPropertyName("fileName")] public string FileName { get; init; } = string.Empty;
    /// <summary>The file's full path.</summary>
    [JsonPropertyName("sourcePath")] public string SourcePath { get; init; } = string.Empty;
    /// <summary>Folder the file is in.</summary>
    [JsonPropertyName("folder")] public string Folder { get; init; } = string.Empty;
    /// <summary>The file's size when it was scanned.</summary>
    [JsonPropertyName("fileSizeBytes")] public long FileSizeBytes { get; init; }
    /// <summary>Width in pixels as displayed.</summary>
    [JsonPropertyName("width")] public int Width { get; init; }
    /// <summary>Height in pixels as displayed.</summary>
    [JsonPropertyName("height")] public int Height { get; init; }
    /// <summary>The format, such as "JPEG" or "HEIC".</summary>
    [JsonPropertyName("format")] public string? Format { get; init; }
    /// <summary>A video: matched only as an identical file, and shown without a picture.</summary>
    [JsonPropertyName("isVideo")] public bool IsVideo { get; init; }
    /// <summary>Playing time, for a video.</summary>
    [JsonPropertyName("durationSeconds")] public double? DurationSeconds { get; init; }
    /// <summary>Capture time as recorded by the camera: local time where the picture was taken, no zone.</summary>
    [JsonPropertyName("takenOn")] public DateTime? TakenOn { get; init; }
    /// <summary>The camera or phone that took it.</summary>
    [JsonPropertyName("cameraModel")] public string? CameraModel { get; init; }
    /// <summary>Where it was taken: degrees north.</summary>
    [JsonPropertyName("latitude")] public double? Latitude { get; init; }
    /// <summary>Where it was taken: degrees east.</summary>
    [JsonPropertyName("longitude")] public double? Longitude { get; init; }
    /// <summary>The nearest town to where it was taken, such as "Buxton, North Carolina, US" or "Near Butte, Montana, US".</summary>
    [JsonPropertyName("placeName")] public string? PlaceName { get; init; }
    /// <summary>"Primary" or "Secondary": which of the scanned folders it is in.</summary>
    [JsonPropertyName("set")] public string? Set { get; init; }
    /// <summary>The user has marked this photo to keep; bulk removal skips it.</summary>
    [JsonPropertyName("kept")] public bool Kept { get; init; }
}

/// <summary>A photo matched against the one a group keeps.</summary>
public sealed class GroupMemberDto
{
    /// <summary>The photo.</summary>
    [JsonPropertyName("photo")] public PhotoItemDto Photo { get; init; } = new();
    /// <summary>identical, samePicture or similar.</summary>
    [JsonPropertyName("match")] public string Match { get; init; } = string.Empty;
    /// <summary>Why the group's keeper was preferred over this photo.</summary>
    [JsonPropertyName("keeperReason")] public string KeeperReason { get; init; } = string.Empty;
}

/// <summary>A photo to keep and the photos matched against it.</summary>
public sealed class DuplicateGroupDto
{
    /// <summary>Names the group in requests.</summary>
    [JsonPropertyName("key")] public string Key { get; init; } = string.Empty;
    /// <summary>The best copy: the one that stays.</summary>
    [JsonPropertyName("keeper")] public PhotoItemDto Keeper { get; init; } = new();
    /// <summary>The other copies, each compared with the keeper.</summary>
    [JsonPropertyName("members")] public IReadOnlyList<GroupMemberDto> Members { get; init; } = [];
    /// <summary>Bytes a removal of this group's duplicates would take off the folder.</summary>
    [JsonPropertyName("reclaimableBytes")] public long ReclaimableBytes { get; init; }
}

/// <summary>One page of the groups a scan found.</summary>
public sealed class DuplicateGroupsPageDto
{
    /// <summary>duplicates (safe to remove in bulk) or similar (review only).</summary>
    [JsonPropertyName("mode")] public string Mode { get; init; } = string.Empty;
    /// <summary>Which page this is, from 1.</summary>
    [JsonPropertyName("page")] public int Page { get; init; }
    /// <summary>How many groups a page holds.</summary>
    [JsonPropertyName("pageSize")] public int PageSize { get; init; }
    /// <summary>Groups matching the filter.</summary>
    [JsonPropertyName("total")] public int Total { get; init; }
    /// <summary>How many pages there are.</summary>
    [JsonPropertyName("totalPages")] public int TotalPages { get; init; }
    /// <summary>Files a bulk removal would take, across every duplicate group (not only this page).</summary>
    [JsonPropertyName("removableCount")] public int RemovableCount { get; init; }
    /// <summary>Bytes a bulk removal would free, across every duplicate group (not only this page).</summary>
    [JsonPropertyName("reclaimableBytes")] public long ReclaimableBytes { get; init; }
    /// <summary>This page's groups.</summary>
    [JsonPropertyName("items")] public IReadOnlyList<DuplicateGroupDto> Items { get; init; } = [];
}

/// <summary>Groups whose copies, or similar shots, go together as one change: more than an address can name.</summary>
public sealed class RemoveGroupsRequest
{
    /// <summary>The groups, by key.</summary>
    [JsonPropertyName("groups")] public List<string>? Groups { get; init; }
}

/// <summary>What removing the copies of one or more groups did.</summary>
public sealed class BulkRemovalResultDto
{
    /// <summary>Files moved to the folder that holds removed files.</summary>
    [JsonPropertyName("removed")] public int Removed { get; init; }
    /// <summary>Their combined size.</summary>
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
    /// <summary>Files left alone, because removing them could not be shown to be safe.</summary>
    [JsonPropertyName("skipped")] public int Skipped { get; init; }
    /// <summary>Sidecars and Live Photo videos moved along with the files they belonged to.</summary>
    [JsonPropertyName("companions")] public int Companions { get; init; }
    /// <summary>Why each file left alone was left, naming it.</summary>
    [JsonPropertyName("problems")] public IReadOnlyList<string> Problems { get; init; } = [];
    /// <summary>What to undo the removal by; absent when no file was moved.</summary>
    [JsonPropertyName("batchId")] public string? BatchId { get; init; }
}
