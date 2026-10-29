using LiteDB;
using Moq;
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
    public static readonly MediaDetails Nothing = new(null, 0, 0, null, null, null);
    public Dictionary<string, MediaDetails> Says { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Reads { get; } = [];
    /// <summary>Runs when a file is read, for a test that needs something to change at that moment.</summary>
    public Action<string>? OnRead { get; set; }

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
    public int Calls;
    public Task<string> ComputeHashAsync(Stream imageStream, CancellationToken ct = default)
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
    public DirectoryInfo Root { get; } = Directory.CreateTempSubdirectory("photosense-organize-");
    public InMemoryPhotoRepository Repo { get; } = new();
    public FakeDetailsReader Reader { get; } = new();
    public CountingHasher Hasher { get; } = new();
    public Mock<IPlaceNameResolver> Resolver { get; } = new();
    public List<AuditEntry> Audited { get; } = [];
    public OrganizeLibrary Library { get; }
    public PlaceLookup Places { get; }
    public OrganizePlanner Planner { get; }
    public LiteDbOrganizeBatchStore Batches { get; }
    public LiteDbMediaDetailsStore Details { get; }
    private readonly LiteDatabase _db = new(new MemoryStream());
    private readonly Mock<IAuditRepository> _audit = new();

    public OrganizeFixture()
    {
        _audit.Setup(a => a.AddAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>())).Callback<AuditEntry, CancellationToken>((e, _) => Audited.Add(e)).Returns(Task.CompletedTask);
        Details = new LiteDbMediaDetailsStore(_db);
        Library = new OrganizeLibrary(Repo, Reader, Hasher, Details);
        Places = new PlaceLookup(Resolver.Object);
        Planner = new OrganizePlanner(Library, Finder, Places);
        Batches = new LiteDbOrganizeBatchStore(_db);
    }

    public CompanionFileFinder Finder { get; } = new(ContentAsId);

    /// <summary>The library as another run of the service would have it: nothing in memory, the same stores behind it.</summary>
    public OrganizeLibrary AnotherRun() => new(Repo, Reader, Hasher, Details);

    public OrganizeMover Mover(ICompanionFileFinder? finder = null) => new(Library, finder ?? Finder, Repo, Batches, _audit.Object);

    /// <param name="eraseFile">Stands in for erasing a file, for a test in which one cannot be erased.</param>
    public PhotoSense.Application.Removal.RemovedFiles Removed(Action<string>? eraseFile = null) => new(Repo, Batches, _audit.Object, eraseFile);

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

    public async Task<string> IdAsync(string path) => (await Library.DescribeAsync(path))!.Id;

    /// <summary>A scan's record of a file, matching the file as it is now.</summary>
    public async Task<Photo> ScannedAsync(string path, DateTime? takenOn = null, string? contentHash = "ABCDEF", long? size = null)
    {
        var info = new FileInfo(path);
        var photo = new Photo
        {
            SourcePath = path, FileName = info.Name, FileSizeBytes = size ?? info.Length, FileModifiedUtc = info.LastWriteTimeUtc, ContentHash = contentHash,
            TakenOn = takenOn, Width = 4032, Height = 3024, Latitude = 46.1283, Longitude = -112.9423,
        };
        await Repo.AddOrUpdateAsync(photo);
        return photo;
    }

    public List<string> Files(params string[] folder)
        => Directory.Exists(In(folder)) ? Directory.EnumerateFiles(In(folder)).Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal).ToList() : [];
}
