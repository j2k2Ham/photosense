namespace PhotoSense.Infrastructure.Library;

/// <summary>What one of the files a picture or video is kept as holds.</summary>
public enum LibraryFileKind
{
    /// <summary>The picture as it was taken, before any edit.</summary>
    Photo,

    /// <summary>A video as it was taken.</summary>
    Video,

    /// <summary>The moving part of a Live Photo.</summary>
    PairedVideo,
}

/// <summary>One file a picture or video is kept as, under the name it was taken with.</summary>
public sealed record LibraryFile(LibraryFileKind Kind, string FileName, long Length);

/// <summary>A picture or video in a phone's photo library, and the files it is kept as.</summary>
public sealed record LibraryItem(string Id, DateTime CreatedUtc, DateTime ModifiedUtc, IReadOnlyList<LibraryFile> Files);

/// <summary>An album PhotoSense made: the names of the folders it is in, outermost first, then its own; and what is in it.</summary>
public sealed record LibraryAlbum(IReadOnlyList<string> Path, IReadOnlyList<string> ItemIds);

/// <summary>
/// Changes to a photo library, made together. An album is named by its path: the folders it is in, then its own name.
/// One that is not there yet is made, with the folders it names, when something is put in it.
/// </summary>
public sealed class LibraryChange
{
    /// <summary>Pictures and videos to put in an album of PhotoSense's.</summary>
    public List<(string Item, IReadOnlyList<string> Album)> Add { get; } = [];

    /// <summary>Pictures and videos to take out of an album of PhotoSense's. They stay in the library.</summary>
    public List<(string Item, IReadOnlyList<string> Album)> Take { get; } = [];

    /// <summary>Albums or folders of PhotoSense's to take away, each only when nothing is in it.</summary>
    public List<IReadOnlyList<string>> Drop { get; } = [];

    /// <summary>Pictures and videos to delete. A phone keeps what is deleted in its own bin for a while, Recently Deleted on an iPhone.</summary>
    public List<string> Delete { get; } = [];

    public bool IsEmpty => Add.Count == 0 && Take.Count == 0 && Drop.Count == 0 && Delete.Count == 0;
}

/// <summary>
/// A phone's photo library as the phone's own photo framework gives it. Only this differs from one kind of phone
/// to another; how its pictures are shown to the rest of PhotoSense is decided once, in <see cref="PhotoLibraryFileSystem"/>.
/// </summary>
public interface IPhotoLibrary
{
    /// <summary>Every picture and video the app has been allowed to see, oldest first.</summary>
    IReadOnlyList<LibraryItem> Items();

    /// <summary>One file of a picture or video as it was taken, fetched from the cloud first when it is kept there.</summary>
    /// <exception cref="FileNotFoundException">The picture or video is no longer in the library.</exception>
    Stream Open(string id, LibraryFileKind kind);

    /// <summary>The albums PhotoSense has made, empty ones too. Albums of the person's own are never among them.</summary>
    IReadOnlyList<LibraryAlbum> Albums();

    /// <summary>
    /// Makes every change given, at once: all of them, or none. The phone may ask the person first, as it does before
    /// it deletes anything, and asks once for all of them.
    /// </summary>
    /// <exception cref="IOException">Nothing was changed: the person said no, or the library could not make one of the changes.</exception>
    void Change(LibraryChange change);
}
