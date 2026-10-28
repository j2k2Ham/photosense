using System.Text.Json.Serialization;

namespace PhotoSense.Contracts.Library;

/// <summary>How much of the photos PhotoSense may see: "everything" (always so on a computer), "selected" or "nothing".</summary>
public sealed class PhotoAccessDto
{
    [JsonPropertyName("access")] public string Access { get; init; } = string.Empty;
}
