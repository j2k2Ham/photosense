using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Trovolve.Domain.Services;

namespace Trovolve.Infrastructure.Locations;

/// <summary>
/// A location's folder in an Amazon S3 bucket, or a bucket of a service that answers as S3 does. Amazon keeps the
/// SHA-256 it checked as each file arrived; a service that keeps none is read back. A name already taken is refused by
/// S3 itself (If-None-Match), so nothing there is ever replaced.
/// </summary>
public sealed class S3Folder : IRemoteFolder
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly string _prefix;
    private readonly bool _sha256;
    private readonly S3StorageClass? _class;

    /// <summary>The folder <paramref name="prefix"/> in <paramref name="bucket"/>, reached through <paramref name="s3"/>.</summary>
    /// <param name="s3">The bucket's service.</param>
    /// <param name="bucket">The bucket.</param>
    /// <param name="prefix">The folder in it, without slashes at either end.</param>
    /// <param name="sha256">Ask the service to check and keep each file's SHA-256, as Amazon S3 does.</param>
    /// <param name="storageClass">Amazon's storage class, as the page names it: "Standard", "Standard-IA" or "Glacier Instant Retrieval"; null for the bucket's own.</param>
    public S3Folder(IAmazonS3 s3, string bucket, string prefix, bool sha256, string? storageClass)
    {
        _s3 = s3;
        _bucket = bucket;
        _prefix = prefix;
        _sha256 = sha256;
        _class = storageClass switch
        {
            "Standard-IA" => S3StorageClass.StandardInfrequentAccess,
            "Glacier Instant Retrieval" => S3StorageClass.GlacierInstantRetrieval,
            "Standard" => S3StorageClass.Standard,
            _ => null,
        };
    }

    /// <inheritdoc />
    public string Where => $"{_bucket}/{_prefix}";

    private string Key(string name) => $"{_prefix}/{name}";

    /// <inheritdoc />
    public Task<IReadOnlyList<RemoteFile>> ListAsync(CancellationToken ct) => AskAsync<IReadOnlyList<RemoteFile>>(async () =>
    {
        var files = new List<RemoteFile>();
        string? next = null;
        do
        {
            var page = await _s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _bucket, Prefix = $"{_prefix}/", ContinuationToken = next }, ct);
            foreach (var o in page.S3Objects ?? [])
            {
                var name = o.Key[(_prefix.Length + 1)..];
                // Only the folder's own files: none in folders inside it.
                if (name.Length > 0 && !name.Contains('/')) files.Add(new RemoteFile(name, o.Size ?? 0));
            }
            next = page.IsTruncated == true ? page.NextContinuationToken : null;
        }
        while (next is not null);
        return files;
    }, "list the folder", "s3:ListBucket", null);

    /// <inheritdoc />
    public Task<RemoteChecksum?> PutAsync(string name, Stream content, long length, CancellationToken ct) => AskAsync(async () =>
    {
        var put = new PutObjectRequest { BucketName = _bucket, Key = Key(name), InputStream = content, AutoCloseStream = false, IfNoneMatch = "*", StorageClass = _class };
        put.Headers.ContentLength = length;
        if (_sha256) put.ChecksumAlgorithm = ChecksumAlgorithm.SHA256;
        else
        {
            // Many services that answer as S3 does read neither a signed stream in pieces nor a trailing checksum.
            put.UseChunkEncoding = false;
            put.DisablePayloadSigning = true;
        }
        var answer = await _s3.PutObjectAsync(put, ct);
        return Sha256Of(answer.ChecksumSHA256);
    }, "write to the folder", "s3:PutObject", name);

    /// <inheritdoc />
    public Task<RemoteChecksum?> ChecksumAsync(string name, CancellationToken ct) => AskAsync(async () =>
    {
        var answer = await _s3.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = _bucket, Key = Key(name), ChecksumMode = ChecksumMode.ENABLED }, ct);
        return Sha256Of(answer.ChecksumSHA256);
    }, "read the folder", "s3:GetObject", name);

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(string name, CancellationToken ct)
        => AskAsync(async () => (await _s3.GetObjectAsync(new GetObjectRequest { BucketName = _bucket, Key = Key(name) }, ct)).ResponseStream, "read the folder", "s3:GetObject", name);

    /// <inheritdoc />
    public Task DeleteAsync(string name, CancellationToken ct)
        => AskAsync(async () => await _s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _bucket, Key = Key(name) }, ct), "delete in the folder", "s3:DeleteObject", name);

    /// <inheritdoc />
    public Task<RemoteSpace?> SpaceAsync(CancellationToken ct) => Task.FromResult<RemoteSpace?>(null);

    private static RemoteChecksum? Sha256Of(string? base64) => string.IsNullOrEmpty(base64) ? null : new RemoteChecksum(ChecksumKind.Sha256, Convert.ToHexStringLower(Convert.FromBase64String(base64)));

    // Asks the service, saying in plain words what went wrong and what to change.
    private async Task<T> AskAsync<T>(Func<Task<T>> ask, string doing, string permission, string? name)
    {
        try
        {
            return await ask();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound && ex.ErrorCode != "NoSuchBucket")
        {
            throw new FileNotFoundException($"{name} is not in {Where}.", name, ex);
        }
        catch (AmazonS3Exception ex)
        {
            throw new IOException(ex.ErrorCode switch
            {
                "NoSuchBucket" => $"There is no bucket named {_bucket}. Check its name and region.",
                "InvalidAccessKeyId" => "This access key ID is not known. Check it and try again.",
                "SignatureDoesNotMatch" => "The secret key does not match the access key ID. Check it and try again.",
                "AccessDenied" => $"This access key is not allowed to {doing}. Allow {permission} on {_bucket}/{_prefix}/* and try again.",
                "PreconditionFailed" => $"A file named {name} is already there, and Trovolve never replaces one.",
                _ => ex.Message,
            }, ex);
        }
        catch (AmazonClientException ex)
        {
            throw new IOException($"Trovolve could not reach the bucket {_bucket}: {ex.Message}", ex);
        }
    }
}
