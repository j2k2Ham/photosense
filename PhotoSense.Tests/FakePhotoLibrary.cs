using PhotoSense.Infrastructure.Library;

namespace PhotoSense.Tests;

/// <summary>A phone's photo library as its photo framework would give it, kept in memory.</summary>
internal sealed class FakePhotoLibrary : IPhotoLibrary
{
    private readonly List<LibraryItem> _items = [];
    private readonly Dictionary<(string, LibraryFileKind), byte[]> _bytes = [];
    private DateTime _clock = new(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>How many times the library has been read through.</summary>
    public int Reads { get; private set; }

    /// <summary>Files are handed out as a stream that can only be read from the start, as some frameworks give them.</summary>
    public bool OnlyFromTheStart { get; set; }

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
