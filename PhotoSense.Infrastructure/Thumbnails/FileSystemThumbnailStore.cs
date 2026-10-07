using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Thumbnails;

public class FileSystemThumbnailStore : IThumbnailStore
{
    private readonly string _root;

    public FileSystemThumbnailStore(string root) => _root = root;

    public async Task SaveAsync(string contentHash, byte[] jpeg, CancellationToken ct = default)
    {
        var path = PathFor(contentHash);
        if (File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Written under a temporary name so a reader never sees a half-written file.
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(temp, jpeg, ct);
        try { File.Move(temp, path, overwrite: false); }
        catch (IOException) { File.Delete(temp); } // an identical file was saved first
    }

    public async Task<byte[]?> GetAsync(string contentHash, CancellationToken ct = default)
    {
        var path = PathFor(contentHash);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : null;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        return Task.CompletedTask;
    }

    // Content hashes are hex, so they are safe as file names; two-character folders keep directories small.
    private string PathFor(string contentHash)
    {
        if (contentHash.Length < 3 || !contentHash.All(char.IsAsciiHexDigit))
            throw new ArgumentException("Not a content hash", nameof(contentHash));
        return Path.Combine(_root, contentHash[..2], contentHash + ".jpg");
    }
}
