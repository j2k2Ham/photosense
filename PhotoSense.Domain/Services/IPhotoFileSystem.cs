namespace PhotoSense.Domain.Services;

/// <summary>What is known of a file without opening it: how big it is, and when it last changed.</summary>
public sealed record StoredFile(long Length, DateTime ModifiedUtc);

/// <summary>
/// Where the photos are kept, as folders that hold files and other folders: a computer's disks, or a phone's
/// photo library shown the same way, its albums as folders. Everything that lists, reads or changes someone's
/// photos does it through here, so that the rules about them hold wherever the photos are.
/// </summary>
public interface IPhotoFileSystem
{
    bool FileExists(string path);

    bool FolderExists(string path);

    /// <summary>The file's size and when it last changed; null when there is no file there.</summary>
    StoredFile? Describe(string path);

    /// <summary>The files directly inside a folder, hidden ones too; none when the folder is not there or may not be looked into.</summary>
    IReadOnlyList<string> FilesIn(string folder);

    /// <summary>The folders directly inside a folder, hidden ones too but not links to folders elsewhere; none when it may not be looked into.</summary>
    IReadOnlyList<string> FoldersIn(string folder);

    /// <summary>
    /// Whether a file or folder is a link to one elsewhere, or is otherwise marked as standing in for one kept
    /// elsewhere, as OneDrive marks the files it keeps online.
    /// </summary>
    bool IsLink(string path);

    /// <summary>
    /// Opens a file to read it, from any point, without holding on to it: while it is being read it can still
    /// be moved or removed. Held the usual way, a video could not be removed while its player was fetching the
    /// next piece of it, nor a picture while it was being converted for viewing.
    /// </summary>
    Stream OpenRead(string path);

    /// <summary>Whether one file holds the bytes another begins with: a whole copy of it, or the part of one that a copy cut short had written.</summary>
    bool IsCopyOf(string copy, string original);

    /// <summary>Moves a file. It never replaces one: should a file stand at <paramref name="to"/>, it fails.</summary>
    void Move(string from, string to);

    /// <summary>Copies a file. It never replaces one.</summary>
    void Copy(string from, string to);

    void MakeFolder(string path);

    /// <summary>Takes away a folder that is empty; one that holds anything stays.</summary>
    void RemoveEmptyFolder(string path);

    /// <summary>Deletes a file PhotoSense wrote itself: a copy of a file that is still there, or part of one.</summary>
    void DeleteCopy(string path);

    /// <summary>Erases a removed file for good, marked read-only or not. Only Delete permanently asks for this.</summary>
    void Erase(string path);
}

/// <summary>A file to be moved, and where to.</summary>
public sealed record FileMove(string From, string To);

/// <summary>
/// Photos kept where many changes are best made as one: a phone's photo library, which asks the person once for all
/// the changes made together, before it deletes any photo, and makes every one of them or none. Where the photos are
/// kept so, changes are handed over whole rather than one file at a time.
/// </summary>
public interface IPhotoChangesTogether
{
    /// <summary>Moves every file where it is to go, making the folders that takes.</summary>
    /// <exception cref="IOException">None was moved: one could not be, or the person said no.</exception>
    /// <exception cref="UnauthorizedAccessException">None was moved: one of them may not be.</exception>
    void MoveAll(IReadOnlyList<FileMove> moves);

    /// <summary>Erases every file given; on a phone they go to its own bin, where they can still be found for a while.</summary>
    /// <exception cref="IOException">None was erased.</exception>
    void EraseAll(IReadOnlyList<string> paths);

    /// <summary>Takes away those of the folders given that are empty; one that holds anything stays.</summary>
    void RemoveEmptyFolders(IReadOnlyList<string> folders);
}
