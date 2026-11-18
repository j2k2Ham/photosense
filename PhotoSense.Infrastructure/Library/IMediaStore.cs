using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Infrastructure.Library;

/// <summary>A picture or video as an Android phone's media store keeps it.</summary>
/// <param name="Id">The store's own id for it.</param>
/// <param name="Folder">Where it is on the phone's storage, as the store says it: <c>DCIM/Camera/</c>.</param>
/// <param name="Name">Its file's name there.</param>
/// <param name="Length">Its size.</param>
/// <param name="ModifiedUtc">When it last changed.</param>
/// <param name="IsVideo">It is a video rather than a picture.</param>
/// <param name="Favorite">It is a favorite (<c>IS_FAVORITE</c>).</param>
public sealed record MediaEntry(string Id, string Folder, string Name, long Length, DateTime ModifiedUtc, bool IsVideo, bool Favorite = false);

/// <summary>Changes to an Android phone's pictures and videos, made together.</summary>
public sealed class MediaChange
{
    /// <summary>Pictures and videos to put in another folder, or under another name: the folder as the store says it.</summary>
    public List<(string Id, string Folder, string Name)> Move { get; } = [];

    /// <summary>Pictures and videos for the phone's bin, which keeps them for 30 days.</summary>
    public List<string> Trash { get; } = [];

    /// <summary>There is nothing to change.</summary>
    public bool IsEmpty => Move.Count == 0 && Trash.Count == 0;
}

/// <summary>
/// The pictures and videos of an Android phone, as its media store keeps them (<c>MediaStore</c>), as far as the
/// person let PhotoSense see them. Only this talks to Android; what is done with the photos is decided in
/// <see cref="MediaFileSystem"/>.
/// </summary>
public interface IMediaStore
{
    /// <summary>Every picture and video the app has been allowed to see.</summary>
    IReadOnlyList<MediaEntry> Entries();

    /// <summary>The file as it is, place it was taken included, to read from any point.</summary>
    Stream Open(string id);

    /// <summary>
    /// Makes the changes, after the phone asks the person once for the moves and once for the bin: all of them, or
    /// none when the person says no or one cannot be made.
    /// </summary>
    /// <exception cref="IOException">Nothing was changed, and why.</exception>
    void Change(MediaChange change);

    /// <summary>
    /// Writes a smaller copy of the video <paramref name="id"/>, made into <paramref name="target"/> as HEVC, into the
    /// same folder with the original's date; answers how many bytes the copy takes. The original is left as it is.
    /// </summary>
    /// <exception cref="IOException">The video could not be read or written; nothing is kept.</exception>
    Task<long> CompressAsync(string id, VideoTarget target, IProgress<double> progress, CancellationToken ct);

    /// <summary>Marks a picture or video a favorite, or takes the mark away, after Android asks the person.</summary>
    /// <exception cref="IOException">The person said no, or the phone could not change it.</exception>
    void SetFavorite(string id, bool favorite);

    /// <summary>
    /// A copy of a picture or video for sharing, in the app's cache, without its place and camera details as asked: a
    /// HEIC picture becomes a JPEG, as Android cannot rewrite HEIC's details; a video is re-written, not re-encoded.
    /// </summary>
    /// <exception cref="IOException">It could not be copied.</exception>
    Task<SharedCopy> CopyForSharingAsync(string id, bool removeLocation, bool removeCamera, CancellationToken ct);
}
