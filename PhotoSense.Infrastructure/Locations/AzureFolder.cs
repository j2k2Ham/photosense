using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Locations;

/// <summary>
/// A location's folder in an Azure Blob Storage container. Azure checks each file's MD5 as it arrives, and keeps it;
/// a name already taken is refused by Azure itself (If-None-Match), so nothing there is ever replaced.
/// </summary>
public sealed class AzureFolder : IRemoteFolder
{
    private readonly BlobContainerClient _container;
    private readonly string _prefix;
    private readonly AccessTier? _tier;

    /// <summary>The folder <paramref name="prefix"/> in <paramref name="container"/>.</summary>
    /// <param name="container">The container.</param>
    /// <param name="prefix">The folder in it, without slashes at either end.</param>
    /// <param name="tier">The access tier, as the page names it: "Hot", "Cool" or "Cold"; null for the account's own.</param>
    public AzureFolder(BlobContainerClient container, string prefix, string? tier)
    {
        _container = container;
        _prefix = prefix;
        // Typed, as AccessTier would otherwise take the null for a name of its own.
        _tier = tier switch { "Hot" => AccessTier.Hot, "Cool" => AccessTier.Cool, "Cold" => AccessTier.Cold, _ => (AccessTier?)null };
    }

    /// <inheritdoc />
    public string Where => $"{_container.Name}/{_prefix}";

    private BlobClient Blob(string name) => _container.GetBlobClient($"{_prefix}/{name}");

    /// <inheritdoc />
    public Task<IReadOnlyList<RemoteFile>> ListAsync(CancellationToken ct) => AskAsync<IReadOnlyList<RemoteFile>>(async () =>
    {
        var files = new List<RemoteFile>();
        await foreach (var blob in _container.GetBlobsAsync(new GetBlobsOptions { Prefix = $"{_prefix}/" }, ct))
        {
            var name = blob.Name[(_prefix.Length + 1)..];
            if (!name.Contains('/')) files.Add(new RemoteFile(name, blob.Properties.ContentLength ?? 0));
        }
        return files;
    }, "list the container", null);

    /// <inheritdoc />
    public Task<RemoteChecksum?> PutAsync(string name, Stream content, long length, CancellationToken ct) => AskAsync(async () =>
    {
        var blob = Blob(name);
        await blob.UploadAsync(content, new BlobUploadOptions
        {
            AccessTier = _tier,
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
            // Azure checks each part's MD5 as it arrives.
            TransferValidation = new UploadTransferValidationOptions { ChecksumAlgorithm = StorageChecksumAlgorithm.MD5 },
        }, ct);
        return Md5Of((await blob.GetPropertiesAsync(cancellationToken: ct)).Value.ContentHash);
    }, "write to the container", name);

    /// <inheritdoc />
    public Task<RemoteChecksum?> ChecksumAsync(string name, CancellationToken ct)
        => AskAsync(async () => Md5Of((await Blob(name).GetPropertiesAsync(cancellationToken: ct)).Value.ContentHash), "read the container", name);

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(string name, CancellationToken ct)
        => AskAsync(async () => (await Blob(name).DownloadStreamingAsync(cancellationToken: ct)).Value.Content, "read the container", name);

    /// <inheritdoc />
    public Task DeleteAsync(string name, CancellationToken ct) => AskAsync(async () => await Blob(name).DeleteIfExistsAsync(cancellationToken: ct), "delete in the container", name);

    /// <inheritdoc />
    public Task<RemoteSpace?> SpaceAsync(CancellationToken ct) => Task.FromResult<RemoteSpace?>(null);

    // Azure keeps the MD5 of a file put in one piece; one put in parts has none, and is read back.
    private static RemoteChecksum? Md5Of(byte[]? hash) => hash is { Length: > 0 } ? new RemoteChecksum(ChecksumKind.Md5, Convert.ToHexStringLower(hash)) : null;

    private async Task<T> AskAsync<T>(Func<Task<T>> ask, string doing, string? name)
    {
        try
        {
            return await ask();
        }
        catch (RequestFailedException ex) when (ex.ErrorCode is "BlobNotFound")
        {
            throw new FileNotFoundException($"{name} is not in {Where}.", name, ex);
        }
        catch (RequestFailedException ex)
        {
            throw new IOException(ex.ErrorCode switch
            {
                "ContainerNotFound" => $"There is no container named {_container.Name} in {_container.AccountName}. Check its name.",
                "AuthenticationFailed" => "The key was not accepted. Check it and try again.",
                "AuthorizationFailure" or "AuthorizationPermissionMismatch" =>
                    $"This key is not allowed to {doing}. Give it read, write, list and delete permissions on {_container.Name} and try again.",
                "BlobAlreadyExists" or "ConditionNotMet" => $"A file named {name} is already there, and PhotoSense never replaces one.",
                _ when ex.Status == 0 => $"PhotoSense could not reach {_container.AccountName}: {ex.Message}",
                _ => ex.Message,
            }, ex);
        }
    }
}
