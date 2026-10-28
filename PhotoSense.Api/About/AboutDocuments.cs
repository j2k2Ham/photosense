namespace PhotoSense.Api.About;

/// <summary>
/// The privacy policy, the terms of use and the support page, as the web site has them (site/pages), with product.json's
/// details filled in. tools/site.mjs fills them in the same way for the site.
/// </summary>
public static class AboutDocuments
{
    private static readonly Dictionary<string, string> Files = new() { ["privacy"] = "privacy-policy.md", ["terms"] = "terms-of-use.md", ["support"] = "support.md" };

    /// <summary>The document's Markdown, or null when there is no document of that name.</summary>
    public static string? Text(string name, ProductInfo product)
    {
        if (!Files.TryGetValue(name, out var file)) return null;
        using var reader = new StreamReader(Bundle.Open(file));
        return Fill(reader.ReadToEnd(), product);
    }

    // What is not set yet says so, rather than leaving a gap in a sentence.
    public static string Fill(string text, ProductInfo product) => text
        .Replace("{{name}}", product.Name)
        .Replace("{{publisher}}", product.Publisher)
        .Replace("{{supportEmail}}", product.SupportEmail ?? "(the support address is not set yet)")
        .Replace("{{governingLaw}}", product.GoverningLaw ?? "(the place whose law applies is not set yet)");
}
