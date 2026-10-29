using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Files;

/// <summary>
/// The computer's own disks. A test stands in for it where a disk has to fail at one exact moment, which no
/// real disk does on demand on every system.
/// </summary>
public class DiskFileSystem : IPhotoFileSystem
{
    // Hidden and system files are part of a folder too, and an entry that may not be looked at is passed over.
    private static readonly EnumerationOptions Everything = new() { IgnoreInaccessible = true, AttributesToSkip = 0 };

    public virtual bool FileExists(string path) => File.Exists(path);

    public virtual bool FolderExists(string path) => Directory.Exists(path);

    public virtual StoredFile? Describe(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? new StoredFile(info.Length, info.LastWriteTimeUtc) : null;
    }

    public virtual IReadOnlyList<string> FilesIn(string folder) => Read(folder, f => Directory.EnumerateFiles(f, "*", Everything));

    // A link would show the same files under a second path, making each look like a copy of itself. Only links
    // are left out: a folder of files kept online (OneDrive's, for one) is a reparse point as well, and is listed.
    public virtual IReadOnlyList<string> FoldersIn(string folder)
        => Read(folder, f => Directory.EnumerateDirectories(f, "*", Everything).Where(d => new DirectoryInfo(d).LinkTarget is null));

    // Anything the system marks as pointing elsewhere counts, OneDrive's files kept online among them.
    public virtual bool IsLink(string path)
    {
        FileSystemInfo entry = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        return entry.Exists && entry.Attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    public virtual Stream OpenRead(string path)
        => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);

    public virtual bool IsCopyOf(string copy, string original)
    {
        using var part = OpenRead(copy);
        using var whole = OpenRead(original);
        return Copies.BeginsWith(whole, part);
    }

    public virtual void Move(string from, string to) => File.Move(from, to, overwrite: false);

    public virtual void Copy(string from, string to) => File.Copy(from, to, overwrite: false);

    public virtual Stream CreateNew(string path) => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16);

    public virtual void MakeFolder(string path) => Directory.CreateDirectory(path);

    public virtual void RemoveEmptyFolder(string path)
    {
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
    }

    public virtual void DeleteCopy(string path) => File.Delete(path);

    public virtual void Erase(string path)
    {
        var file = new FileInfo(path);
        // A file marked read-only cannot be erased until the mark is taken off.
        if (file.IsReadOnly) file.IsReadOnly = false;
        file.Delete();
    }

    // A folder that is not there, or may not be looked into, holds nothing as far as anyone can tell; nor does
    // a path that could not name a folder at all, such as the folder of a bare file name.
    private static IReadOnlyList<string> Read(string folder, Func<string, IEnumerable<string>> list)
    {
        try { return list(folder).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return []; }
    }
}
