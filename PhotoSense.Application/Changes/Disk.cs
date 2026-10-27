namespace PhotoSense.Application.Changes;

/// <summary>
/// The file system as changes use it. A test stands in for it where a disk has to fail at one exact moment,
/// which no real disk does on demand on every system.
/// </summary>
public class Disk
{
    public static Disk Real { get; } = new();

    public virtual bool FileExists(string path) => File.Exists(path);

    public virtual bool FolderExists(string path) => Directory.Exists(path);

    /// <summary>Moves a file. It never replaces one: should a file stand at <paramref name="to"/>, it fails.</summary>
    public virtual void Move(string from, string to) => File.Move(from, to, overwrite: false);

    /// <summary>Copies a file. It never replaces one.</summary>
    public virtual void Copy(string from, string to) => File.Copy(from, to, overwrite: false);

    public virtual void MakeFolder(string path) => Directory.CreateDirectory(path);

    /// <summary>Takes away a folder that is empty; one that holds anything stays.</summary>
    public virtual void RemoveEmptyFolder(string path)
    {
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
    }

    /// <summary>Deletes a file PhotoSense wrote itself: a copy of a file that is still there, or part of one.</summary>
    public virtual void DeleteCopy(string path) => File.Delete(path);

    public virtual long Length(string path) => new FileInfo(path).Length;

    public virtual long ModifiedUtcTicks(string path) => File.GetLastWriteTimeUtc(path).Ticks;

    /// <summary>
    /// Whether one file holds the bytes another begins with: a whole copy of it, or the part of one that a
    /// copy cut short had written.
    /// </summary>
    public virtual bool IsCopyOf(string copy, string original)
    {
        using var part = File.OpenRead(copy);
        using var whole = File.OpenRead(original);
        if (part.Length > whole.Length) return false;
        byte[] a = new byte[81920], b = new byte[81920];
        for (int read; (read = part.Read(a)) > 0;)
        {
            whole.ReadExactly(b, 0, read);
            if (!a.AsSpan(0, read).SequenceEqual(b.AsSpan(0, read))) return false;
        }
        return true;
    }
}
