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

    /// <summary>When set, the person says no to the next change, and this is why; nothing changes.</summary>
    public string? Refuse { get; set; }

    /// <summary>Adds a picture or video kept as the files given, taken a minute after the one before.</summary>
    public LibraryItem Add(string id, params (LibraryFileKind Kind, string Name, byte[] Bytes)[] files)
    {
        _clock = _clock.AddMinutes(1);
        var item = new LibraryItem(id, _clock, _clock.AddSeconds(30), [.. files.Select(f => new LibraryFile(f.Kind, f.Name, f.Bytes.LongLength))]);
        foreach (var (kind, _, bytes) in files) _bytes[(id, kind)] = bytes;
        _items.Add(item);
        return item;
    }

    public void Remove(string id) => _items.RemoveAll(i => i.Id == id);

    /// <summary>An album PhotoSense made, as if it had made it before.</summary>
    public void Album(IReadOnlyList<string> path, params string[] items) => _albums.Add(([.. path], [.. items]));

    /// <summary>What is in each of PhotoSense's albums, by path.</summary>
    public Dictionary<string, string[]> InAlbums() => _albums.ToDictionary(a => string.Join('/', a.Path), a => a.Items.Order(StringComparer.Ordinal).ToArray());

    public IReadOnlyList<LibraryItem> Items()
    {
        Reads++;
        return [.. _items];
    }

    public Stream Open(string id, LibraryFileKind kind)
    {
        if (!_bytes.TryGetValue((id, kind), out var bytes)) throw new FileNotFoundException("Not in the library.", id);
        return OnlyFromTheStart ? new FromTheStart(bytes) : new MemoryStream(bytes, writable: false);
    }

    public IReadOnlyList<LibraryAlbum> Albums() => [.. _albums.Select(a => new LibraryAlbum([.. a.Path], [.. a.Items]))];

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
        // A folder holds no pictures of its own here: it is there while an album in it is.
        foreach (var path in change.Drop) _albums.RemoveAll(a => a.Path.SequenceEqual(path) && a.Items.Count == 0);
        foreach (var id in change.Delete)
        {
            _items.RemoveAll(i => i.Id == id);
            foreach (var album in _albums) album.Items.Remove(id);
            RecentlyDeleted.Add(id);
        }
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
