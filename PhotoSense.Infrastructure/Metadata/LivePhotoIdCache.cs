using System.Collections.Concurrent;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Infrastructure.Metadata;

/// <summary>
/// Remembers the Live Photo identifier read from a file for as long as the file stays the same. Working
/// out what belongs to each of a thousand files asks for the same identifiers again and again.
/// </summary>
public sealed class LivePhotoIdCache
{
    private readonly Func<string, string?> _read;
    private readonly ConcurrentDictionary<string, (long Size, DateTime ModifiedUtc, string? Id)> _known = new();

    public LivePhotoIdCache() : this(LivePhotoLink.ReadId) { }

    /// <param name="read">Reads the identifier a picture or video carries, if any.</param>
    public LivePhotoIdCache(Func<string, string?> read) => _read = read;

    public string? Read(string path)
    {
        var info = new FileInfo(path);
        // A file that is not there has nothing to remember; the reader says what it makes of that.
        if (!info.Exists) return _read(path);
        var key = PhotoPath.Key(path);
        if (_known.TryGetValue(key, out var known) && known.Size == info.Length && known.ModifiedUtc == info.LastWriteTimeUtc) return known.Id;
        var id = _read(path);
        _known[key] = (info.Length, info.LastWriteTimeUtc, id);
        return id;
    }
}
