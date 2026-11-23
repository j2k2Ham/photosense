using System.Text.Json.Serialization;

namespace Trovolve.Contracts.Removed;

/// <summary>A folder that holds removed files.</summary>
public sealed class RemovedFolderDto
{
    /// <summary>The folder's full path.</summary>
    [JsonPropertyName("path")] public string Path { get; init; } = string.Empty;
    /// <summary>How many files it holds, in every folder inside it.</summary>
    [JsonPropertyName("files")] public int Files { get; init; }
    /// <summary>How much they take up.</summary>
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
    /// <summary>Its files go to a bin they can be got back from; otherwise, on a drive with none, they are erased for good.</summary>
    [JsonPropertyName("recyclable")] public bool Recyclable { get; init; }
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

/// <summary>What emptying the folders of removed files did.</summary>
public sealed class EraseRemovedDto
{
    /// <summary>Files that went: to a bin, or erased for good where there was none.</summary>
    [JsonPropertyName("erased")] public int Erased { get; init; }
    /// <summary>Of those, the files that went to a bin they can be got back from: the Recycle Bin, the Trash, or a phone's own.</summary>
    [JsonPropertyName("recycled")] public int Recycled { get; init; }
    /// <summary>How much they took up.</summary>
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
    /// <summary>Files that could not be erased and are still there.</summary>
    [JsonPropertyName("skipped")] public int Skipped { get; init; }
    /// <summary>Why each file that could not be erased was left, naming it.</summary>
    [JsonPropertyName("problems")] public List<string> Problems { get; init; } = [];
}
