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
}
