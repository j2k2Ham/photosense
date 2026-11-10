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
    private readonly Func<string, CancellationToken, Task<string?>> _read;
    private readonly ConcurrentDictionary<string, (long Size, DateTime ModifiedUtc, string? Id)> _known = new();

    /// <summary>Remembers what is read from the files of <paramref name="files"/>.</summary>
    public LivePhotoIdCache(IPhotoFileSystem files) : this(files, (path, ct) => LivePhotoLink.ReadIdAsync(files, path, ct)) { }

    /// <summary>Remembers what <paramref name="read"/> reads from the files of <paramref name="files"/>.</summary>
    /// <param name="files">Where the photos are kept.</param>
    /// <param name="read">Reads the identifier a picture or video carries, if any.</param>
    public LivePhotoIdCache(IPhotoFileSystem files, Func<string, CancellationToken, Task<string?>> read)
    {
        _files = files;
        _read = read;
    }

    /// <summary>The Live Photo identifier the file carries, or null when it has none; read again only when the file has changed.</summary>
    public async Task<string?> ReadAsync(string path, CancellationToken ct)
    {
        // A file that is not there has nothing to remember; the reader says what it makes of that.
        if (_files.Describe(path) is not { } now) return await _read(path, ct);
        var key = PhotoPath.Key(path);
        if (_known.TryGetValue(key, out var known) && known.Size == now.Length && known.ModifiedUtc == now.ModifiedUtc) return known.Id;
        var id = await _read(path, ct);
        _known[key] = (now.Length, now.ModifiedUtc, id);
        return id;
    }
}
