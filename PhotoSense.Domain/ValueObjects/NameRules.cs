using System.Text.RegularExpressions;

namespace PhotoSense.Domain.ValueObjects;

/// <summary>The rules for a file's name where it arrives, so that nothing already there is replaced.</summary>
public static class NameRules
{
    /// <summary>The first of "name (1).ext", "name (2).ext" and so on that nothing has yet.</summary>
    public static string NextFree(string fileName, Func<string, bool> taken)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName), extension = Path.GetExtension(fileName);
        for (int n = 1; ; n++)
        {
            var name = $"{stem} ({n}){extension}";
            if (!taken(name)) return name;
        }
    }

    /// <summary>Matches a name and the same name with a number: "IMG_1.JPG", "IMG_1 (1).JPG".</summary>
    public static Regex Family(string fileName) => Family(fileName, PhotoPath.Comparer.Equals("a", "A"));

    /// <summary>Matches a name and the same name with a number, with or without regard to case.</summary>
    /// <param name="fileName">The name.</param>
    /// <param name="ignoreCase">Whether two names differing only in case are one name, as they are on Windows.</param>
    public static Regex Family(string fileName, bool ignoreCase)
        => new($"^{Regex.Escape(Path.GetFileNameWithoutExtension(fileName))}( \\(\\d+\\))?{Regex.Escape(Path.GetExtension(fileName))}$",
            ignoreCase ? RegexOptions.IgnoreCase | RegexOptions.CultureInvariant : RegexOptions.CultureInvariant);

    /// <summary>Whether a name asked for can be given to the file: a plain name, with the extension the file has.</summary>
    public static bool Usable(string? wanted, string fileName)
        => !string.IsNullOrWhiteSpace(wanted) && wanted == wanted.Trim() && wanted.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
           && wanted.IndexOfAny(['<', '>', ':', '"', '|', '?', '*', '/', '\\']) < 0 && !wanted.EndsWith('.')
           && Path.GetFileNameWithoutExtension(wanted).Trim().Length > 0
           && string.Equals(Path.GetExtension(wanted), Path.GetExtension(fileName), StringComparison.OrdinalIgnoreCase);
}
