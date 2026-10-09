using System.Text.Json.Serialization;

namespace PhotoSense.Contracts.Organize;

/// <summary>Where files were taken: a town, or a landmark or area within reach of one.</summary>
public sealed class OrganizePlaceDto
{
    [JsonPropertyName("town")] public string Town { get; init; } = string.Empty;
    /// <summary>The region the town is in; the country where the list has no region for it.</summary>
    [JsonPropertyName("state")] public string State { get; init; } = string.Empty;
    [JsonPropertyName("country")] public string Country { get; init; } = string.Empty;
    /// <summary>A landmark or area known at this spot; absent when none is.</summary>
    [JsonPropertyName("area")] public string? Area { get; init; }
    [JsonPropertyName("latitude")] public double Latitude { get; init; }
    [JsonPropertyName("longitude")] public double Longitude { get; init; }
}

public sealed class OrganizeFileDto
{
    /// <summary>Stands for the file in requests about it. It changes when the file is moved.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("folder")] public string Folder { get; init; } = string.Empty;
    [JsonPropertyName("sizeBytes")] public long SizeBytes { get; init; }
    [JsonPropertyName("isVideo")] public bool IsVideo { get; init; }
    /// <summary>When it was taken, as the camera's clock had it; failing that, when the file was last changed. No zone.</summary>
    [JsonPropertyName("date")] public string Date { get; init; } = string.Empty;
    [JsonPropertyName("dateFromFile")] public bool DateFromFile { get; init; }
    [JsonPropertyName("width")] public int Width { get; init; }
    [JsonPropertyName("height")] public int Height { get; init; }
    [JsonPropertyName("durationSeconds")] public double? DurationSeconds { get; init; }
    /// <summary>Which of the listing's places it was taken at; absent with no position, or none near a known place.</summary>
    [JsonPropertyName("place")] public int? Place { get; init; }
}

public sealed class OrganizeFilesDto
{
    [JsonPropertyName("root")] public string Root { get; init; } = string.Empty;
    /// <summary>Every file's details were already on record from a scan, so nothing had to be read.</summary>
    [JsonPropertyName("fromScan")] public bool FromScan { get; init; }
    [JsonPropertyName("places")] public List<OrganizePlaceDto> Places { get; init; } = [];
    [JsonPropertyName("files")] public List<OrganizeFileDto> Files { get; init; } = [];
}

/// <summary>How far the reading of a folder has got.</summary>
public sealed class OrganizeProgressDto
{
    /// <summary>The folder is being read at the moment.</summary>
    [JsonPropertyName("reading")] public bool Reading { get; init; }
    /// <summary>Pictures and videos found in it.</summary>
    [JsonPropertyName("total")] public int Total { get; init; }
    /// <summary>How many of them have been gone through so far.</summary>
    [JsonPropertyName("done")] public int Done { get; init; }
    /// <summary>Tells one reading of the folder from the next: what was had of another reading is no part of this one.</summary>
    [JsonPropertyName("readingId")] public string? ReadingId { get; init; }
    /// <summary>How many files found so far come before the ones in this answer.</summary>
    [JsonPropertyName("from")] public int From { get; init; }
    /// <summary>More files have been found than this answer holds: ask again at once.</summary>
    [JsonPropertyName("more")] public bool More { get; init; }
    /// <summary>Files found since the ones the asker said it had, in the order they were found.</summary>
    [JsonPropertyName("files")] public List<OrganizeFileDto> Files { get; init; } = [];
    /// <summary>Places the asker has not had yet; a file's place is its number among all the places of the reading.</summary>
    [JsonPropertyName("places")] public List<OrganizePlaceDto> Places { get; init; } = [];
}

/// <summary>A file to move or copy, by its id.</summary>
public sealed class OrganizeRequestFile
{
    [JsonPropertyName("id")] public string? Id { get; init; }
    /// <summary>The name it should arrive under; its own when left out.</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }
    /// <summary>A subfolder of the destination for this file; either slash makes more. Left out, the destination decides.</summary>
    [JsonPropertyName("subfolder")] public string? Subfolder { get; init; }
}

/// <summary>Files to take out of the folder being organized, by their ids.</summary>
public sealed class OrganizeRemoveRequest
{
    /// <summary>The folder being organized: removed files are held inside it.</summary>
    [JsonPropertyName("root")] public string? Root { get; init; }
    [JsonPropertyName("files")] public List<OrganizeRequestFile> Files { get; init; } = [];
}

/// <summary>Where a set of files is to go and which files, for a plan; with how, for carrying it out.</summary>
public sealed class OrganizeRequest
{
    /// <summary>The folder the new folder is made in.</summary>
    [JsonPropertyName("basePath")] public string? BasePath { get; init; }
    /// <summary>The new folder's name; either slash makes subfolders.</summary>
    [JsonPropertyName("folderName")] public string? FolderName { get; init; }
    /// <summary>Put the files in the base folder itself, without a new folder.</summary>
    [JsonPropertyName("direct")] public bool Direct { get; init; }
    /// <summary>A subfolder for the year of each file that is given no subfolder of its own.</summary>
    [JsonPropertyName("yearSplit")] public bool YearSplit { get; init; }
    /// <summary>"copy" leaves the files where they are; anything else moves them.</summary>
    [JsonPropertyName("mode")] public string? Mode { get; init; }
    /// <summary>Bring Live Photo videos and edit files along with their pictures.</summary>
    [JsonPropertyName("companions")] public bool Companions { get; init; }
    /// <summary>What the folder is called in the record of what was done.</summary>
    [JsonPropertyName("label")] public string? Label { get; init; }
    [JsonPropertyName("files")] public List<OrganizeRequestFile> Files { get; init; } = [];
}

public sealed class OrganizeExistingDto
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("sizeBytes")] public long SizeBytes { get; init; }
    [JsonPropertyName("date")] public string Date { get; init; } = string.Empty;
    [JsonPropertyName("width")] public int Width { get; init; }
    [JsonPropertyName("height")] public int Height { get; init; }
    [JsonPropertyName("isVideo")] public bool IsVideo { get; init; }
    [JsonPropertyName("placeName")] public string? PlaceName { get; init; }
    /// <summary>Byte for byte the same file as the one arriving.</summary>
    [JsonPropertyName("identical")] public bool Identical { get; init; }
}

public sealed class OrganizeClashDto
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("existing")] public List<OrganizeExistingDto> Existing { get; init; } = [];
    [JsonPropertyName("nextFree")] public string NextFree { get; init; } = string.Empty;
}

public sealed class OrganizePlanItemDto
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("folder")] public string Folder { get; init; } = string.Empty;
}

public sealed class OrganizePlanDto
{
    [JsonPropertyName("destination")] public string Destination { get; init; } = string.Empty;
    [JsonPropertyName("exists")] public bool Exists { get; init; }
    [JsonPropertyName("items")] public List<OrganizePlanItemDto> Items { get; init; } = [];
    [JsonPropertyName("clashes")] public List<OrganizeClashDto> Clashes { get; init; } = [];
    /// <summary>Every file name already in each folder files are going to.</summary>
    [JsonPropertyName("taken")] public Dictionary<string, List<string>> Taken { get; init; } = [];
    [JsonPropertyName("companions")] public int Companions { get; init; }
}

public sealed class OrganizeMovedDto
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("path")] public string Path { get; init; } = string.Empty;
}

public sealed class OrganizeApplyDto
{
    [JsonPropertyName("batchId")] public string? BatchId { get; init; }
    [JsonPropertyName("done")] public int Done { get; init; }
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
    [JsonPropertyName("companions")] public int Companions { get; init; }
    [JsonPropertyName("renamed")] public int Renamed { get; init; }
    [JsonPropertyName("skipped")] public int Skipped { get; init; }
    [JsonPropertyName("problems")] public List<string> Problems { get; init; } = [];
    [JsonPropertyName("items")] public List<OrganizeMovedDto> Items { get; init; } = [];
}

public sealed class OrganizeBatchDto
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    /// <summary>The folder the files went to; for files that were removed, the folder they were removed from.</summary>
    [JsonPropertyName("label")] public string Label { get; init; } = string.Empty;
    /// <summary>"move", "copy" or "remove".</summary>
    [JsonPropertyName("mode")] public string Mode { get; init; } = string.Empty;
    [JsonPropertyName("count")] public int Count { get; init; }
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
    [JsonPropertyName("utc")] public DateTime Utc { get; init; }
}

public sealed class OrganizeUndoDto
{
    [JsonPropertyName("restored")] public int Restored { get; init; }
    [JsonPropertyName("skipped")] public int Skipped { get; init; }
    [JsonPropertyName("problems")] public List<string> Problems { get; init; } = [];
}
