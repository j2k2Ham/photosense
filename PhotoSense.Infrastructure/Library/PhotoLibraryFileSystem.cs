using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Infrastructure.Files;

namespace PhotoSense.Infrastructure.Library;

/// <summary>
/// A phone's photo library shown as one folder that holds every picture and video in it once, each under the
/// name it was taken with. A Live Photo is two files there, its picture and its video, as it is when copied off
/// the phone. Nothing in the library can be changed through here yet: every change is refused before anything
/// happens, so nothing is moved, copied or removed.
/// </summary>
public sealed class PhotoLibraryFileSystem : IPhotoFileSystem
{
    /// <summary>Why every change is refused.</summary>
    public const string ReadOnly = "PhotoSense cannot change the photos on this phone yet.";

    private readonly IPhotoLibrary _library;
    private IReadOnlyDictionary<string, (LibraryItem Item, LibraryFile File)>? _files;

    /// <param name="root">The path the library is shown at, as a folder.</param>
    public PhotoLibraryFileSystem(IPhotoLibrary library, string root)
    {
        _library = library;
        Root = Path.TrimEndingDirectorySeparator(root);
    }

    /// <summary>The path of the one folder the library is shown as.</summary>
    public string Root { get; }

    public bool FileExists(string path) => Find(path) is not null;

    public bool FolderExists(string path) => IsRoot(path);

    public StoredFile? Describe(string path) => Find(path) is { } found ? new StoredFile(found.File.Length, found.Item.ModifiedUtc) : null;

    // The library is read again each time its folder is listed, so that a scan sees what is in it now.
    public IReadOnlyList<string> FilesIn(string folder) => IsRoot(folder) ? [.. (_files = Read()).Keys] : [];

    public IReadOnlyList<string> FoldersIn(string folder) => [];

    public bool IsLink(string path) => false;

    public Stream OpenRead(string path)
    {
        var (item, file) = Find(path) ?? throw new FileNotFoundException("The picture is no longer in the photo library.", path);
        var stream = _library.Open(item.Id, file.Kind);
        if (stream.CanSeek) return stream;
        // A file is read from wherever it is asked to be, so one that can only be read from the start is held whole.
        var whole = new MemoryStream();
        using (stream) stream.CopyTo(whole);
        whole.Position = 0;
        return whole;
    }

    public bool IsCopyOf(string copy, string original)
    {
        using var part = OpenRead(copy);
        using var whole = OpenRead(original);
        return Copies.BeginsWith(whole, part);
    }

    public void Move(string from, string to) => throw new UnauthorizedAccessException(ReadOnly);

    public void Copy(string from, string to) => throw new UnauthorizedAccessException(ReadOnly);

    public void MakeFolder(string path) => throw new UnauthorizedAccessException(ReadOnly);

    public void RemoveEmptyFolder(string path) => throw new UnauthorizedAccessException(ReadOnly);

    public void DeleteCopy(string path) => throw new UnauthorizedAccessException(ReadOnly);

    public void Erase(string path) => throw new UnauthorizedAccessException(ReadOnly);

    private bool IsRoot(string path) => PhotoPath.Comparer.Equals(Path.TrimEndingDirectorySeparator(path), Root);

    private (LibraryItem Item, LibraryFile File)? Find(string path)
        => (_files ??= Read()).TryGetValue(path, out var found) ? found : null;

    private Dictionary<string, (LibraryItem Item, LibraryFile File)> Read()
    {
        var shown = new List<(LibraryItem Item, List<LibraryFile> Files, string Stem)>();
        foreach (var item in _library.Items())
        {
            // A picture shows its own files only: the picture or video as taken, and a Live Photo's video beside it.
            if (item.Files.FirstOrDefault(f => f.Kind is LibraryFileKind.Photo or LibraryFileKind.Video) is not { } taken) continue;
            shown.Add((item, [taken, .. item.Files.Where(f => f.Kind == LibraryFileKind.PairedVideo).Take(1)], Path.GetFileNameWithoutExtension(taken.FileName)));
        }

        // Pictures taken under the same name are told apart by a piece of each one's own id rather than by a count, so
        // that a name never comes to stand for another picture when one arrives or goes. Each of a picture's files
        // shares its name, as the picture and video of a Live Photo must to be known for one.
        var named = new Dictionary<string, (LibraryItem, LibraryFile)>(PhotoPath.Comparer);
        foreach (var group in shown.GroupBy(s => s.Stem, StringComparer.OrdinalIgnoreCase))
        {
            var tags = Tags(group.Select(s => s.Item.Id).ToList());
            foreach (var (item, files, stem) in group)
            {
                var name = tags is null ? stem : $"{stem} ({tags[item.Id]})";
                foreach (var file in files) named[Path.Combine(Root, name + Path.GetExtension(file.FileName))] = (item, file);
            }
        }
        return named;
    }

    // Null when one picture alone has the name. Otherwise the start of each one's id, as much as tells them apart.
    private static Dictionary<string, string>? Tags(IReadOnlyList<string> ids)
    {
        if (ids.Count == 1) return null;
        var plain = ids.ToDictionary(id => id, id => new string([.. id.Where(char.IsAsciiLetterOrDigit)]).ToUpperInvariant());
        for (var length = 8; ; length += 4)
        {
            var tags = plain.ToDictionary(p => p.Key, p => p.Value[..Math.Min(length, p.Value.Length)]);
            if (tags.Values.Distinct().Count() == ids.Count || length >= plain.Values.Max(v => v.Length)) return tags;
        }
    }
}
