using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Azure.Storage;
using Azure.Storage.Blobs;
using Renci.SshNet;
using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Locations;

/// <summary>Reaches each kind of location's PhotoSense folder, with the settings and secrets it was connected with.</summary>
public sealed class RemoteFolders : IRemoteFolders
{
    private readonly CloudAccounts _accounts;
    private readonly HttpClient _http;
    private readonly TimeProvider _clock;

    /// <summary>Reaches the locations.</summary>
    /// <param name="accounts">Renews the cloud services' tokens.</param>
    /// <param name="http">Sends the requests of the cloud services and WebDAV.</param>
    /// <param name="clock">How long Box asks to be waited for.</param>
    public RemoteFolders(CloudAccounts accounts, HttpClient http, TimeProvider clock)
    {
        _accounts = accounts;
        _http = http;
        _clock = clock;
    }

    /// <inheritdoc />
    public IRemoteFolder Open(BackupLocation location, IReadOnlyDictionary<string, string> secrets, Func<IReadOnlyDictionary<string, string>, CancellationToken, Task> renewed)
    {
        var s = location.Settings;
        string Setting(string key, string otherwise = "") => s.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : otherwise;
        string Secret(string key) => secrets.GetValueOrDefault(key, "");
        var prefix = Setting("prefix", "PhotoSense").Trim('/');
        return location.Kind switch
        {
            LocationKind.GoogleDrive => new DriveFolder(_http, _accounts, secrets, renewed),
            LocationKind.OneDrive => new OneDriveFolder(_http, _accounts, secrets, renewed),
            LocationKind.Dropbox => new DropboxFolder(_http, _accounts, secrets, renewed),
            LocationKind.Box => new BoxFolder(_http, _accounts, secrets, renewed, (wait, ct) => Task.Delay(wait, _clock, ct)),
            // The region as the page offers it: "us-west-2 (Oregon)".
            LocationKind.AmazonS3 => new S3Folder(new AmazonS3Client(new BasicAWSCredentials(Setting("akid"), Secret("secret")),
                new AmazonS3Config { RegionEndpoint = RegionEndpoint.GetBySystemName(Setting("region", "us-east-1").Split(' ')[0]) }), Setting("bucket"), prefix, true, Setting("cls")),
            // A service that answers as S3 does: at its own address, by path, asked for no checksum it may not keep.
            LocationKind.S3Compatible => new S3Folder(new AmazonS3Client(new BasicAWSCredentials(Setting("akid"), Secret("secret")), new AmazonS3Config
            {
                ServiceURL = Setting("endpoint"), ForcePathStyle = true, AuthenticationRegion = Setting("region", "us-east-1"),
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED, ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            }), Setting("bucket"), prefix, false, null),
            LocationKind.AzureBlob => new AzureFolder(Container(Setting("account"), Setting("container"), Setting("auth", "SAS token"), Secret("key")), prefix, Setting("tier")),
            LocationKind.WebDav => new WebDavFolder(_http, new Uri(Setting("url")), Setting("folder", "/PhotoSense"), Setting("user"), Secret("pass")),
            LocationKind.Sftp => new SftpFolder(new SftpClient(Ssh(Setting("host"), int.TryParse(Setting("port"), out var port) ? port : 22, Setting("user"), Setting("auth", "Password"), Secret("pass"))),
                Setting("folder", "PhotoSense"), Setting("host")),
            _ => throw new ArgumentException("The computer is backed up to by PhotoSense on it, not as a folder.", nameof(location)),
        };
    }

    private static BlobContainerClient Container(string account, string container, string auth, string key)
    {
        var at = $"https://{account}.blob.core.windows.net/{container}";
        return auth == "Account key"
            ? new BlobContainerClient(new Uri(at), new StorageSharedKeyCredential(account, key))
            : new BlobContainerClient(new Uri($"{at}?{key.TrimStart('?')}"));
    }

    private static ConnectionInfo Ssh(string host, int port, string user, string auth, string secret)
        => new(host, port, user, auth == "SSH key"
            ? new PrivateKeyAuthenticationMethod(user, new PrivateKeyFile(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(secret))))
            : new PasswordAuthenticationMethod(user, secret));
}
