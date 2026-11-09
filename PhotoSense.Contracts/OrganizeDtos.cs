using System.Text.Json.Serialization;

namespace PhotoSense.Contracts.Organize;

/// <summary>Where files were taken: a town, or a landmark or area within reach of one.</summary>
public sealed class OrganizePlaceDto
{
    /// <summary>The nearest town.</summary>
    [JsonPropertyName("town")] public string Town { get; init; } = string.Empty;
    /// <summary>The region the town is in; the country where the list has no region for it.</summary>
    [JsonPropertyName("state")] public string State { get; init; } = string.Empty;
    /// <summary>The country it is in.</summary>
    [JsonPropertyName("country")] public string Country { get; init; } = string.Empty;
    /// <summary>A landmark or area known at this spot; absent when none is.</summary>
    [JsonPropertyName("area")] public string? Area { get; init; }
    /// <summary>Where the place is: degrees north.</summary>
    [JsonPropertyName("latitude")] public double Latitude { get; init; }
    /// <summary>Where the place is: degrees east.</summary>
    [JsonPropertyName("longitude")] public double Longitude { get; init; }
}

/// <summary>A picture or video in the folder being organized.</summary>
public sealed class OrganizeFileDto
{
    /// <summary>Stands for the file in requests about it. It changes when the file is moved.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    /// <summary>The file's name.</summary>
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    /// <summary>The folder it is in.</summary>
    [JsonPropertyName("folder")] public string Folder { get; init; } = string.Empty;
    /// <summary>The file's size.</summary>
    [JsonPropertyName("sizeBytes")] public long SizeBytes { get; init; }
    /// <summary>It is a video rather than a picture.</summary>
    [JsonPropertyName("isVideo")] public bool IsVideo { get; init; }
    /// <summary>When it was taken, as the camera's clock had it; failing that, when the file was last changed. No zone.</summary>
    [JsonPropertyName("date")] public string Date { get; init; } = string.Empty;
    /// <summary>Nothing in the file says when it was taken, so the date is the file's own.</summary>
    [JsonPropertyName("dateFromFile")] public bool DateFromFile { get; init; }
    /// <summary>Width in pixels as displayed; 0 when the file does not say.</summary>
    [JsonPropertyName("width")] public int Width { get; init; }
    /// <summary>Height in pixels as displayed; 0 when the file does not say.</summary>
    [JsonPropertyName("height")] public int Height { get; init; }
    /// <summary>Playing time, for a video.</summary>
    [JsonPropertyName("durationSeconds")] public double? DurationSeconds { get; init; }
    /// <summary>Which of the listing's places it was taken at; absent with no position, or none near a known place.</summary>
    [JsonPropertyName("place")] public int? Place { get; init; }
    /// <summary>The video half of a Live Photo whose picture is beside it: it goes wherever the picture goes. Told once the whole folder is read.</summary>
    [JsonPropertyName("livePhoto")] public bool LivePhoto { get; init; }
}

/// <summary>Every picture and video under a folder, once the folder has been read.</summary>
public sealed class OrganizeFilesDto
{
    /// <summary>The folder listed.</summary>
    [JsonPropertyName("root")] public string Root { get; init; } = string.Empty;
    /// <summary>Every file's details were already on record from a scan, so nothing had to be read.</summary>
    [JsonPropertyName("fromScan")] public bool FromScan { get; init; }
    /// <summary>The places the files were taken at, each once; a file names one by its number.</summary>
    [JsonPropertyName("places")] public List<OrganizePlaceDto> Places { get; init; } = [];
    /// <summary>The files, the latest first.</summary>
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
    /// <summary>The file, by the id the listing gave it.</summary>
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
    /// <summary>The files to take out.</summary>
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
    /// <summary>The files.</summary>
    [JsonPropertyName("files")] public List<OrganizeRequestFile> Files { get; init; } = [];
}

/// <summary>A file already where another is going, under its name or that name with a number.</summary>
public sealed class OrganizeExistingDto
{
    /// <summary>The file, by the id the listing gives it.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    /// <summary>The file's name.</summary>
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    /// <summary>The file's size.</summary>
    [JsonPropertyName("sizeBytes")] public long SizeBytes { get; init; }
    /// <summary>When it was taken, or failing that last changed; no zone.</summary>
    [JsonPropertyName("date")] public string Date { get; init; } = string.Empty;
    /// <summary>Width in pixels as displayed.</summary>
    [JsonPropertyName("width")] public int Width { get; init; }
    /// <summary>Height in pixels as displayed.</summary>
    [JsonPropertyName("height")] public int Height { get; init; }
    /// <summary>It is a video rather than a picture.</summary>
    [JsonPropertyName("isVideo")] public bool IsVideo { get; init; }
    /// <summary>Where it was taken, in words, when it says.</summary>
    [JsonPropertyName("placeName")] public string? PlaceName { get; init; }
    /// <summary>Byte for byte the same file as the one arriving.</summary>
    [JsonPropertyName("identical")] public bool Identical { get; init; }
}

/// <summary>A file whose name is taken where it would go.</summary>
public sealed class OrganizeClashDto
{
    /// <summary>The file going, by its id.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    /// <summary>The files already there under its name or that name with a number, the very name first.</summary>
    [JsonPropertyName("existing")] public List<OrganizeExistingDto> Existing { get; init; } = [];
    /// <summary>The name it would arrive under.</summary>
    [JsonPropertyName("nextFree")] public string NextFree { get; init; } = string.Empty;
}

/// <summary>A file that would move, and where to.</summary>
public sealed class OrganizePlanItemDto
{
    /// <summary>The file, by its id.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    /// <summary>The folder it would go to.</summary>
    [JsonPropertyName("folder")] public string Folder { get; init; } = string.Empty;
}

/// <summary>Where each file would go and which names are taken there; nothing has moved.</summary>
public sealed class OrganizePlanDto
{
    /// <summary>The folder the files would go to.</summary>
    [JsonPropertyName("destination")] public string Destination { get; init; } = string.Empty;
    /// <summary>That folder is there already.</summary>
    [JsonPropertyName("exists")] public bool Exists { get; init; }
    /// <summary>The files that would move.</summary>
    [JsonPropertyName("items")] public List<OrganizePlanItemDto> Items { get; init; } = [];
    /// <summary>The files whose names are taken where they would go.</summary>
    [JsonPropertyName("clashes")] public List<OrganizeClashDto> Clashes { get; init; } = [];
    /// <summary>Every file name already in each folder files are going to.</summary>
    [JsonPropertyName("taken")] public Dictionary<string, List<string>> Taken { get; init; } = [];
    /// <summary>Live Photo videos and edit files that would be left behind unless brought along.</summary>
    [JsonPropertyName("companions")] public int Companions { get; init; }
}

/// <summary>Where a file that went now is.</summary>
public sealed class OrganizeMovedDto
{
    /// <summary>The file, by the id it was asked for by.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    /// <summary>Where it is now; for a copy, where the copy is.</summary>
    [JsonPropertyName("path")] public string Path { get; init; } = string.Empty;
}

/// <summary>What a move, copy or removal did.</summary>
public sealed class OrganizeApplyDto
{
    /// <summary>What to undo it by; absent when nothing moved.</summary>
    [JsonPropertyName("batchId")] public string? BatchId { get; init; }
    /// <summary>Files asked for that went.</summary>
    [JsonPropertyName("done")] public int Done { get; init; }
    /// <summary>How much they take up.</summary>
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
    /// <summary>Live Photo videos and edit files that went with them.</summary>
    [JsonPropertyName("companions")] public int Companions { get; init; }
    /// <summary>Files that arrived under another name, so that nothing was replaced.</summary>
    [JsonPropertyName("renamed")] public int Renamed { get; init; }
    /// <summary>Files asked for that did not go.</summary>
    [JsonPropertyName("skipped")] public int Skipped { get; init; }
    /// <summary>Why each of those did not, in words for the person.</summary>
    [JsonPropertyName("problems")] public List<string> Problems { get; init; } = [];
    /// <summary>Where each file that went now is.</summary>
    [JsonPropertyName("items")] public List<OrganizeMovedDto> Items { get; init; } = [];
}

/// <summary>A move, copy or removal that can still be undone.</summary>
public sealed class OrganizeBatchDto
{
    /// <summary>What to undo it by.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    /// <summary>The folder the files went to; for files that were removed, the folder they were removed from.</summary>
    [JsonPropertyName("label")] public string Label { get; init; } = string.Empty;
    /// <summary>"move", "copy" or "remove".</summary>
    [JsonPropertyName("mode")] public string Mode { get; init; } = string.Empty;
    /// <summary>Files asked for that went, not counting what went with them.</summary>
    [JsonPropertyName("count")] public int Count { get; init; }
    /// <summary>How much they take up.</summary>
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
    /// <summary>When it was done.</summary>
    [JsonPropertyName("utc")] public DateTime Utc { get; init; }
}

/// <summary>What undoing a move, copy or removal did.</summary>
public sealed class OrganizeUndoDto
{
    /// <summary>Files put back, or copies of them deleted.</summary>
    [JsonPropertyName("restored")] public int Restored { get; init; }
    /// <summary>Files that could not be put back.</summary>
    [JsonPropertyName("skipped")] public int Skipped { get; init; }
    /// <summary>Why each of those could not be, in words for the person.</summary>
    [JsonPropertyName("problems")] public List<string> Problems { get; init; } = [];
}
