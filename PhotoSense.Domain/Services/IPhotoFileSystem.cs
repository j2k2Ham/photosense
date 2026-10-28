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
