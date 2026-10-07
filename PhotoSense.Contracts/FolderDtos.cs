using System.Text.Json.Serialization;

namespace PhotoSense.Contracts.Folders;

public sealed class FolderDto
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    /// <summary>The full path, as the service sees it.</summary>
    [JsonPropertyName("path")] public string Path { get; init; } = string.Empty;
}

public sealed class FolderListingDto
{
    /// <summary>The folder that was listed; absent when the starting places are listed instead.</summary>
    [JsonPropertyName("path")] public string? Path { get; init; }
    /// <summary>The folder it is in; absent at the top of a disk.</summary>
    [JsonPropertyName("parent")] public string? Parent { get; init; }
    [JsonPropertyName("folders")] public List<FolderDto> Folders { get; init; } = [];
}
