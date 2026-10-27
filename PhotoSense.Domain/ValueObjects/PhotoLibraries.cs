namespace PhotoSense.Domain.ValueObjects;

/// <summary>
/// Folders in which a photo program keeps a library of its own, such as "Photos Library.photoslibrary" in a
/// Mac's Pictures folder. What is inside belongs to that program's database: a file moved or removed from
/// outside it breaks the library. PhotoSense never looks inside one, nor lets one be chosen.
/// </summary>
public static class PhotoLibraries
{
    // Photos, iPhoto, a library Photos took over from iPhoto, Aperture.
    private static readonly string[] Endings = [".photoslibrary", ".photolibrary", ".migratedphotolibrary", ".aplibrary"];
    private static readonly string[] Names = ["iPhoto Library", "Photo Booth Library"];

    /// <summary>Whether a folder of this name is a photo program's own library.</summary>
    public static bool IsLibrary(string folderName)
        => Endings.Any(e => folderName.EndsWith(e, StringComparison.OrdinalIgnoreCase)) || Names.Contains(folderName, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a path is such a library or is inside one.</summary>
    public static bool IsInLibrary(string path) => path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(IsLibrary);
}
