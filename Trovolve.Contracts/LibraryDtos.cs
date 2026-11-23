using System.Text.Json.Serialization;

namespace Trovolve.Contracts.Library;

/// <summary>How much of the photos Trovolve may see: "everything" (always so on a computer), "selected" or "nothing".</summary>
public sealed class PhotoAccessDto
{
    /// <summary>"everything", "selected" or "nothing".</summary>
    [JsonPropertyName("access")] public string Access { get; init; } = string.Empty;
}
