using System.Text;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Tests;

/// <summary>
/// Photos kept in memory and nowhere else, shown as folders that hold files, as a phone's photo library will
/// be. None of it is on any disk, so whatever works through it has not touched one. It behaves as a disk
/// does where PhotoSense counts on it: nothing is ever replaced, and a file goes only into a folder that is there.
/// </summary>
internal sealed class InMemoryFileSystem : IPhotoFileSystem
{
    private readonly Dictionary<string, (string Path, byte[] Bytes, DateTime ModifiedUtc)> _files = new(PhotoPath.Comparer);
    private readonly Dictionary<string, string> _folders = new(PhotoPath.Comparer);
    private readonly object _lock = new();
    private DateTime _clock = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    /// <summary>Puts a file in memory, with the folders it is in, as one arriving now.</summary>
    public string Put(string path, byte[] bytes)
    {
        lock (_lock)
        {
            MakeFolder(System.IO.Path.GetDirectoryName(path)!);
            _files[path] = (path, bytes, _clock = _clock.AddSeconds(1));
            return path;
        }
    }

    public string Put(string path, string text) => Put(path, Encoding.UTF8.GetBytes(text));

    /// <summary>Every file there is, wherever it is.</summary>
    public IReadOnlyList<string> AllFiles()
    {
        lock (_lock) return _files.Values.Select(f => f.Path).Order(StringComparer.Ordinal).ToList();
    }

    public bool FileExists(string path)
    {
        lock (_lock) return _files.ContainsKey(path);
    }

    public bool FolderExists(string path)
    {
        lock (_lock) return _folders.ContainsKey(Trim(path));
    }

    public StoredFile? Describe(string path)
    {
        lock (_lock) return _files.TryGetValue(path, out var f) ? new StoredFile(f.Bytes.LongLength, f.ModifiedUtc) : null;
    }

    public IReadOnlyList<string> FilesIn(string folder)
    {
        lock (_lock) return _files.Values.Where(f => In(f.Path, folder)).Select(f => f.Path).ToList();
    }

    public IReadOnlyList<string> FoldersIn(string folder)
    {
        lock (_lock) return _folders.Values.Where(f => In(f, folder)).ToList();
    }

    public bool IsLink(string path) => false;

    public Stream OpenRead(string path)
    {
        lock (_lock) return _files.TryGetValue(path, out var f) ? new MemoryStream(f.Bytes, writable: false) : throw new FileNotFoundException("No such file.", path);
    }

    public bool IsCopyOf(string copy, string original)
    {
        lock (_lock) return _files[original].Bytes.AsSpan().StartsWith(_files[copy].Bytes);
    }

    public void Move(string from, string to)
    {
        lock (_lock)
        {
            var file = Arriving(from, to);
            _files.Remove(from);
            _files[to] = file with { Path = to };
        }
    }

    public void Copy(string from, string to)
    {
        lock (_lock)
        {
            var file = Arriving(from, to);
            _files[to] = file with { Path = to };
        }
    }

    public void MakeFolder(string path)
    {
        lock (_lock)
            for (var at = Trim(path); !string.IsNullOrEmpty(at); at = System.IO.Path.GetDirectoryName(at))
                _folders.TryAdd(at, at);
    }

    public void RemoveEmptyFolder(string path)
    {
        lock (_lock)
            if (FilesIn(path).Count == 0 && FoldersIn(path).Count == 0) _folders.Remove(Trim(path));
    }

    public void DeleteCopy(string path)
    {
        lock (_lock) _files.Remove(path);
    }

    public void Erase(string path)
    {
        lock (_lock) _files.Remove(path);
    }

    // What a move or copy takes, once it is known that it replaces nothing and has a folder to go into.
    private (string Path, byte[] Bytes, DateTime ModifiedUtc) Arriving(string from, string to)
    {
        if (!_files.TryGetValue(from, out var file)) throw new FileNotFoundException("No such file.", from);
        if (_files.ContainsKey(to)) throw new IOException($"{System.IO.Path.GetFileName(to)} is there already.");
        if (!_folders.ContainsKey(System.IO.Path.GetDirectoryName(to)!)) throw new DirectoryNotFoundException($"No folder for {to}.");
        return file;
    }

    private static bool In(string path, string folder) => PhotoPath.Comparer.Equals(System.IO.Path.GetDirectoryName(path), Trim(folder));

    private static string Trim(string path) => System.IO.Path.TrimEndingDirectorySeparator(path);
}
