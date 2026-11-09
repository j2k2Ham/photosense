using System.Text.Json;

namespace PhotoSense.Api.About;

/// <summary>Who makes PhotoSense and how to reach them: product.json, at the root of the repository, built in.</summary>
/// <param name="Name">What the product is called.</param>
/// <param name="Publisher">Who makes it.</param>
/// <param name="Copyright">The copyright line.</param>
/// <param name="SupportEmail">Where to write for help; null until set.</param>
/// <param name="Website">The product's web site; null until set.</param>
/// <param name="PrivacyPolicyUrl">Where the privacy policy is published; null until set.</param>
/// <param name="TermsUrl">Where the terms of use are published; null until set.</param>
/// <param name="SupportUrl">Where the support page is published; null until set.</param>
/// <param name="GoverningLaw">The place whose law the terms are under; null until set.</param>
internal sealed record ProductInfo(string Name, string Publisher, string Copyright, string? SupportEmail, string? Website,
    string? PrivacyPolicyUrl, string? TermsUrl, string? SupportUrl, string? GoverningLaw)
{
    /// <summary>product.json as it was built into this program.</summary>
    public static ProductInfo Bundled { get; } = Read(Bundle.Open("product.json"));

    /// <summary>product.json; a value left blank there is not set yet.</summary>
    public static ProductInfo Read(Stream json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        string? Given(string name) => root.TryGetProperty(name, out var value) && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()!.Trim() : null;
        string Needed(string name) => Given(name) ?? throw new InvalidDataException($"product.json has no {name}.");
        return new ProductInfo(Needed("name"), Needed("publisher"), Needed("copyright"), Given("supportEmail"), Given("website"),
            Given("privacyPolicyUrl"), Given("termsUrl"), Given("supportUrl"), Given("governingLaw"));
    }
}

/// <summary>The files built into this assembly from the root of the repository (see PhotoSense.Api.csproj).</summary>
internal static class Bundle
{
    /// <summary>A file built in, by its name in the repository's root folder.</summary>
    /// <exception cref="InvalidOperationException">The build left it out.</exception>
    public static Stream Open(string name) =>
        typeof(Bundle).Assembly.GetManifestResourceStream($"PhotoSense.About.{name}") ?? throw new InvalidOperationException($"{name} was not built into PhotoSense.");
}
