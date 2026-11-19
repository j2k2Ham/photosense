using PhotoSense.Domain.Services;

namespace PhotoSense.Tests;

/// <summary>The locations' secrets, in memory, in place of the Keychain or the Android Keystore.</summary>
internal sealed class MemoryLocationSecrets : ILocationSecrets
{
    /// <summary>What is kept, by location.</summary>
    public readonly Dictionary<string, IReadOnlyDictionary<string, string>> Kept = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> ReadAsync(string id, CancellationToken ct)
    {
        lock (Kept) return Task.FromResult(Kept.GetValueOrDefault(id) ?? new Dictionary<string, string>());
    }

    /// <inheritdoc />
    public Task SaveAsync(string id, IReadOnlyDictionary<string, string> secrets, CancellationToken ct)
    {
        lock (Kept) Kept[id] = new Dictionary<string, string>(secrets);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(string id, CancellationToken ct)
    {
        lock (Kept) Kept.Remove(id);
        return Task.CompletedTask;
    }
}

/// <summary>The system's sign-in sheet, answered as the test says: the service sends the person back to <see cref="Back"/>, or they close it.</summary>
internal sealed class FakeWebSignIn : IWebSignIn
{
    /// <summary>Where the service sends the person back, given where they were sent and the sign-in's state; null closes the sheet.</summary>
    public Func<Uri, Uri?> Back { get; set; } = start => null;

    /// <summary>Where each sign-in began, and the scheme it waited for.</summary>
    public readonly List<(Uri Start, string Scheme)> Opened = [];

    /// <inheritdoc />
    public Task<Uri> SignInAsync(Uri start, string callbackScheme, CancellationToken ct)
    {
        Opened.Add((start, callbackScheme));
        return Back(start) is { } back ? Task.FromResult(back) : throw new OperationCanceledException("The person closed the sheet.");
    }
}

/// <summary>A phone's backup locations and what each holds, in memory.</summary>
internal sealed class MemoryLocationStore : ILocationStore
{
    private readonly Dictionary<string, BackupLocation> _locations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, LocationCopy>> _copies = new(StringComparer.Ordinal);
    private int _countAt = 1;

    /// <inheritdoc />
    public Task<IReadOnlyList<BackupLocation>> ListAsync(CancellationToken ct)
    {
        lock (_locations) return Task.FromResult<IReadOnlyList<BackupLocation>>([.. _locations.Values]);
    }

    /// <inheritdoc />
    public Task SaveAsync(BackupLocation location, CancellationToken ct)
    {
        lock (_locations) _locations[location.Id] = location;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string id, CancellationToken ct)
    {
        lock (_locations)
        {
            _locations.Remove(id);
            _copies.Remove(id);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, LocationCopy>> CopiesAsync(string id, CancellationToken ct)
    {
        lock (_locations) return Task.FromResult<IReadOnlyDictionary<string, LocationCopy>>(new Dictionary<string, LocationCopy>(_copies.GetValueOrDefault(id) ?? [], StringComparer.Ordinal));
    }

    /// <inheritdoc />
    public Task KeepCopyAsync(string id, string sha256, LocationCopy copy, CancellationToken ct)
    {
        lock (_locations)
        {
            if (!_copies.TryGetValue(id, out var copies)) _copies[id] = copies = new(StringComparer.Ordinal);
            copies[sha256] = copy;
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ForgetCopyAsync(string id, string sha256, CancellationToken ct)
    {
        lock (_locations) _copies.GetValueOrDefault(id)?.Remove(sha256);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> CountAtAsync(CancellationToken ct) => Task.FromResult(_countAt);

    /// <inheritdoc />
    public Task SaveCountAtAsync(int locations, CancellationToken ct)
    {
        _countAt = locations;
        return Task.CompletedTask;
    }
}

/// <summary>
/// A location's folder in memory, as each kind would answer: with a checksum it keeps, or none; refusing a name already
/// taken; and failing as the test says.
/// </summary>
internal sealed class MemoryFolder : IRemoteFolder
{
    /// <summary>The files, by name.</summary>
    public readonly Dictionary<string, byte[]> Files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Which checksum it keeps, or none, so that what arrives is read back.</summary>
    public ChecksumKind? Keeps { get; set; } = ChecksumKind.Sha256;

    /// <summary>Fails each of these, by what it is asked: "list", "put", "read", "delete", "space", "checksum".</summary>
    public readonly Dictionary<string, Exception> Fails = new(StringComparer.Ordinal);

    /// <summary>Changes what arrives, as a server that damaged it would.</summary>
    public Func<byte[], byte[]> Damage { get; set; } = bytes => bytes;

    /// <summary>How much it says it holds; null says nothing.</summary>
    public RemoteSpace? Space { get; set; }

    /// <summary>Called as each file's checksum is asked for: a test can pause there.</summary>
    public Func<string, Task> Checking { get; set; } = _ => Task.CompletedTask;

    /// <summary>Called as each file is put, before it is kept: a test can pause or stop there.</summary>
    public Func<string, Task> Putting { get; set; } = _ => Task.CompletedTask;

    /// <inheritdoc />
    public string Where { get; set; } = "photos-archive/PhotoSense";

    /// <inheritdoc />
    public Task<IReadOnlyList<RemoteFile>> ListAsync(CancellationToken ct)
    {
        Fail("list");
        lock (Files) return Task.FromResult<IReadOnlyList<RemoteFile>>([.. Files.Select(f => new RemoteFile(f.Key, f.Value.Length))]);
    }

    /// <inheritdoc />
    public async Task<RemoteChecksum?> PutAsync(string name, Stream content, long length, CancellationToken ct)
    {
        Fail("put");
        await Putting(name);
        ct.ThrowIfCancellationRequested();
        var bytes = new MemoryStream();
        await content.CopyToAsync(bytes, ct);
        lock (Files)
        {
            if (Files.ContainsKey(name)) throw new IOException($"A file named {name} is already there, and PhotoSense never replaces one.");
            Files[name] = Damage(bytes.ToArray());
        }
        return await ChecksumAsync(name, ct);
    }

    /// <inheritdoc />
    public async Task<RemoteChecksum?> ChecksumAsync(string name, CancellationToken ct)
    {
        Fail("checksum");
        await Checking(name);
        ct.ThrowIfCancellationRequested();
        byte[] bytes;
        lock (Files) bytes = Files.TryGetValue(name, out var found) ? found : throw new FileNotFoundException(name);
        if (Keeps is not { } kind) return null;
        using var hashing = await PhotoSense.Application.Locations.HashingStream.OfAsync(new MemoryStream(bytes), ct);
        return new RemoteChecksum(kind, hashing.Of(kind));
    }

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(string name, CancellationToken ct)
    {
        Fail("read");
        lock (Files) return Task.FromResult<Stream>(new MemoryStream(Files.TryGetValue(name, out var found) ? found : throw new FileNotFoundException(name)));
    }

    /// <inheritdoc />
    public Task DeleteAsync(string name, CancellationToken ct)
    {
        Fail("delete");
        lock (Files) Files.Remove(name);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<RemoteSpace?> SpaceAsync(CancellationToken ct)
    {
        Fail("space");
        return Task.FromResult(Space);
    }

    private void Fail(string what)
    {
        if (Fails.TryGetValue(what, out var failure)) throw failure;
    }
}

/// <summary>Reaches the folders in memory, one for each location, made as each is first opened.</summary>
internal sealed class MemoryFolders : IRemoteFolders
{
    /// <summary>Each location's folder, by its id; one added before the location is opened is the one it gets.</summary>
    public readonly Dictionary<string, MemoryFolder> Folders = new(StringComparer.Ordinal);

    /// <summary>The secrets each location was last opened with.</summary>
    public readonly Dictionary<string, IReadOnlyDictionary<string, string>> OpenedWith = new(StringComparer.Ordinal);

    /// <summary>What to do with renewed tokens, as a cloud service's folder would hand them back.</summary>
    public Func<IReadOnlyDictionary<string, string>, CancellationToken, Task>? Renewed { get; private set; }

    /// <inheritdoc />
    public IRemoteFolder Open(BackupLocation location, IReadOnlyDictionary<string, string> secrets, Func<IReadOnlyDictionary<string, string>, CancellationToken, Task> renewed)
    {
        Renewed = renewed;
        lock (Folders)
        {
            OpenedWith[location.Id] = secrets;
            if (!Folders.TryGetValue(location.Id, out var folder)) Folders[location.Id] = folder = new MemoryFolder();
            return folder;
        }
    }
}

/// <summary>Signing in to the cloud services as the test says: available or not, and what each finish gives.</summary>
internal sealed class FakeCloudAccounts : ICloudAccounts
{
    /// <summary>The services PhotoSense is registered with.</summary>
    public HashSet<LocationKind> Registered { get; } = [.. LocationKinds.Cloud];

    /// <summary>What a finished sign-in gives; throws as a refusal would.</summary>
    public Func<LocationKind, Uri, CloudAccount> Finish { get; set; } = (kind, _) => new CloudAccount("alex.m@gmail.com", new Dictionary<string, string> { ["access"] = "a", ["refresh"] = "r" });

    /// <inheritdoc />
    public bool Available(LocationKind kind) => Registered.Contains(kind);

    /// <inheritdoc />
    public CloudSignInStart Begin(LocationKind kind) => new(new Uri($"https://sign-in.example/{LocationKinds.IdOf(kind)}"), "com.j2k2ham.photosense.auth", "state", "verifier");

    /// <inheritdoc />
    public Task<CloudAccount> FinishAsync(LocationKind kind, CloudSignInStart begun, Uri callback, CancellationToken ct) => Task.FromResult(Finish(kind, callback));
}
