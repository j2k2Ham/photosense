using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;
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

    /// <summary>The videos compressed, with what each was made into.</summary>
    public List<(string Id, VideoTarget Target)> Compressed { get; } = [];

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

    /// <inheritdoc />
    public IReadOnlyList<MediaEntry> Entries() => [.. _entries];

    /// <inheritdoc />
    public Stream Open(string id)
    {
        var bytes = _bytes[id];
        return OnlyFromTheStart ? new OnlyForward(bytes) : new MemoryStream(bytes, writable: false);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    /// <remarks>The copy takes a thousandth of the bits a second it is written at, in bytes, and goes beside the original.</remarks>
    public Task<long> CompressAsync(string id, VideoTarget target, IProgress<double> progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var original = _entries.Single(e => e.Id == id);
        Compressed.Add((id, target));
        var bytes = new byte[target.VideoBitsPerSecond / 1000];
        Add(id + "-smaller", original.Folder, Path.GetFileNameWithoutExtension(original.Name) + " (smaller).mp4", bytes, video: true);
        progress.Report(1);
        return Task.FromResult(bytes.LongLength);
    }

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
        var at = _entries.FindIndex(e => e.Id == id);
        _entries[at] = _entries[at] with { Favorite = favorite };
    }

    /// <inheritdoc />
    /// <remarks>A HEIC picture's copy is a JPEG, as on Android; the copy is kept nowhere.</remarks>
    public Task<SharedCopy> CopyForSharingAsync(string id, bool removeLocation, bool removeCamera, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var entry = _entries.Single(e => e.Id == id);
        var heic = entry.Name.EndsWith(".heic", StringComparison.OrdinalIgnoreCase);
        var name = heic ? Path.ChangeExtension(entry.Name, ".jpg") : entry.Name;
        return Task.FromResult(new SharedCopy(Path.Combine(Path.GetTempPath(), "shared", name), entry.IsVideo ? "video/mp4" : "image/jpeg"));
    }

    private sealed class OnlyForward(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public override bool CanSeek => false;
    }
}
