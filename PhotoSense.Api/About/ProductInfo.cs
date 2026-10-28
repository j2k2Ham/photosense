using System.Text.Json;

namespace PhotoSense.Api.About;

/// <summary>Who makes PhotoSense and how to reach them: product.json, at the root of the repository, built in.</summary>
public sealed record ProductInfo(string Name, string Publisher, string Copyright, string? SupportEmail, string? Website,
    string? PrivacyPolicyUrl, string? TermsUrl, string? SupportUrl, string? GoverningLaw)
{
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
public static class Bundle
{
    public static Stream Open(string name) =>
        typeof(Bundle).Assembly.GetManifestResourceStream($"PhotoSense.About.{name}") ?? throw new InvalidOperationException($"{name} was not built into PhotoSense.");
}
