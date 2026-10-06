using System.Text.RegularExpressions;

namespace PhotoSense.Application.Scanning.Services;

/// <summary>
/// How iOS names the files of one item. For the picture IMG_1234 it can write:
/// IMG_1234.HEIC (the picture), IMG_1234.MOV (its Live Photo video), IMG_E1234.HEIC (an edited version),
/// and IMG_1234.AAE / IMG_O1234.AAE (the record of the edits). Items saved from elsewhere get four
/// letters in place of "IMG_": ABCD1234, ABCDE1234.
/// </summary>
public static class PhotoNaming
{
    // Prefix, optional E (edited) or O (sidecar) marker, four digits, then whatever a file manager appended to a copy, such as " (1)".
    private static readonly Regex ItemName = new(@"^(IMG_|[A-Z]{4})([EO]?)(\d{4})(\D.*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The name shared by every file of the item this file belongs to: IMG_E1234.HEIC and IMG_O1234.AAE give IMG_1234.</summary>
    public static string ItemOf(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var match = ItemName.Match(name);
        return (match.Success ? match.Groups[1].Value + match.Groups[3].Value + match.Groups[4].Value : name).ToUpperInvariant();
    }

    /// <summary>Whether the file is the edited version iOS saves beside an original.</summary>
    public static bool IsEditedRender(string fileName)
    {
        var match = ItemName.Match(Path.GetFileNameWithoutExtension(fileName));
        return match.Success && match.Groups[2].Value.Equals("E", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSidecar(string fileName) => Path.GetExtension(fileName).Equals(".aae", StringComparison.OrdinalIgnoreCase);
}
