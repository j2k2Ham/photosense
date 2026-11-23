using System.Net.Sockets;
using Renci.SshNet;
using Renci.SshNet.Common;
using Trovolve.Domain.Services;

namespace Trovolve.Infrastructure.Locations;

/// <summary>
/// A location's folder on a server reached over SSH (SFTP). SFTP keeps no checksum, so what arrives is read back; a file
/// is only ever created new there, so nothing is replaced. The folder is made the first time it is listed. SFTP does
/// not say reliably how full a server is, so nothing is said.
/// </summary>
public sealed class SftpFolder : IRemoteFolder, IDisposable
{
    private readonly ISftpClient _sftp;
    private readonly string _folder;
    private readonly string _host;

    /// <summary>The folder <paramref name="folder"/> on the server <paramref name="sftp"/> reaches.</summary>
    /// <param name="sftp">The server, connected on first use.</param>
    /// <param name="folder">The folder on it: "/home/alex/Trovolve".</param>
    /// <param name="host">The server's name, as the person typed it, for what is said when it cannot be reached.</param>
    public SftpFolder(ISftpClient sftp, string folder, string host)
    {
        _sftp = sftp;
        _folder = folder.TrimEnd('/');
        _host = host;
    }

    /// <inheritdoc />
    public string Where => _folder;

    private string File(string name) => $"{_folder}/{name}";

    /// <inheritdoc />
    public Task<IReadOnlyList<RemoteFile>> ListAsync(CancellationToken ct) => AskAsync<IReadOnlyList<RemoteFile>>(async () =>
    {
        if (!await _sftp.ExistsAsync(_folder, ct))
        {
            await MakeFolderAsync(ct);
            return [];
        }
        var files = new List<RemoteFile>();
        await foreach (var file in _sftp.ListDirectoryAsync(_folder, ct))
            if (file.IsRegularFile) files.Add(new RemoteFile(file.Name, file.Length));
        return files;
    }, "list", null);

    /// <inheritdoc />
    public Task<RemoteChecksum?> PutAsync(string name, Stream content, long length, CancellationToken ct) => AskAsync<RemoteChecksum?>(async () =>
    {
        // Never over a file there: a name taken is refused, so nothing is replaced.
        await _sftp.UploadFileAsync(content, File(name), canOverride: false, uploadProgress: null, ct);
        return null;
    }, "write to", name);

    /// <inheritdoc />
    public Task<RemoteChecksum?> ChecksumAsync(string name, CancellationToken ct) => AskAsync<RemoteChecksum?>(async () =>
        await _sftp.ExistsAsync(File(name), ct) ? null : throw new FileNotFoundException($"{name} is not in {_folder}.", name), "read", name);

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(string name, CancellationToken ct) => AskAsync<Stream>(async () =>
    {
        // Read into a file of Trovolve's own, gone once read, as a large video does not fit in memory.
        var copy = new FileStream(Path.Combine(Path.GetTempPath(), $"trovolve-{Guid.NewGuid():N}.tmp"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        var handedOver = false;
        try
        {
            await _sftp.DownloadFileAsync(File(name), copy, ct);
            copy.Position = 0;
            handedOver = true;
            return copy;
        }
        finally
        {
            if (!handedOver) await copy.DisposeAsync();
        }
    }, "read", name);

    /// <inheritdoc />
    public Task DeleteAsync(string name, CancellationToken ct) => AskAsync(async () =>
    {
        if (await _sftp.ExistsAsync(File(name), ct)) await _sftp.DeleteFileAsync(File(name), ct);
        return true;
    }, "delete in", name);

    /// <inheritdoc />
    public Task<RemoteSpace?> SpaceAsync(CancellationToken ct) => Task.FromResult<RemoteSpace?>(null);

    /// <summary>Lets go of the connection.</summary>
    public void Dispose() => _sftp.Dispose();

    private async Task MakeFolderAsync(CancellationToken ct)
    {
        var path = _folder.StartsWith('/') ? "" : ".";
        foreach (var part in _folder.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            path = $"{path}/{part}";
            if (!await _sftp.ExistsAsync(path, ct)) await _sftp.CreateDirectoryAsync(path, ct);
        }
    }

    // Connects first if need be, and says in plain words what went wrong and what to change.
    private async Task<T> AskAsync<T>(Func<Task<T>> ask, string doing, string? name)
    {
        try
        {
            if (!_sftp.IsConnected) await _sftp.ConnectAsync(CancellationToken.None);
            return await ask();
        }
        catch (SftpPathNotFoundException ex)
        {
            throw new FileNotFoundException($"{name} is not in {_folder}.", name, ex);
        }
        catch (SshAuthenticationException ex)
        {
            throw new IOException("The user name, password or key was not accepted. Check them and try again.", ex);
        }
        catch (SftpPermissionDeniedException ex)
        {
            throw new IOException($"This account is not allowed to {doing} {_folder}. Check its permissions and try again.", ex);
        }
        catch (Exception ex) when (ex is SocketException or SshConnectionException or SshOperationTimeoutException)
        {
            throw new IOException($"Trovolve could not reach {_host}: {ex.Message}", ex);
        }
        catch (SshException ex)
        {
            throw new IOException(ex.Message.Contains("exist", StringComparison.OrdinalIgnoreCase)
                ? $"A file named {name} is already there, and Trovolve never replaces one." : ex.Message, ex);
        }
    }
}
