using PhotoSense.Infrastructure.Library;

namespace PhotoSense.Tests;

/// <summary>An Android phone's media store as Android would give it, kept in memory.</summary>
internal sealed class FakeMediaStore : IMediaStore
{
    private readonly List<MediaEntry> _entries = [];
    private readonly Dictionary<string, byte[]> _bytes = [];
    private DateTime _clock = new(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Files are handed out as a stream that can only be read from the start.</summary>
    public bool OnlyFromTheStart { get; set; }

    /// <summary>The changes asked for, each of which the phone would ask the person about.</summary>
    public List<MediaChange> Changes { get; } = [];

    /// <summary>What went to the phone's bin.</summary>
    public List<string> Trashed { get; } = [];

    /// <summary>When set, the person says no to the next change, and this is why; nothing changes.</summary>
    public string? Refuse { get; set; }

    /// <summary>Adds a photo in a folder as the store says it ("DCIM/Camera/"), taken a minute after the one before.</summary>
    public MediaEntry Add(string id, string folder, string name, byte[] bytes, bool video = false)
    {
        _clock = _clock.AddMinutes(1);
        var entry = new MediaEntry(id, folder, name, bytes.LongLength, _clock, video);
        _bytes[id] = bytes;
        _entries.Add(entry);
        return entry;
    }

    /// <summary>Where each photo is now, as the store says it: "DCIM/Camera/IMG_1.JPG".</summary>
    public Dictionary<string, string> Places() => _entries.ToDictionary(e => e.Id, e => e.Folder + e.Name);

    public IReadOnlyList<MediaEntry> Entries() => [.. _entries];

    public Stream Open(string id)
    {
        var bytes = _bytes[id];
        return OnlyFromTheStart ? new OnlyForward(bytes) : new MemoryStream(bytes, writable: false);
    }

    public void Change(MediaChange change)
    {
        Changes.Add(change);
        if (Refuse is { } why)
        {
            Refuse = null;
            throw new IOException(why);
        }
        foreach (var (id, folder, name) in change.Move)
        {
            var at = _entries.FindIndex(e => e.Id == id);
            _entries[at] = _entries[at] with { Folder = folder, Name = name };
        }
        foreach (var id in change.Trash)
        {
            _entries.RemoveAll(e => e.Id == id);
            Trashed.Add(id);
        }
    }

    private sealed class OnlyForward(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public override bool CanSeek => false;
    }
}
