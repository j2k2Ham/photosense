using LiteDB;
using Moq;
using PhotoSense.Application.Changes;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Infrastructure.Files;
using PhotoSense.Infrastructure.Persistence;

namespace PhotoSense.Tests.Application;

/// <summary>
/// The real disk, except that what a test names fails, on any system, until the test lets it through. A
/// file held open fails to move only on Windows, so tests ask this instead.
/// </summary>
internal sealed class FailingDisk : DiskFileSystem
{
    private readonly List<(string Operation, Func<string, bool> When, string? Reason)> _failing = [];

    /// <summary>Runs before every move with the file about to go, for a test that looks at that moment.</summary>
    public Action<string>? BeforeMove { get; set; }

    /// <summary>
    /// Makes an operation ("move", "copy", "make", "remove folder", "delete", "erase" or "read") fail for the paths
    /// it matches; a move or copy is matched by the file it takes.
    /// </summary>
    /// <param name="operation">Which operation fails.</param>
    /// <param name="when">Which paths it fails for.</param>
    /// <param name="reason">What the disk says; a sentence naming the operation and the file when not given.</param>
    public FailingDisk Fail(string operation, Func<string, bool> when, string? reason = null)
    {
        _failing.Add((operation, when, reason));
        return this;
    }

    /// <summary>Makes an operation fail for one path.</summary>
    public FailingDisk Fail(string operation, string path) => Fail(operation, p => PhotoPath.Comparer.Equals(p, path));

    /// <summary>Lets every operation succeed again.</summary>
    public void Heal() => _failing.Clear();

    private void Check(string operation, string path)
    {
        foreach (var (op, when, reason) in _failing)
            if (op == operation && when(path)) throw new IOException(reason ?? $"The disk would not {operation} {Path.GetFileName(path)}.");
    }

    public override void Move(string from, string to)
    {
        BeforeMove?.Invoke(from);
        Check("move", from);
        base.Move(from, to);
    }

    public override void Copy(string from, string to)
    {
        Check("copy", from);
        base.Copy(from, to);
    }

    public override void MakeFolder(string path)
    {
        Check("make", path);
        base.MakeFolder(path);
    }

    public override Stream CreateNew(string path)
    {
        Check("create", path);
        return base.CreateNew(path);
    }

    public override void RemoveEmptyFolder(string path)
    {
        Check("remove folder", path);
        base.RemoveEmptyFolder(path);
    }

    public override void DeleteCopy(string path)
    {
        Check("delete", path);
        base.DeleteCopy(path);
    }

    public override void Erase(string path)
    {
        Check("erase", path);
        base.Erase(path);
    }

    public override Stream OpenRead(string path)
    {
        Check("read", path);
        return base.OpenRead(path);
    }
}

/// <summary>A folder of files with the records of a scan and a journal of changes, all of its own.</summary>
internal sealed class ChangesFixture : IDisposable
{
    private readonly LiteDatabase _db = new(new MemoryStream());

    /// <summary>The test's own folder, deleted when it ends.</summary>
    public DirectoryInfo Root { get; } = Directory.CreateTempSubdirectory("photosense-changes-");
    /// <summary>Scans' records, in memory.</summary>
    public InMemoryPhotoRepository Repo { get; } = new();
    /// <summary>The changes, kept in a database in memory.</summary>
    public IOrganizeBatchStore Batches { get; }

    /// <summary>What the store holds of a change at this moment, read straight from the database: for a hook that runs in the middle of a move, where nothing can be awaited.</summary>
    public OrganizeBatch? Stored(Guid id) => _db.GetCollection<OrganizeBatch>("organize_batches").FindById(id);
    /// <summary>The disk, which fails as the test says.</summary>
    public FailingDisk Disk { get; } = new();
    /// <summary>What was recorded as done.</summary>
    public List<AuditEntry> Audited { get; } = [];
    /// <summary>The changes under test.</summary>
    public FileChanges Changes { get; }

    public ChangesFixture()
    {
        var audit = new Mock<IAuditRepository>();
        audit.Setup(a => a.AddAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>())).Callback<AuditEntry, CancellationToken>((e, _) => Audited.Add(e)).Returns(Task.CompletedTask);
        Batches = _db.Batches();
        Changes = new FileChanges(Batches, Repo, audit.Object, Disk);
    }

    /// <summary>A path in the test's folder.</summary>
    public string In(params string[] parts) => Path.Combine([Root.FullName, .. parts]);

    /// <summary>Writes a file below the root.</summary>
    public string Put(string relative, string content = "data")
    {
        var path = In(relative.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>A scan's record of a file, matching the file as it is now.</summary>
    public async Task<Photo> RecordAsync(string path, CancellationToken ct)
    {
        var info = new FileInfo(path);
        var photo = new Photo { SourcePath = path, FileName = info.Name, FileSizeBytes = info.Length, FileModifiedUtc = info.LastWriteTimeUtc, ScanRoot = Root.FullName, ContentHash = info.Name };
        await Repo.AddOrUpdateAsync(photo, ct);
        return photo;
    }

    /// <summary>A change of one file to each place given, from each file given, with the folders it needs listed.</summary>
    public OrganizeBatch Change(bool copy = false, bool removed = false, params (string From, string To)[] moves)
    {
        var batch = new OrganizeBatch { UtcTicks = TestClock.Day.UtcTicks, Label = "There", Copy = copy, Removed = removed };
        foreach (var (from, to) in moves)
        {
            Changes.NeedFolder(batch.CreatedFolders, Path.GetDirectoryName(to)!);
            batch.Items.Add(new OrganizeBatchItem { From = from, To = to });
        }
        return batch;
    }

    /// <summary>The names of the files in a folder of the test's, hidden ones too, in order.</summary>
    public List<string> Files(params string[] folder)
        => Directory.Exists(In(folder)) ? Directory.EnumerateFiles(In(folder)).Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal).ToList() : [];

    public void Dispose()
    {
        _db.Dispose();
        TestFiles.Remove(Root);
    }
}
