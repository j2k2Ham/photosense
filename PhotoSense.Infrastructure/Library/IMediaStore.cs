namespace PhotoSense.Infrastructure.Library;

/// <summary>A picture or video as an Android phone's media store keeps it.</summary>
/// <param name="Id">The store's own id for it.</param>
/// <param name="Folder">Where it is on the phone's storage, as the store says it: <c>DCIM/Camera/</c>.</param>
/// <param name="Name">Its file's name there.</param>
public sealed record MediaEntry(string Id, string Folder, string Name, long Length, DateTime ModifiedUtc, bool IsVideo);

/// <summary>Changes to an Android phone's pictures and videos, made together.</summary>
public sealed class MediaChange
{
    /// <summary>Pictures and videos to put in another folder, or under another name: the folder as the store says it.</summary>
    public List<(string Id, string Folder, string Name)> Move { get; } = [];

    /// <summary>Pictures and videos for the phone's bin, which keeps them for 30 days.</summary>
    public List<string> Trash { get; } = [];

    public bool IsEmpty => Move.Count == 0 && Trash.Count == 0;
}

/// <summary>
/// The pictures and videos of an Android phone, as its media store keeps them (<c>MediaStore</c>), as far as the
/// person let PhotoSense see them. Only this talks to Android; what is done with the photos is decided in
/// <see cref="MediaFileSystem"/>.
/// </summary>
public interface IMediaStore
{
    IReadOnlyList<MediaEntry> Entries();

    /// <summary>The file as it is, place it was taken included, to read from any point.</summary>
    Stream Open(string id);

    /// <summary>
    /// Makes the changes, after the phone asks the person once for the moves and once for the bin: all of them, or
    /// none when the person says no or one cannot be made.
    /// </summary>
    /// <exception cref="IOException">Nothing was changed, and why.</exception>
    void Change(MediaChange change);
}
