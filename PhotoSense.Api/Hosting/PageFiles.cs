namespace PhotoSense.Api.Hosting;

/// <summary>Which file of the built page answers an address, for a program that hands out the page without a web server.</summary>
public static class PageFiles
{
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8", [".js"] = "text/javascript; charset=utf-8", [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8", [".txt"] = "text/plain; charset=utf-8", [".map"] = "application/json; charset=utf-8",
        [".svg"] = "image/svg+xml", [".png"] = "image/png", [".jpg"] = "image/jpeg", [".webp"] = "image/webp", [".ico"] = "image/x-icon",
        [".woff"] = "font/woff", [".woff2"] = "font/woff2",
    };

    /// <summary>
    /// The file, relative to the page's folder and with forward slashes, that answers <paramref name="path"/>:
    /// the file itself, or the page of that name. Null when none does.
    /// </summary>
    /// <param name="exists">Whether the page's folder holds a file at a relative path.</param>
    public static string? For(string path, Func<string, bool> exists)
    {
        var parts = Uri.UnescapeDataString(path).Split('/', StringSplitOptions.RemoveEmptyEntries);
        // Nothing outside the page's own folder is ever handed out.
        if (parts.Any(p => p is "." or ".." || p.Contains('\\'))) return null;
        var file = string.Join('/', parts);
        string[] candidates = file.Length == 0 ? ["index.html"] : [file, file + ".html", file + "/index.html"];
        return candidates.FirstOrDefault(exists);
    }

    /// <summary>What a web view is told a file of the page is.</summary>
    public static string ContentType(string file) => Types.GetValueOrDefault(Path.GetExtension(file), "application/octet-stream");
}
