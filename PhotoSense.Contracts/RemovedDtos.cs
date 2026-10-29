using System.Text.Json.Serialization;

namespace PhotoSense.Contracts.Removed;

/// <summary>A folder that holds removed files.</summary>
public sealed class RemovedFolderDto
{
    [JsonPropertyName("path")] public string Path { get; init; } = string.Empty;
    [JsonPropertyName("files")] public int Files { get; init; }
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
}

/// <summary>Everything that has been removed and not yet erased.</summary>
public sealed class RemovedFilesDto
{
    /// <summary>The folders that hold removed files; one that holds none is not listed.</summary>
    [JsonPropertyName("folders")] public List<RemovedFolderDto> Folders { get; init; } = [];
    [JsonPropertyName("files")] public int Files { get; init; }
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
}

/// <summary>The folders of removed files to erase, as they were listed.</summary>
public sealed class EraseRemovedRequest
{
    [JsonPropertyName("folders")] public List<string?> Folders { get; init; } = [];
}

public sealed class EraseRemovedDto
{
    [JsonPropertyName("erased")] public int Erased { get; init; }
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
    /// <summary>Files that could not be erased and are still there.</summary>
    [JsonPropertyName("skipped")] public int Skipped { get; init; }
    [JsonPropertyName("problems")] public List<string> Problems { get; init; } = [];
}
