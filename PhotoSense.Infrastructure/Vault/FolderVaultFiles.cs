using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Vault;

/// <summary>
/// The hidden vault's files in a folder of PhotoSense's own inside the app, where no other app sees them.
/// </summary>
public sealed class FolderVaultFiles : IVaultFiles
{
    private const string Writing = ".writing";
    private readonly string _folder;

    /// <summary>The vault's files in <paramref name="folder"/>, made when the first is written.</summary>
    public FolderVaultFiles(string folder) => _folder = folder;

    /// <inheritdoc />
    public IReadOnlyList<VaultFileInfo> List()
    {
        if (!Directory.Exists(_folder)) return [];
        return [.. new DirectoryInfo(_folder).EnumerateFiles().Where(f => !f.Name.EndsWith(Writing, StringComparison.Ordinal))
            .Select(f => new VaultFileInfo(f.Name, f.Length, f.LastWriteTimeUtc)).OrderBy(f => f.Name, StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    /// <remarks>Written under a temporary name, through to the disk, as the item may be about to leave the library; given its own once whole.</remarks>
    public async Task WriteAsync(string name, Func<Stream, Task> write, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = PathOf(name);
        if (File.Exists(path)) throw new IOException($"The hidden vault has a file called {name} already.");
        Directory.CreateDirectory(_folder);
        var writing = path + Writing;
        var whole = false;
        try
        {
            await using (var output = new FileStream(writing, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
                await write(output);
            File.Move(writing, path, overwrite: false);
            whole = true;
        }
        // A file cut short is not the vault's.
        finally { if (!whole) File.Delete(writing); }
    }

    /// <inheritdoc />
    public Stream OpenRead(string name) => new FileStream(PathOf(name), FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

    /// <inheritdoc />
    /// <remarks>A file that is not there, or is still being written, is gone already.</remarks>
    public void Delete(string name)
    {
        if (!Directory.Exists(_folder)) return;
        File.Delete(PathOf(name));
        File.Delete(PathOf(name) + Writing);
    }

    // A plain name inside the folder, so that no name can reach outside it.
    private string PathOf(string name)
    {
        if (name.Length == 0 || name != Path.GetFileName(name) || name is "." or ".." || name.EndsWith(Writing, StringComparison.Ordinal))
            throw new ArgumentException($"{name} is not a name of the hidden vault.", nameof(name));
        return Path.Combine(_folder, name);
    }
}
