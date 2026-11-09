using System.Text.Json.Serialization;

namespace PhotoSense.Contracts.About;

/// <summary>What PhotoSense says about itself under About in the menu.</summary>
public sealed class AboutDto
{
    /// <summary>What the product is called.</summary>
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    /// <summary>The answering program's own version: 1.2.0.57 for an ordinary build, 1.2.0 for a release.</summary>
    [JsonPropertyName("version")] public string Version { get; init; } = string.Empty;
    /// <summary>Which program is answering: "server" on a computer, "iphone" on an iPhone or iPad.</summary>
    [JsonPropertyName("program")] public string Program { get; init; } = string.Empty;
    /// <summary>Who makes it.</summary>
    [JsonPropertyName("publisher")] public string Publisher { get; init; } = string.Empty;
    /// <summary>The copyright line.</summary>
    [JsonPropertyName("copyright")] public string Copyright { get; init; } = string.Empty;
    /// <summary>Where to write for help; null until one is set in product.json.</summary>
    [JsonPropertyName("supportEmail")] public string? SupportEmail { get; init; }
    /// <summary>The product's web site; null until one is set.</summary>
    [JsonPropertyName("website")] public string? Website { get; init; }
    /// <summary>Where the privacy policy is published; null until set.</summary>
    [JsonPropertyName("privacyPolicyUrl")] public string? PrivacyPolicyUrl { get; init; }
    /// <summary>Where the terms of use are published; null until set.</summary>
    [JsonPropertyName("termsUrl")] public string? TermsUrl { get; init; }
    /// <summary>Where the support page is published; null until set.</summary>
    [JsonPropertyName("supportUrl")] public string? SupportUrl { get; init; }
}
