using System.Text.Json.Serialization;

namespace PhotoSense.Contracts.Removed;

/// <summary>A folder that holds removed files.</summary>
public sealed class RemovedFolderDto
{
    /// <summary>The folder's full path.</summary>
    [JsonPropertyName("path")] public string Path { get; init; } = string.Empty;
    /// <summary>How many files it holds, in every folder inside it.</summary>
    [JsonPropertyName("files")] public int Files { get; init; }
    /// <summary>How much they take up.</summary>
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
}

/// <summary>Everything that has been removed and not yet erased.</summary>
public sealed class RemovedFilesDto
{
    /// <summary>The folders that hold removed files; one that holds none is not listed.</summary>
    [JsonPropertyName("folders")] public List<RemovedFolderDto> Folders { get; init; } = [];
    /// <summary>How many files are waiting, in all.</summary>
    [JsonPropertyName("files")] public int Files { get; init; }
    /// <summary>How much they take up, in all.</summary>
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
}

/// <summary>The folders of removed files to erase, as they were listed.</summary>
public sealed class EraseRemovedRequest
{
    /// <summary>The folders, by their full paths.</summary>
    [JsonPropertyName("folders")] public List<string?> Folders { get; init; } = [];
}

/// <summary>What erasing did.</summary>
public sealed class EraseRemovedDto
{
    /// <summary>Files erased for good.</summary>
    [JsonPropertyName("erased")] public int Erased { get; init; }
    /// <summary>How much they took up.</summary>
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
    /// <summary>Files that could not be erased and are still there.</summary>
    [JsonPropertyName("skipped")] public int Skipped { get; init; }
    /// <summary>Why each file that could not be erased was left, naming it.</summary>
    [JsonPropertyName("problems")] public List<string> Problems { get; init; } = [];
}
