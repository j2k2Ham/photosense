using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Infrastructure.Library;

namespace PhotoSense.Tests;

/// <summary>A phone's photo library as its photo framework would give it, kept in memory.</summary>
internal sealed class FakePhotoLibrary : IPhotoLibrary
{
    private readonly List<LibraryItem> _items = [];
    private readonly Dictionary<(string, LibraryFileKind), byte[]> _bytes = [];
    private readonly List<(List<string> Path, List<string> Items)> _albums = [];
    private DateTime _clock = new(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>How many times the library has been read through.</summary>
    public int Reads { get; private set; }

    /// <summary>Files are handed out as a stream that can only be read from the start, as some frameworks give them.</summary>
    public bool OnlyFromTheStart { get; set; }

    /// <summary>The changes asked for, each of which the phone would ask the person about once.</summary>
    public List<LibraryChange> Changes { get; } = [];

    /// <summary>What was deleted, which the phone keeps in its own bin.</summary>
    public List<string> RecentlyDeleted { get; } = [];

    /// <summary>Runs before a file is handed out, as fetching it from the cloud would: it can take a while, or fail.</summary>
    public Func<CancellationToken, Task>? Fetching { get; set; }

    /// <summary>When set, the person says no to the next change, and this is why; nothing changes.</summary>
    public string? Refuse { get; set; }

    /// <summary>The videos compressed, with what each was made into.</summary>
    public List<(string Id, VideoTarget Target)> Compressed { get; } = [];

    /// <summary>Runs as a video is compressed, after half of it: it can take a while, or fail.</summary>
    public Func<string, CancellationToken, Task>? Compressing { get; set; }

    /// <summary>Adds a picture or video kept as the files given, taken a minute after the one before.</summary>
    public LibraryItem Add(string id, params (LibraryFileKind Kind, string Name, byte[] Bytes)[] files)
    {
        _clock = _clock.AddMinutes(1);
        var item = new LibraryItem(id, _clock, _clock.AddSeconds(30), [.. files.Select(f => new LibraryFile(f.Kind, f.Name, f.Bytes.LongLength))]);
        foreach (var (kind, _, bytes) in files) _bytes[(id, kind)] = bytes;
        _items.Add(item);
        return item;
    }

    /// <summary>Marks a picture or video as a favorite, as the person would in Photos.</summary>
    public void Favorite(string id)
    {
        var at = _items.FindIndex(i => i.Id == id);
        _items[at] = _items[at] with { Favorite = true };
    }

    /// <summary>Marks a picture as Photos would: a picture of the screen, or a shot of a burst.</summary>
    public void Mark(string id, bool screenshot = false, string? burst = null)
    {
        var at = _items.FindIndex(i => i.Id == id);
        _items[at] = _items[at] with { Screenshot = screenshot, BurstId = burst };
    }

    /// <summary>Says a picture or video's file is <paramref name="length"/> bytes long, as a large video would be, without holding as many.</summary>
    public void Sized(string id, long length)
    {
        var at = _items.FindIndex(i => i.Id == id);
        _items[at] = _items[at] with { Files = [.. _items[at].Files.Select(f => f with { Length = length })] };
    }

    /// <summary>Takes a picture or video out of the library, as deleting it in Photos would.</summary>
    public void Remove(string id) => _items.RemoveAll(i => i.Id == id);

    /// <summary>An album PhotoSense made, as if it had made it before.</summary>
    public void Album(IReadOnlyList<string> path, params string[] items) => _albums.Add(([.. path], [.. items]));

    /// <summary>What is in each of PhotoSense's albums, by path.</summary>
    public Dictionary<string, string[]> InAlbums() => _albums.ToDictionary(a => string.Join('/', a.Path), a => a.Items.Order(StringComparer.Ordinal).ToArray());

    /// <inheritdoc />
    public IReadOnlyList<LibraryItem> Items()
    {
        Reads++;
        return [.. _items];
    }

    /// <inheritdoc />
    public async Task<Stream> OpenAsync(string id, LibraryFileKind kind, CancellationToken ct)
    {
        if (!_bytes.TryGetValue((id, kind), out var bytes)) throw new FileNotFoundException("Not in the library.", id);
        if (Fetching is { } fetching) await fetching(ct);
        return OnlyFromTheStart ? new FromTheStart(bytes) : new MemoryStream(bytes, writable: false);
    }

    /// <inheritdoc />
    public IReadOnlyList<LibraryAlbum> Albums() => [.. _albums.Select(a => new LibraryAlbum([.. a.Path], [.. a.Items]))];

    /// <inheritdoc />
    public void Change(LibraryChange change)
    {
        Changes.Add(change);
        if (Refuse is { } why)
        {
            Refuse = null;
            throw new IOException(why);
        }
        foreach (var (item, path) in change.Take) Find(path)?.Items.Remove(item);
        foreach (var (item, path) in change.Add)
        {
            if (Find(path) is not { } album) _albums.Add(album = ([.. path], []));
            if (!album.Items.Contains(item)) album.Items.Add(item);
        }
        foreach (var id in change.Delete)
        {
            _items.RemoveAll(i => i.Id == id);
            foreach (var album in _albums) album.Items.Remove(id);
            RecentlyDeleted.Add(id);
        }
    }

    /// <inheritdoc />
    /// <remarks>The copy takes a thousandth of the bits a second it is written at, in bytes, and is named after the original.</remarks>
    public async Task<long> CompressAsync(string id, VideoTarget target, IProgress<double> progress, CancellationToken ct)
    {
        var original = _items.Single(i => i.Id == id);
        progress.Report(0.5);
        if (Compressing is { } compressing) await compressing(id, ct);
        ct.ThrowIfCancellationRequested();
        Compressed.Add((id, target));
        var bytes = new byte[target.VideoBitsPerSecond / 1000];
        var name = Path.GetFileNameWithoutExtension(original.Files[0].FileName) + " (smaller).MOV";
        Add($"{id}-smaller-{Compressed.Count}", (LibraryFileKind.Video, name, bytes));
        progress.Report(1);
        return bytes.LongLength;
    }

    /// <summary>What was put in the library as new items, in order: each one's id, when it was taken and where.</summary>
    public List<(string Id, DateTime? Taken, PlaceChange? Place)> Imported { get; } = [];

    /// <summary>When set, the next item put in the library fails, and this is why; nothing is added.</summary>
    public string? RefuseImport { get; set; }

    /// <inheritdoc />
    /// <remarks>Read a piece at a time, as the phone's framework writes a file from what it is given.</remarks>
    public async Task<string> ImportAsync(IReadOnlyList<(LibraryFileKind Kind, string FileName, Stream Content)> files, DateTime? taken, PlaceChange? place, CancellationToken ct)
    {
        if (RefuseImport is { } why)
        {
            RefuseImport = null;
            throw new IOException(why);
        }
        var read = new List<(LibraryFileKind, string, byte[])>();
        foreach (var (kind, name, content) in files)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, ct);
            read.Add((kind, name, copy.ToArray()));
        }
        var id = $"imported-{Imported.Count + 1}";
        Add(id, [.. read]);
        Imported.Add((id, taken, place));
        return id;
    }

    /// <summary>The bytes of one file of a picture or video, as the library keeps them.</summary>
    public byte[] Bytes(string id, LibraryFileKind kind) => _bytes[(id, kind)];

    /// <summary>Each change of dates and places asked for, in order, as the ids and what each was given.</summary>
    public List<IReadOnlyList<(string Id, DateTime? Taken, PlaceChange? Place)>> DetailsChanged { get; } = [];

    /// <inheritdoc />
    public void ChangeDetails(IReadOnlyList<(string Id, DateTime? Taken, PlaceChange? Place)> changes)
    {
        if (Refuse is { } why)
        {
            Refuse = null;
            throw new IOException(why);
        }
        DetailsChanged.Add(changes);
    }

    /// <inheritdoc />
    public void SetFavorite(string id, bool favorite)
    {
        if (Refuse is { } why)
        {
            Refuse = null;
            throw new IOException(why);
        }
        var at = _items.FindIndex(i => i.Id == id);
        _items[at] = _items[at] with { Favorite = favorite };
    }

    /// <summary>The copies made for sharing: of what, and what was left out.</summary>
    public List<(string Id, LibraryFileKind Kind, bool RemoveLocation, bool RemoveCamera)> Copied { get; } = [];

    /// <inheritdoc />
    /// <remarks>The copy is named after the file and kept nowhere: the test's share sheet only notes it.</remarks>
    public Task<SharedCopy> CopyForSharingAsync(string id, LibraryFileKind kind, bool removeLocation, bool removeCamera, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Refuse is { } why)
        {
            Refuse = null;
            throw new IOException(why);
        }
        Copied.Add((id, kind, removeLocation, removeCamera));
        var name = _items.Single(i => i.Id == id).Files.First(f => f.Kind == kind).FileName;
        return Task.FromResult(new SharedCopy(Path.Combine(Path.GetTempPath(), "shared", name), kind == LibraryFileKind.Photo ? "image/heic" : "video/quicktime"));
    }

    private (List<string> Path, List<string> Items)? Find(IReadOnlyList<string> path)
    {
        var found = _albums.FindIndex(a => a.Path.SequenceEqual(path));
        return found < 0 ? null : _albums[found];
    }

    private sealed class FromTheStart(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes, writable: false);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
