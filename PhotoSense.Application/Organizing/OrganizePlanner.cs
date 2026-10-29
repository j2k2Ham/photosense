using System.Text.RegularExpressions;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Application.Organizing;

/// <summary>
/// Where a set of files is to go: a folder, made new or already there. Inside it a file can be given a
/// subfolder of its own; one that is given none goes into a subfolder for its year when the folder is split so.
/// </summary>
public sealed record OrganizeDestination
{
    private static readonly char[] Unusable = ['<', '>', ':', '"', '|', '?', '*'];

    /// <summary>The folder the files go to, before any split by year.</summary>
    public string Folder { get; }
    public bool YearSplit { get; }

    /// <param name="basePath">The folder the new folder is made in.</param>
    /// <param name="folderName">The new folder's name; either slash makes subfolders.</param>
    /// <param name="direct">Put the files in the base folder itself, without a new folder.</param>
    /// <exception cref="ArgumentException">The destination cannot be used; the message says why.</exception>
    /// <exception cref="DirectoryNotFoundException">The base folder is not there.</exception>
    public OrganizeDestination(string? basePath, string? folderName, bool direct, bool yearSplit)
    {
        if (string.IsNullOrWhiteSpace(basePath) || !Path.IsPathFullyQualified(basePath)) throw new ArgumentException("Choose where the files go.");
        if (!Directory.Exists(basePath)) throw new DirectoryNotFoundException($"Folder not found: {basePath}");
        YearSplit = yearSplit;
        Folder = Path.GetFullPath(basePath);
        if (direct) return;

        var names = Names(folderName);
        if (names.Count == 0) throw new ArgumentException("Give the folder a name first.");
        Folder = names.Aggregate(Folder, Path.Combine);
    }

    // The folders a name stands for, outermost first; either slash in it makes a subfolder.
    private static List<string> Names(string? name)
    {
        // Windows drops dots and spaces from the end of a name; so does this, before anything is made.
        var names = (name ?? string.Empty).Split('\\', '/').Select(n => n.Trim().TrimEnd('.', ' ')).Where(n => n.Length > 0).ToList();
        // A name of dots alone has gone by now, so nothing here can lead out of the folder it is made in.
        if (names.Any(n => n.IndexOfAny(Unusable) >= 0 || n.Any(char.IsControl))) throw new ArgumentException("Folder names cannot contain < > : \" / | ? or *");
        return names;
    }

    /// <summary>A subfolder asked for a file, as it will be made inside the destination; null when none is asked for.</summary>
    /// <exception cref="ArgumentException">It cannot be used as the name of a folder.</exception>
    public static string? Subfolder(string? wanted)
    {
        var names = Names(wanted);
        return names.Count > 0 ? Path.Combine([.. names]) : null;
    }

    /// <param name="subfolder">The subfolder asked for this file, if one was.</param>
    /// <exception cref="ArgumentException">The subfolder cannot be used as the name of a folder.</exception>
    public string FolderFor(LibraryFile file, string? subfolder = null)
        => Subfolder(subfolder) is { } inside ? Path.Combine(Folder, inside) : YearSplit ? Path.Combine(Folder, file.Date.Year.ToString("0000")) : Folder;
}

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

/// <summary>
/// The files of each folder by the item they belong to, read once for a folder however many of its files
/// are asked about. Only a file whose item has other files can have companions.
/// </summary>
public sealed class SiblingIndex
{
    private readonly Dictionary<string, ILookup<string, string>> _items = new(PhotoPath.Comparer);

    /// <summary>The other files in the file's folder that carry its item's name.</summary>
    /// <param name="stillThere">
    /// Leave out files that have gone since the folder was read: for a caller that moves files as it goes.
    /// </param>
    public IReadOnlyList<string> SiblingsOf(string path, bool stillThere = false)
    {
        var folder = Path.GetDirectoryName(path)!;
        if (!_items.TryGetValue(folder, out var items))
            _items[folder] = items = (Directory.Exists(folder) ? Directory.EnumerateFiles(folder) : []).ToLookup(PhotoNaming.ItemOf);
        return items[PhotoNaming.ItemOf(path)].Where(f => !PhotoPath.Comparer.Equals(f, path) && (!stillThere || File.Exists(f))).ToList();
    }
}

public sealed record PlannedFile(LibraryFile File, string Folder);
/// <summary>A file already in the folder another is going to, under that file's name or that name with a number.</summary>
public sealed record ExistingFile(LibraryFile File, string? PlaceName, bool Identical);
public sealed record PlannedClash(LibraryFile File, IReadOnlyList<ExistingFile> Existing, string NextFree);

/// <param name="Clashes">The files whose names are taken; each lists at least the file that has the very name.</param>
/// <param name="Taken">Every file name in each folder files are going to.</param>
public sealed record OrganizePlan(
    string Destination, bool Exists, IReadOnlyList<PlannedFile> Items, IReadOnlyList<PlannedClash> Clashes,
    IReadOnlyDictionary<string, List<string>> Taken, int Companions);

/// <summary>Works out where each file would go and which names are already taken there. Nothing is moved.</summary>
public sealed class OrganizePlanner
{
    private readonly OrganizeLibrary _library;
    private readonly ICompanionFileFinder _companions;
    private readonly PlaceLookup _places;

    public OrganizePlanner(OrganizeLibrary library, ICompanionFileFinder companions, PlaceLookup places)
    {
        _library = library;
        _companions = companions;
        _places = places;
    }

    /// <param name="subfolders">A subfolder of the destination for each file, in the order of the files; none for a file leaves it to the destination.</param>
    public async Task<OrganizePlan> PlanAsync(OrganizeDestination destination, IReadOnlyList<string> fileIds, IReadOnlyList<string?>? subfolders = null, CancellationToken ct = default)
    {
        // Each file is taken as the listing last saw it. Asking the disk about thousands of files again takes
        // long on a slow or busy disk, and whether a file is still there is settled when it is moved.
        var found = new PlannedFile?[fileIds.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, fileIds.Count), ct, async (i, token) =>
        {
            // A file nothing is known of, or already in the folder it would go to, has nowhere to be moved.
            if ((_library.Recall(fileIds[i]) ?? await _library.FindAsync(fileIds[i], token)) is not { } file) return;
            var folder = destination.FolderFor(file, subfolders?.ElementAtOrDefault(i));
            if (!PhotoPath.Comparer.Equals(file.Folder, folder)) found[i] = new PlannedFile(file, folder);
        });
        var items = found.OfType<PlannedFile>().ToList();

        var taken = new Dictionary<string, List<string>>(PhotoPath.Comparer);
        foreach (var folder in items.Select(i => i.Folder).Distinct(PhotoPath.Comparer).Where(Directory.Exists))
            taken[folder] = Directory.EnumerateFiles(folder).Select(f => Path.GetFileName(f)!).ToList();

        var clashes = new List<PlannedClash>();
        foreach (var (file, folder) in items)
        {
            if (!taken.TryGetValue(folder, out var names) || !names.Contains(file.Name, PhotoPath.Comparer)) continue;
            var family = NameRules.Family(file.Name);
            var existing = new List<ExistingFile>();
            // The file with the very name first, then the numbered ones in the order of their numbers.
            foreach (var name in names.Where(n => family.IsMatch(n)).OrderBy(n => n.Length).ThenBy(n => n, StringComparer.OrdinalIgnoreCase).ToList())
            {
                // Gone since the folder was read: its name is free again.
                if (await _library.DescribeAsync(Path.Combine(folder, name), ct) is not { } there) { names.Remove(name); continue; }
                // Files of different sizes cannot be the same; only equal sizes are worth reading through.
                var identical = there.SizeBytes == file.SizeBytes && await SameBytesAsync(there, file, ct);
                existing.Add(new ExistingFile(there, _places.Describe(there.Details.Latitude, there.Details.Longitude), identical));
            }
            if (names.Contains(file.Name, PhotoPath.Comparer)) clashes.Add(new PlannedClash(file, existing, NameRules.NextFree(file.Name, n => names.Contains(n, PhotoPath.Comparer))));
        }

        // Live Photo videos and edit files that would be left behind, not counting any that are going anyway.
        var going = items.Select(i => i.File.Path).ToHashSet(PhotoPath.Comparer);
        var siblings = new SiblingIndex();
        var withSiblings = items.Select(i => (i.File.Path, Siblings: siblings.SiblingsOf(i.File.Path))).Where(i => i.Siblings.Count > 0).ToList();
        var companions = withSiblings.AsParallel().SelectMany(i => _companions.FindAmong(i.Path, i.Siblings)).Where(c => !going.Contains(c)).Distinct(PhotoPath.Comparer).Count();

        return new OrganizePlan(destination.Folder, Directory.Exists(destination.Folder), items, clashes, taken, companions);
    }

    private async Task<bool> SameBytesAsync(LibraryFile one, LibraryFile other, CancellationToken ct)
    {
        try { return await _library.HashAsync(one, ct) == await _library.HashAsync(other, ct); }
        // One of them has gone or cannot be opened: they are not known to be the same.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
