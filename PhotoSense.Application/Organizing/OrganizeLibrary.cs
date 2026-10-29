using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using PhotoSense.Application.Scanning;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Application.Organizing;

/// <summary>A picture or video as Organize knows it: where it is, and what it says about itself.</summary>
public sealed record LibraryFile(string Id, string Path, long SizeBytes, DateTime ModifiedUtc, MediaDetails Details, bool FromScan, string? ContentHash)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path)!;
    public bool IsVideo => MediaFiles.IsVideo(Path);
    /// <summary>Nothing in the file says when it was taken, so the date is the file's own.</summary>
    public bool DateFromFile => Details.TakenOn is null;
    /// <summary>When it was taken, as the camera's clock had it; failing that, when the file was last changed.</summary>
    public DateTime Date => Details.TakenOn ?? ModifiedUtc.ToLocalTime();
}

/// <summary>How far the reading of a folder has got, with some of the files it has found.</summary>
/// <param name="Reading">Tells one reading of the folder from the next.</param>
/// <param name="Total">Pictures and videos in the folder.</param>
/// <param name="Done">How many of them have been gone through.</param>
/// <param name="From">How many found files come before the ones handed over here, in the order they were found.</param>
/// <param name="Found">Files found so far, from that place on.</param>
/// <param name="More">More files have been found than were handed over.</param>
/// <param name="Notes">Whatever those who present the reading need to keep from one asking to the next; it goes when the reading does.</param>
public sealed record ListingProgress(Guid Reading, int Total, int Done, int From, IReadOnlyList<LibraryFile> Found, bool More, ConcurrentDictionary<string, object> Notes);

/// <summary>
/// The files of any folder, with when and where each was taken, without a scan. What a scan already
/// recorded about a file is used as it is; anything else is read from the file once and kept, here and
/// in a store that outlasts this run, for as long as the file stays the same. Each file is known to the
/// UI by an id that only this run of the service can turn back into a path, so no request names a path
/// of its own choosing.
/// </summary>
public sealed class OrganizeLibrary
{
    private readonly IPhotoRepository _repo;
    private readonly IMediaDetailsReader _reader;
    private readonly IImageHashingService _hashing;
    private readonly IMediaDetailsStore _details;
    private readonly byte[] _idKey;
    private readonly ConcurrentDictionary<string, LibraryFile> _known = new();
    private readonly ConcurrentDictionary<string, string> _paths = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<LibraryFile>>>> _reading = new();
    private readonly ConcurrentDictionary<string, Reading> _progress = new();

    // How many files of a folder are read at the same moment. A disk serves only so many readers, and every
    // one of them holds a thread while it waits: with no limit, the service had none left to answer anything
    // else with, and the pictures of the files already found took half a minute to come.
    private const int ReadersAtOnce = 4;

    // One reading of a folder as it goes: how many files the folder holds, and the files found so far in the order they were found.
    private sealed class Reading(int total)
    {
        public readonly Guid Id = Guid.NewGuid();
        public readonly int Total = total;
        public int Done;
        public readonly List<LibraryFile> Found = [];
        public readonly ConcurrentDictionary<string, object> Notes = new();
    }

    /// <param name="idKey">What file ids are derived with; a random one for each run of the service when not given.</param>
    public OrganizeLibrary(IPhotoRepository repo, IMediaDetailsReader reader, IImageHashingService hashing, IMediaDetailsStore details, byte[]? idKey = null)
    {
        _repo = repo;
        _reader = reader;
        _hashing = hashing;
        _details = details;
        _idKey = idKey ?? RandomNumberGenerator.GetBytes(32);
    }

    /// <summary>Every picture and video in a folder and the folders inside it, the latest first.</summary>
    /// <exception cref="DirectoryNotFoundException">There is no such folder.</exception>
    public async Task<IReadOnlyList<LibraryFile>> ListAsync(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) throw new DirectoryNotFoundException($"Folder not found: {root}");
        var folder = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(root));
        var key = PhotoPath.Key(folder);
        // A folder asked for again while it is still being read is read once: everyone asking gets the one answer.
        var reading = _reading.GetOrAdd(key, _ => new Lazy<Task<IReadOnlyList<LibraryFile>>>(() => ReadAsync(folder, key)));
        try { return await reading.Value; }
        finally { _reading.TryRemove(new KeyValuePair<string, Lazy<Task<IReadOnlyList<LibraryFile>>>>(key, reading)); }
    }

    /// <summary>How far the reading of a folder has got, or null when it is not being read.</summary>
    /// <param name="from">How many of the files found so far the asker already has.</param>
    /// <param name="atMost">The most files to hand over at once.</param>
    public ListingProgress? ProgressOf(string root, int from = 0, int atMost = int.MaxValue)
    {
        if (!_progress.TryGetValue(PhotoPath.Key(System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(root))), out var reading)) return null;
        lock (reading.Found)
        {
            var first = Math.Clamp(from, 0, reading.Found.Count);
            var count = Math.Clamp(atMost, 0, reading.Found.Count - first);
            return new ListingProgress(reading.Id, reading.Total, Volatile.Read(ref reading.Done), first, reading.Found.GetRange(first, count), first + count < reading.Found.Count, reading.Notes);
        }
    }

    private async Task<IReadOnlyList<LibraryFile>> ReadAsync(string folder, string key)
    {
        // The rest runs off the caller's thread, so that the reading is under way, and can be asked about, at once.
        await Task.Yield();
        var paths = PhotoFileEnumerator.Enumerate(folder, recursive: true).ToList();
        var reading = _progress[key] = new Reading(paths.Count);
        try
        {
            // What the scan recorded and what earlier listings read are fetched in one pass each, and only
            // when some file is not already known: a question for every file takes far longer.
            var scanned = new Dictionary<string, Domain.Entities.Photo>();
            var kept = new Dictionary<string, StoredMediaDetails>();
            if (paths.Exists(path => !_known.ContainsKey(PhotoPath.Key(path))))
            {
                foreach (var photo in await _repo.GetAllAsync()) scanned[PhotoPath.Key(photo.SourcePath)] = photo;
                foreach (var details in _details.Under(folder)) kept[PhotoPath.Key(details.Path)] = details;
            }

            var read = new ConcurrentBag<StoredMediaDetails>();
            Parallel.ForEach(paths, new ParallelOptions { MaxDegreeOfParallelism = ReadersAtOnce }, path =>
            {
                var info = new FileInfo(path);
                var at = PhotoPath.Key(path);
                if (info.Exists)
                {
                    var file = Known(info, at) ?? Learn(info, at, scanned.GetValueOrDefault(at), kept.GetValueOrDefault(at), read.Add);
                    // Kept in the order found, so that whoever is watching can be handed the new ones each time they ask.
                    lock (reading.Found) reading.Found.Add(file);
                }
                Interlocked.Increment(ref reading.Done);
            });
            _details.Save(read);
            return reading.Found.OrderByDescending(f => f.Date).ThenBy(f => f.Path, StringComparer.Ordinal).ToList();
        }
        finally { _progress.TryRemove(key, out _); }
    }

    /// <summary>The file an id was given out for, as it is now; null for an id this run never gave out or a file that has gone.</summary>
    public async Task<LibraryFile?> FindAsync(string id, CancellationToken ct = default)
        => PathOf(id) is { } path ? await DescribeAsync(path, ct) : null;

    /// <summary>
    /// The file an id was given out for, as it was when last looked at, without going to the disk; null when
    /// nothing is remembered of it. For working out a plan over thousands of files: whether each is still
    /// there is settled when the files are moved.
    /// </summary>
    public LibraryFile? Recall(string id) => PathOf(id) is { } path ? _known.GetValueOrDefault(PhotoPath.Key(path)) : null;

    /// <summary>The path an id was given out for, whether or not the file is still there.</summary>
    public string? PathOf(string id) => _paths.GetValueOrDefault(id);

    /// <summary>What is known about the file at a path, or null when there is none.</summary>
    public async Task<LibraryFile?> DescribeAsync(string path, CancellationToken ct = default)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return null;
        var key = PhotoPath.Key(path);
        if (Known(info, key) is { } known) return known;
        var read = new List<StoredMediaDetails>();
        var file = Learn(info, key, await _repo.GetByPathAsync(path, ct), _details.At(path), read.Add);
        _details.Save(read);
        return file;
    }

    private LibraryFile? Known(FileInfo info, string key)
        => _known.TryGetValue(key, out var known) && known.SizeBytes == info.Length && known.ModifiedUtc == info.LastWriteTimeUtc ? known : null;

    // What is on record about a file holds for as long as the file has not changed since; otherwise the file is read.
    private LibraryFile Learn(FileInfo info, string key, Domain.Entities.Photo? scanned, StoredMediaDetails? kept, Action<StoredMediaDetails> onRead)
    {
        var id = IdOf(key);
        _paths[id] = info.FullName;
        LibraryFile file;
        if (scanned is not null && scanned.FileSizeBytes == info.Length && scanned.FileModifiedUtc == info.LastWriteTimeUtc)
            file = new LibraryFile(id, info.FullName, info.Length, info.LastWriteTimeUtc,
                new MediaDetails(scanned.TakenOn, scanned.Width, scanned.Height, scanned.DurationSeconds, scanned.Latitude, scanned.Longitude, scanned.LivePhotoId), FromScan: true, scanned.ContentHash);
        else if (kept is not null && kept.SizeBytes == info.Length && kept.ModifiedUtc == info.LastWriteTimeUtc)
            file = new LibraryFile(id, info.FullName, info.Length, info.LastWriteTimeUtc, kept.Details, FromScan: false, ContentHash: null);
        else
        {
            file = new LibraryFile(id, info.FullName, info.Length, info.LastWriteTimeUtc, _reader.Read(info.FullName), FromScan: false, ContentHash: null);
            onRead(new StoredMediaDetails(info.FullName, info.Length, info.LastWriteTimeUtc, file.Details));
        }
        return _known[key] = file;
    }

    /// <summary>
    /// Reads a file's Live Photo identifier from what the library already knows of the file as it now is,
    /// and asks <paramref name="otherwise"/> only about a file it does not know. Finding what belongs to
    /// each of thousands of pictures would otherwise open every one of them, and its video, again.
    /// </summary>
    public Func<string, string?> LivePhotoIds(Func<string, string?> otherwise) => path =>
    {
        var info = new FileInfo(path);
        return info.Exists && Known(info, PhotoPath.Key(path)) is { } known ? known.Details.LivePhotoId : otherwise(path);
    };

    /// <summary>The hash of the file's bytes, worked out once for as long as the file stays the same.</summary>
    public async Task<string> HashAsync(LibraryFile file, CancellationToken ct = default)
    {
        if (file.ContentHash is { } hash) return hash;
        // The file as it was handed in may be from before its hash was worked out.
        var key = PhotoPath.Key(file.Path);
        if (_known.TryGetValue(key, out var known) && known.ContentHash is { } worked && known.SizeBytes == file.SizeBytes && known.ModifiedUtc == file.ModifiedUtc) return worked;
        await using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
        hash = await _hashing.ComputeHashAsync(stream, ct);
        _known[key] = file with { ContentHash = hash };
        return hash;
    }

    /// <summary>Carries what is known about a file over to the path it was moved or copied to; neither changes the file.</summary>
    public void Arrived(string from, string to, bool copied)
    {
        var id = IdOf(PhotoPath.Key(to));
        _paths[id] = to;
        if (!_known.TryGetValue(PhotoPath.Key(from), out var known)) return;
        if (!copied) _known.TryRemove(PhotoPath.Key(from), out _);
        var arrived = new FileInfo(to);
        _known[PhotoPath.Key(to)] = known with { Id = id, Path = to, ModifiedUtc = arrived.LastWriteTimeUtc };
        // Kept for the next run as well, so that a file is not read again merely for having been moved.
        _details.Save([new StoredMediaDetails(to, known.SizeBytes, arrived.LastWriteTimeUtc, known.Details)]);
    }

    private string IdOf(string pathKey) => Convert.ToHexString(HMACSHA256.HashData(_idKey, Encoding.UTF8.GetBytes(pathKey)), 0, 12).ToLowerInvariant();
}

/// <summary>Looks up the place of a position once: pictures taken together share theirs to the last few metres.</summary>
public sealed class PlaceLookup
{
    private readonly IPlaceNameResolver _resolver;
    private readonly ConcurrentDictionary<(double, double), PlaceMatch?> _found = new();

    public PlaceLookup(IPlaceNameResolver resolver) => _resolver = resolver;

    public PlaceMatch? At(double? latitude, double? longitude)
    {
        if (latitude is not { } lat || longitude is not { } lon) return null;
        // Four decimals of a degree is about ten metres.
        return _found.GetOrAdd((Math.Round(lat, 4), Math.Round(lon, 4)), at => _resolver.Locate(at.Item1, at.Item2));
    }

    /// <summary>The nearest town in words, as the Clean up screens name it.</summary>
    public string? Describe(double? latitude, double? longitude)
        => latitude is { } lat && longitude is { } lon ? _resolver.Describe(lat, lon) : null;
}
