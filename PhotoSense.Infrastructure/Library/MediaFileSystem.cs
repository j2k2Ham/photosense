using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Infrastructure.Files;

namespace PhotoSense.Infrastructure.Library;

/// <summary>
/// An Android phone's pictures and videos shown as the folders they are in. The phone's storage is one folder,
/// holding DCIM, Pictures, Movies and the rest as the phone has them; a picture is a file in its own folder, under
/// its own name.
/// <para>
/// Android keeps pictures and videos only inside DCIM, Pictures or Movies, so a folder of them is made inside one of
/// those. What is removed from a folder waits in <c>_PhotoSense_Removed</c> inside it, as on a computer; for the
/// phone's storage as a whole, which can hold no folder of its own, in <c>Pictures/PhotoSense Removed</c>. Erasing
/// sends to the phone's own bin. Nothing is ever copied onto the phone: photos are copied to a computer.
/// </para>
/// </summary>
public sealed class MediaFileSystem : IPhotoFileSystem, IPhotoChangesTogether
{
    /// <summary>Where what is removed from the phone's storage as a whole waits, as Android shows it: inside Pictures.</summary>
    public const string RemovedHere = "PhotoSense Removed";

    /// <summary>Why a copy is refused.</summary>
    public const string NoCopies = "Photos are not copied on the phone: they can be moved here, and copied to a computer.";

    /// <summary>The folders of the phone's storage that Android keeps pictures and videos in.</summary>
    public static readonly IReadOnlyList<string> MediaFolders = ["DCIM", "Pictures", "Movies"];

    private const string Pictures = "Pictures";
    private readonly IMediaStore _store;
    private Seen? _seen;
    // Folders made for photos still to come: on the phone a folder is there while something is in it.
    private readonly HashSet<string> _made = new(PhotoPath.Comparer);

    private sealed record Seen(Dictionary<string, (MediaEntry Entry, string Folder)> Files, HashSet<string> Folders);

    /// <param name="root">The path the phone's storage is shown at, as a folder.</param>
    public MediaFileSystem(IMediaStore store, string root)
    {
        _store = store;
        Root = Path.TrimEndingDirectorySeparator(root);
    }

    /// <summary>The path of the folder the phone's storage is shown as.</summary>
    public string Root { get; }

    public bool FileExists(string path) => Find(path) is not null;

    public bool FolderExists(string path)
    {
        var folder = Path.TrimEndingDirectorySeparator(path);
        return Same(folder, Root) || Look().Folders.Contains(folder) || _made.Contains(folder);
    }

    public StoredFile? Describe(string path) => Find(path) is { } found ? new StoredFile(found.Length, found.ModifiedUtc) : null;

    // The phone is read again each time its storage is listed, so that a scan sees what is on it now.
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
        var found = Find(path) ?? throw new FileNotFoundException("The photo is no longer on the phone.", path);
        var stream = _store.Open(found.Id);
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

    /// <summary>Photos arrive on a computer from a phone, not on the phone.</summary>
    public Stream CreateNew(string path) => throw new UnauthorizedAccessException("PhotoSense does not put new files on a phone.");

    public void MakeFolder(string path)
    {
        var folder = Inside(path);
        if (Same(folder, Root)) return;
        StoreFolder(folder);
        for (var at = folder; !Same(at, Root); at = Path.GetDirectoryName(at)!) _made.Add(at);
    }

    public void RemoveEmptyFolder(string path) => RemoveEmptyFolders([path]);

    public void DeleteCopy(string path) => throw new UnauthorizedAccessException(NoCopies);

    public void Erase(string path) => EraseAll([path]);

    public void MoveAll(IReadOnlyList<FileMove> moves)
    {
        var seen = Look();
        var change = new MediaChange();
        var going = new HashSet<string>();
        foreach (var (from, to) in moves)
        {
            var target = Inside(to);
            var folder = StoreFolder(Path.GetDirectoryName(target)!);
            if (!seen.Files.TryGetValue(from, out var found))
            {
                // Moved already, as by a change that was cut short and is now being put right.
                if (seen.Files.ContainsKey(target)) continue;
                throw new FileNotFoundException($"{Path.GetFileName(from)} is no longer on the phone.", from);
            }
            if (seen.Files.ContainsKey(target)) throw new IOException($"{Path.GetFileName(to)} is there already, and is not replaced.");
            if (!going.Add(found.Entry.Id)) throw new IOException($"{Path.GetFileName(from)} cannot go to two places at once.");
            change.Move.Add((found.Entry.Id, folder, Path.GetFileName(target)));
        }
        Apply(change);
    }

    public void EraseAll(IReadOnlyList<string> paths)
    {
        var seen = Look();
        var change = new MediaChange();
        // A photo gone since its folder was read has nothing left to erase.
        change.Trash.AddRange(paths.Select(p => seen.Files.TryGetValue(p, out var found) ? found.Entry.Id : null).OfType<string>().Distinct());
        Apply(change);
    }

    /// <summary>Forgets the folders made for photos that never came; a folder Android has goes when it is empty, as Android has it.</summary>
    public void RemoveEmptyFolders(IReadOnlyList<string> folders)
    {
        var seen = Look();
        // Deepest first: a folder whose folders were forgotten is empty too.
        foreach (var folder in folders.Select(Path.TrimEndingDirectorySeparator).OrderByDescending(f => f.Length))
            if (!seen.Files.Values.Any(f => Same(f.Folder, folder)) && FoldersIn(folder).Count == 0) _made.Remove(folder);
    }

    private void Apply(MediaChange change)
    {
        if (change.IsEmpty) return;
        try { _store.Change(change); }
        // What the phone holds now is read afresh, whether or not the change was made.
        finally { _seen = null; }
    }

    private Seen Look() => _seen ??= Read();

    private MediaEntry? Find(string path) => Look().Files.TryGetValue(path, out var found) ? found.Entry : null;

    private static bool Same(string a, string b) => PhotoPath.Comparer.Equals(a, b);

    // A path on the phone, or why it may not be used.
    private string Inside(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Same(full, Root) && !PhotoPath.Key(full).StartsWith(PhotoPath.Key(Root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new UnauthorizedAccessException($"{path} is not on the phone.");
        return full;
    }

    // Where a folder shown here is as the store says it, or why photos cannot be put there.
    private string StoreFolder(string folder)
    {
        string[] parts = [.. Path.GetRelativePath(Root, folder).Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries).Where(p => p != ".")];
        if (parts.Length == 0) throw new UnauthorizedAccessException("On an Android phone, photos are kept inside DCIM, Pictures or Movies, not in the phone's storage itself.");
        if (parts[0] == PhotoStorageOptions.RemovedFolderName) parts = [Pictures, RemovedHere, .. parts[1..]];
        else if (parts is [Pictures, RemovedHere, ..])
            throw new UnauthorizedAccessException($"PhotoSense keeps {Pictures}/{RemovedHere} for what it removes from the phone, so photos are not put there.");
        else if (!MediaFolders.Contains(parts[0], StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"On an Android phone, photos are kept inside DCIM, Pictures or Movies, so {Path.GetFileName(folder)} has to be made inside one of those.");
        return string.Concat(parts.Select(p => p + "/"));
    }

    // Where a folder of the store is shown: the phone's storage, then its folders; what was removed from the phone as a whole, in the removed folder of the storage.
    private string Shown(string storeFolder)
    {
        var parts = storeFolder.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && parts[0].Equals(Pictures, StringComparison.OrdinalIgnoreCase) && parts[1] == RemovedHere)
            parts = [PhotoStorageOptions.RemovedFolderName, .. parts[2..]];
        return Path.Combine([Root, .. parts]);
    }

    private Seen Read()
    {
        var files = new Dictionary<string, (MediaEntry, string)>(PhotoPath.Comparer);
        var folders = new HashSet<string>(PhotoPath.Comparer);
        foreach (var entry in _store.Entries())
        {
            var folder = Shown(entry.Folder);
            files.TryAdd(Path.Combine(folder, entry.Name), (entry, folder));
            for (var at = folder; !Same(at, Root); at = Path.GetDirectoryName(at)!) folders.Add(at);
        }
        return new Seen(files, folders);
    }
}
