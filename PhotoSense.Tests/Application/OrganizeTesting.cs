using LiteDB;
using Moq;
using PhotoSense.Application.Changes;
using PhotoSense.Application.Organizing;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Deletion;
using PhotoSense.Infrastructure.Hashing;
using PhotoSense.Infrastructure.Persistence;

namespace PhotoSense.Tests.Application;

/// <summary>Stands in for reading a file's details: what each file "says" is set by the test, and every read is counted.</summary>
internal sealed class FakeDetailsReader : IMediaDetailsReader
{
    /// <summary>What a file that says nothing yields.</summary>
    public static readonly MediaDetails Nothing = new(null, 0, 0, null, null, null);
    /// <summary>What each file says, by its name; a file not listed says nothing.</summary>
    public Dictionary<string, MediaDetails> Says { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The files read, in order.</summary>
    public List<string> Reads { get; } = [];
    /// <summary>Runs when a file is read, for a test that needs something to change at that moment.</summary>
    public Action<string>? OnRead { get; set; }

    /// <inheritdoc />
    public MediaDetails Read(string path)
    {
        lock (Reads) Reads.Add(Path.GetFileName(path));
        OnRead?.Invoke(path);
        return Says.GetValueOrDefault(Path.GetFileName(path), Nothing);
    }
}

/// <summary>Counts how often files are read through to be hashed.</summary>
internal sealed class CountingHasher : IImageHashingService
{
    private readonly Sha256ImageHashingService _inner = new();
    /// <summary>How many files were hashed.</summary>
    public int Calls;
    /// <inheritdoc />
    public Task<string> ComputeHashAsync(Stream imageStream, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        return _inner.ComputeHashAsync(imageStream, ct);
    }
}

/// <summary>
/// A folder of files with everything Organize needs around it. A file's content stands in for the Live
/// Photo identifier it carries ("id:..."), as in the tests of the companion finder.
/// </summary>
internal sealed class OrganizeFixture : IDisposable
{
    /// <summary>The test's own folder, deleted when it ends.</summary>
    public DirectoryInfo Root { get; } = Directory.CreateTempSubdirectory("photosense-organize-");
    /// <summary>Scans' records, in memory.</summary>
    public InMemoryPhotoRepository Repo { get; } = new();
    /// <summary>Reads what the test says each file says.</summary>
    public FakeDetailsReader Reader { get; } = new();
    /// <summary>Hashes files, and counts them.</summary>
    public CountingHasher Hasher { get; } = new();
    /// <summary>Names places as the test sets it up to.</summary>
    public Mock<IPlaceNameResolver> Resolver { get; } = new();
    /// <summary>What was recorded as done.</summary>
    public List<AuditEntry> Audited { get; } = [];
    /// <summary>The files Organize knows.</summary>
    public OrganizeLibrary Library { get; }
    /// <summary>Looks up places through the resolver.</summary>
    public PlaceLookup Places { get; }
    /// <summary>Plans moves over the library.</summary>
    public OrganizePlanner Planner { get; }
    /// <summary>The changes, kept in a database in memory.</summary>
    public LiteDbOrganizeBatchStore Batches { get; }
    /// <summary>What Organize read, kept in a database in memory.</summary>
    public LiteDbMediaDetailsStore Details { get; }
    /// <summary>The disk the moves go through; a test can make it fail.</summary>
    public FailingDisk Disk { get; } = new();
    /// <summary>The changes moves are made through.</summary>
    public FileChanges Changes { get; }
    private readonly LiteDatabase _db = new(new MemoryStream());
    private readonly Mock<IAuditRepository> _audit = new();

    public OrganizeFixture()
    {
        _audit.Setup(a => a.AddAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>())).Callback<AuditEntry, CancellationToken>((e, _) => Audited.Add(e)).Returns(Task.CompletedTask);
        Details = new LiteDbMediaDetailsStore(_db);
        Library = new OrganizeLibrary(Repo, Reader, Hasher, Details, Disk);
        Places = new PlaceLookup(Resolver.Object);
        Finder = new CompanionFileFinder(Disk, ContentAsId);
        Planner = new OrganizePlanner(Library, Finder, Places, Disk);
        Batches = new LiteDbOrganizeBatchStore(_db);
        Changes = new FileChanges(Batches, Repo, _audit.Object, Disk);
    }

    /// <summary>Finds companions, reading a file's content as its Live Photo identifier.</summary>
    public CompanionFileFinder Finder { get; }

    /// <summary>The library as another run of the service would have it: nothing in memory, the same stores behind it.</summary>
    public OrganizeLibrary AnotherRun() => new(Repo, Reader, Hasher, Details, Disk);

    /// <summary>The clock changes are stamped with.</summary>
    public TestClock Clock { get; } = new(TestClock.Day, TimeSpan.FromSeconds(1));

    /// <summary>Moves files for Organize, with the finder given or the fixture's own.</summary>
    public OrganizeMover Mover(ICompanionFileFinder? finder = null) => new(Library, finder ?? Finder, Changes, _audit.Object, Disk, Clock);

    /// <param name="files">The file system it erases through; the fixture's own disk when not given.</param>
    public PhotoSense.Application.Removal.RemovedFiles Removed(IPhotoFileSystem? files = null) => new(Repo, Batches, _audit.Object, files ?? Disk);

    public void Dispose()
    {
        _db.Dispose();
        TestFiles.Remove(Root);
    }

    private static string? ContentAsId(string path)
    {
        var text = File.ReadAllText(path);
        return text.StartsWith("id:", StringComparison.Ordinal) ? text[3..] : null;
    }

    /// <summary>A path in the test's folder.</summary>
    public string In(params string[] parts) => Path.Combine([Root.FullName, .. parts]);

    /// <summary>Writes a file below the root; its last change is set to noon on the first of January of the year given.</summary>
    public string Put(string relative, string content = "data", int year = 2020)
    {
        var path = In(relative.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTime(path, new DateTime(year, 1, 1, 12, 0, 0, DateTimeKind.Local));
        return path;
    }

    /// <summary>The id Organize gives the file at a path.</summary>
    public async Task<string> IdAsync(string path, CancellationToken ct) => (await Library.DescribeAsync(path, ct))!.Id;

    /// <summary>A scan's record of a file, matching the file as it is now.</summary>
    public async Task<Photo> ScannedAsync(string path, CancellationToken ct, DateTime? takenOn = null, string? contentHash = "ABCDEF", long? size = null)
    {
        var info = new FileInfo(path);
        var photo = new Photo
        {
            SourcePath = path, FileName = info.Name, FileSizeBytes = size ?? info.Length, FileModifiedUtc = info.LastWriteTimeUtc, ContentHash = contentHash,
            TakenOn = takenOn, Width = 4032, Height = 3024, Latitude = 46.1283, Longitude = -112.9423,
        };
        await Repo.AddOrUpdateAsync(photo, ct);
        return photo;
    }

    /// <summary>The names of the files in a folder of the test's, in order.</summary>
    public List<string> Files(params string[] folder)
        => Directory.Exists(In(folder)) ? Directory.EnumerateFiles(In(folder)).Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal).ToList() : [];
}
