using System.Collections.Concurrent;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Infrastructure.Metadata;

/// <summary>
/// Remembers the Live Photo identifier read from a file for as long as the file stays the same. Working
/// out what belongs to each of a thousand files asks for the same identifiers again and again.
/// </summary>
public sealed class LivePhotoIdCache
{
    private readonly IPhotoFileSystem _files;
    private readonly Func<string, string?> _read;
    private readonly ConcurrentDictionary<string, (long Size, DateTime ModifiedUtc, string? Id)> _known = new();

    public LivePhotoIdCache(IPhotoFileSystem files) : this(files, path => LivePhotoLink.ReadId(files, path)) { }

    /// <param name="read">Reads the identifier a picture or video carries, if any.</param>
    public LivePhotoIdCache(IPhotoFileSystem files, Func<string, string?> read)
    {
        _files = files;
        _read = read;
    }

    public string? Read(string path)
    {
        // A file that is not there has nothing to remember; the reader says what it makes of that.
        if (_files.Describe(path) is not { } now) return _read(path);
        var key = PhotoPath.Key(path);
        if (_known.TryGetValue(key, out var known) && known.Size == now.Length && known.ModifiedUtc == now.ModifiedUtc) return known.Id;
        var id = _read(path);
        _known[key] = (now.Length, now.ModifiedUtc, id);
        return id;
    }
}
