using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Infrastructure.Files;

namespace PhotoSense.Infrastructure.Library;

/// <summary>
/// A phone's photo library shown as folders. The library itself is one folder, holding every picture and video
/// that is in none of the albums PhotoSense made; each of those albums is a folder inside it, and the folders the
/// album is in are folders above it. A picture is a file in the folder of its album, under the name it was taken
/// with; a Live Photo is two files there, its picture and its video, as it is when copied off the phone.
/// <para>
/// Moving a picture puts it in the album of the folder it goes to and takes it out of every other album PhotoSense
/// made; the picture itself never moves, and the person's own albums are left as they are. What is removed waits in
/// an album of its own, <see cref="RemovedAlbum"/>; erasing it sends it to the phone's own bin. A picture keeps its
/// name wherever it goes, and is never copied: the library holds one of it.
/// </para>
/// </summary>
public sealed class PhotoLibraryFileSystem : IPhotoFileSystem, IPhotoChangesTogether
{
    /// <summary>What the album of removed pictures is called in the phone's own Photos app.</summary>
    public const string RemovedAlbum = "PhotoSense Removed";

    /// <summary>Why a copy is refused.</summary>
    public const string NoCopies = "On the phone a picture is kept once, in one of PhotoSense's albums at a time: it can be moved there, not copied.";

    private readonly IPhotoLibrary _library;
    private Seen? _seen;
    // Folders made for pictures still to come: an album is made only when something goes into it.
    private readonly HashSet<string> _made = new(PhotoPath.Comparer);

    private sealed record Shown(LibraryItem Item, LibraryFile File, string Folder);

    private sealed record Seen(Dictionary<string, Shown> Files, HashSet<string> Folders, Dictionary<string, IReadOnlyList<string>> Albums, ILookup<string, string> FoldersOf);

    /// <param name="root">The path the library is shown at, as a folder.</param>
    public PhotoLibraryFileSystem(IPhotoLibrary library, string root)
    {
        _library = library;
        Root = Path.TrimEndingDirectorySeparator(root);
    }

    /// <summary>The path of the folder the library is shown as.</summary>
    public string Root { get; }

    public bool FileExists(string path) => Find(path) is not null;

    public bool FolderExists(string path)
    {
        var folder = Path.TrimEndingDirectorySeparator(path);
        return Same(folder, Root) || Look().Folders.Contains(folder) || _made.Contains(folder);
    }

    public StoredFile? Describe(string path) => Find(path) is { } found ? new StoredFile(found.File.Length, found.Item.ModifiedUtc) : null;

    // The library is read again each time its folder is listed, so that a scan sees what is in it now.
    public IReadOnlyList<string> FilesIn(string folder)
    {
        var at = Path.TrimEndingDirectorySeparator(folder);
        var seen = Same(at, Root) ? _seen = Read() : Look();
        return [.. seen.Files.Where(f => Same(f.Value.Folder, at)).Select(f => f.Key)];
    }

    public IReadOnlyList<string> FoldersIn(string folder)
    {
        var at = Path.TrimEndingDirectorySeparator(folder);
        return [.. Look().Folders.Concat(_made).Where(f => Same(Path.GetDirectoryName(f)!, at)).Distinct(PhotoPath.Comparer).Order(StringComparer.Ordinal)];
    }

    public bool IsLink(string path) => false;

    public Stream OpenRead(string path)
    {
        var found = Find(path) ?? throw new FileNotFoundException("The picture is no longer in the photo library.", path);
        var stream = _library.Open(found.Item.Id, found.File.Kind);
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

    public void Move(string from, string to) => MoveAll([new FileMove(from, to)]);

    public void Copy(string from, string to) => throw new UnauthorizedAccessException(NoCopies);

    public void MakeFolder(string path)
    {
        for (var at = Inside(path); !Same(at, Root); at = Path.GetDirectoryName(at)!) _made.Add(at);
    }

    public void RemoveEmptyFolder(string path) => RemoveEmptyFolders([path]);

    public void DeleteCopy(string path) => throw new UnauthorizedAccessException(NoCopies);

    public void Erase(string path) => EraseAll([path]);

    public void MoveAll(IReadOnlyList<FileMove> moves)
    {
        var seen = Look();
        var change = new LibraryChange();
        var going = new Dictionary<string, string>();
        foreach (var (from, to) in moves)
        {
            var target = Path.GetDirectoryName(Inside(to))!;
            if (!string.Equals(Path.GetFileName(from), Path.GetFileName(to), StringComparison.Ordinal))
                throw new IOException($"{Path.GetFileName(from)} keeps its own name on the phone, so it cannot arrive as {Path.GetFileName(to)}.");
            if (!seen.Files.TryGetValue(from, out var found))
            {
                // Gone already with the picture it belongs to, as a Live Photo's video goes with its picture.
                if (seen.Files.ContainsKey(to)) continue;
                throw new FileNotFoundException($"{Path.GetFileName(from)} is no longer in the photo library.", from);
            }
            var id = found.Item.Id;
            if (going.TryGetValue(id, out var already))
            {
                if (Same(already, target)) continue;
                throw new IOException($"{Path.GetFileName(from)} cannot go to two albums at once.");
            }
            going[id] = target;
            var now = seen.FoldersOf[id].ToList();
            foreach (var folder in now.Where(f => !Same(f, target) && !Same(f, Root))) change.Take.Add((id, NamesOf(folder, seen)));
            if (!Same(target, Root) && !now.Contains(target, PhotoPath.Comparer)) change.Add.Add((id, NamesOf(target, seen)));
        }
        Apply(change);
    }

    public void EraseAll(IReadOnlyList<string> paths)
    {
        var seen = Look();
        var change = new LibraryChange();
        // A picture gone since its folder was read has nothing left to erase.
        change.Delete.AddRange(paths.Select(p => seen.Files.GetValueOrDefault(p)?.Item.Id).OfType<string>().Distinct());
        Apply(change);
    }

    public void RemoveEmptyFolders(IReadOnlyList<string> folders)
    {
        var seen = Look();
        var change = new LibraryChange();
        var going = new HashSet<string>(PhotoPath.Comparer);
        // Deepest first: a folder whose folders go with it is empty too.
        foreach (var folder in folders.Select(Path.TrimEndingDirectorySeparator).OrderByDescending(f => f.Length))
        {
            if (Same(folder, Root) || !FolderExists(folder) || going.Contains(folder)) continue;
            if (seen.Files.Values.Any(f => Same(f.Folder, folder)) || FoldersIn(folder).Any(f => !going.Contains(f))) continue;
            going.Add(folder);
            _made.Remove(folder);
            if (seen.Folders.Contains(folder)) change.Drop.Add(NamesOf(folder, seen));
        }
        Apply(change);
    }

    private void Apply(LibraryChange change)
    {
        if (change.IsEmpty) return;
        try { _library.Change(change); }
        // What the library holds now is read afresh, whether or not the change was made.
        finally { _seen = null; }
    }

    private Seen Look() => _seen ??= Read();

    private Shown? Find(string path) => Look().Files.GetValueOrDefault(path);

    private static bool Same(string a, string b) => PhotoPath.Comparer.Equals(a, b);

    // A path in the library, or why it may not be used.
    private string Inside(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Same(full, Root) && !PhotoPath.Key(full).StartsWith(PhotoPath.Key(Root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new UnauthorizedAccessException($"{path} is not in the photo library.");
        return full;
    }

    // The names of an album, or of a folder of albums, as the library knows them: those of an album that is there,
    // otherwise the names of the folders the path goes through. The album of removed pictures has a name of its own.
    private IReadOnlyList<string> NamesOf(string folder, Seen seen)
    {
        if (seen.Albums.TryGetValue(folder, out var names)) return names;
        var parts = Path.GetRelativePath(Root, folder).Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        foreach (var (at, path) in seen.Albums)
            if (PhotoPath.Key(at).StartsWith(PhotoPath.Key(folder) + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return [.. path.Take(parts.Length)];
        return [.. parts.Select((p, i) => i == 0 && p == PhotoStorageOptions.RemovedFolderName ? RemovedAlbum : p)];
    }

    // Where an album is shown: below the library, through the folders it is in. A name that holds what a path cannot is shown without it.
    private string FolderOf(IReadOnlyList<string> names)
        => Path.Combine([Root, .. names.Select((n, i) => i == 0 && n == RemovedAlbum ? PhotoStorageOptions.RemovedFolderName : Plain(n))]);

    private static string Plain(string name)
    {
        var plain = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' ? '-' : c)).Trim();
        return plain.Length == 0 || plain.All(c => c == '.') ? "-" : plain;
    }

    private Seen Read()
    {
        var albums = new Dictionary<string, IReadOnlyList<string>>(PhotoPath.Comparer);
        var folders = new HashSet<string>(PhotoPath.Comparer);
        var inAlbums = new List<(string Item, string Folder)>();
        foreach (var album in _library.Albums())
        {
            var folder = FolderOf(album.Path);
            albums.TryAdd(folder, album.Path);
            for (var at = folder; !Same(at, Root); at = Path.GetDirectoryName(at)!) folders.Add(at);
            inAlbums.AddRange(album.ItemIds.Select(id => (id, folder)));
        }
        var albumsOf = inAlbums.ToLookup(a => a.Item, a => a.Folder);

        var shown = new List<(LibraryItem Item, List<LibraryFile> Files, string Stem)>();
        foreach (var item in _library.Items())
        {
            // A picture shows its own files only: the picture or video as taken, and a Live Photo's video beside it.
            if (item.Files.FirstOrDefault(f => f.Kind is LibraryFileKind.Photo or LibraryFileKind.Video) is not { } taken) continue;
            shown.Add((item, [taken, .. item.Files.Where(f => f.Kind == LibraryFileKind.PairedVideo).Take(1)], Path.GetFileNameWithoutExtension(taken.FileName)));
        }

        // Pictures taken under the same name are told apart by a piece of each one's own id rather than by a count, so
        // that a name never comes to stand for another picture when one arrives or goes. Each of a picture's files
        // shares its name, as the picture and video of a Live Photo must to be known for one; and a picture has its
        // name wherever it is, so that moving it never needs another.
        var named = new Dictionary<string, Shown>(PhotoPath.Comparer);
        var where = new List<(string Item, string Folder)>();
        foreach (var group in shown.GroupBy(s => s.Stem, StringComparer.OrdinalIgnoreCase))
        {
            var tags = Tags(group.Select(s => s.Item.Id).ToList());
            foreach (var (item, files, stem) in group)
            {
                var name = tags is null ? stem : $"{stem} ({tags[item.Id]})";
                var places = albumsOf[item.Id].Distinct(PhotoPath.Comparer).DefaultIfEmpty(Root);
                foreach (var place in places)
                {
                    where.Add((item.Id, place));
                    foreach (var file in files) named[Path.Combine(place, name + Path.GetExtension(file.FileName))] = new Shown(item, file, place);
                }
            }
        }
        return new Seen(named, folders, albums, where.ToLookup(w => w.Item, w => w.Folder));
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
