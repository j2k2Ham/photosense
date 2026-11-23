namespace PhotoSense.Domain.Services;

/// <summary>The kinds of place a phone backs up to, each a location.</summary>
public enum LocationKind
{
    /// <summary>The computer the phone was paired with.</summary>
    Computer,
    /// <summary>Google Drive, in My Drive/PhotoSense.</summary>
    GoogleDrive,
    /// <summary>OneDrive, in its app folder, Apps/PhotoSense.</summary>
    OneDrive,
    /// <summary>Dropbox, in its app folder, Apps/PhotoSense.</summary>
    Dropbox,
    /// <summary>An Amazon S3 bucket.</summary>
    AmazonS3,
    /// <summary>A bucket on a service that answers as S3 does, at an address of its own.</summary>
    S3Compatible,
    /// <summary>An Azure Blob Storage container.</summary>
    AzureBlob,
    /// <summary>A WebDAV server, as many home NAS boxes have.</summary>
    WebDav,
    /// <summary>An SFTP server.</summary>
    Sftp,
}

/// <summary>What sort each kind of location is, and what it is called.</summary>
public static class LocationKinds
{
    /// <summary>The cloud services, which the person signs in to and which are always listed.</summary>
    public static readonly IReadOnlyList<LocationKind> Cloud = [LocationKind.GoogleDrive, LocationKind.OneDrive, LocationKind.Dropbox];

    /// <summary>What Connect to server adds, one at a time.</summary>
    public static readonly IReadOnlyList<LocationKind> Servers = [LocationKind.AmazonS3, LocationKind.S3Compatible, LocationKind.AzureBlob, LocationKind.WebDav, LocationKind.Sftp];

    /// <summary>A cloud service, signed in to.</summary>
    public static bool IsCloud(LocationKind kind) => Cloud.Contains(kind);

    /// <summary>A location Connect to server added.</summary>
    public static bool IsServer(LocationKind kind) => Servers.Contains(kind);

    /// <summary>What a kind of location is called: "Google Drive", "Amazon S3", "WebDAV".</summary>
    public static string NameOf(LocationKind kind) => kind switch
    {
        LocationKind.Computer => "Computer",
        LocationKind.GoogleDrive => "Google Drive",
        LocationKind.OneDrive => "OneDrive",
        LocationKind.Dropbox => "Dropbox",
        LocationKind.AmazonS3 => "Amazon S3",
        LocationKind.S3Compatible => "S3-compatible",
        LocationKind.AzureBlob => "Azure Blob Storage",
        LocationKind.WebDav => "WebDAV",
        _ => "SFTP",
    };

    /// <summary>The id a cloud service is always known by: "google-drive"; the computer's is "computer".</summary>
    public static string IdOf(LocationKind kind) => kind switch
    {
        LocationKind.Computer => "computer",
        LocationKind.GoogleDrive => "google-drive",
        LocationKind.OneDrive => "onedrive",
        LocationKind.Dropbox => "dropbox",
        _ => throw new ArgumentException("A location added by Connect to server has an id of its own.", nameof(kind)),
    };
}

/// <summary>A place the phone backs up to: the computer, a cloud service, or one added by Connect to server.</summary>
/// <param name="Id">How PhotoSense knows it: "computer", "google-drive", or an id of its own for one added.</param>
/// <param name="Kind">What sort of place it is.</param>
/// <param name="Name">What the person sees it called: the service's name, or the name they gave one they added.</param>
/// <param name="On">Backups go to it, and its checked copies count toward Safe to delete.</param>
/// <param name="Account">The account signed in to, or the user name, as the location shows it; null for none.</param>
/// <param name="Settings">How it is reached, less its secrets: the bucket, region, address, folder, port, user name.</param>
/// <param name="LastUsed">When it was last turned on or backed up to, which decides which stays on when Pro ends.</param>
/// <param name="LastBackup">When a backup to it last finished.</param>
/// <param name="LastChecked">When its copies were last checked.</param>
/// <param name="Problem">Why the last backup or check of it stopped; null when it did not.</param>
public sealed record BackupLocation(string Id, LocationKind Kind, string Name, bool On, string? Account, IReadOnlyDictionary<string, string> Settings,
    DateTimeOffset? LastUsed, DateTimeOffset? LastBackup, DateTimeOffset? LastChecked, string? Problem);

/// <summary>Where a phone keeps its locations, and which of its files each holds, checked.</summary>
public interface ILocationStore
{
    /// <summary>The locations kept, the cloud services among them once signed in to.</summary>
    Task<IReadOnlyList<BackupLocation>> ListAsync(CancellationToken ct);

    /// <summary>Keeps a location, in place of one with its id.</summary>
    Task SaveAsync(BackupLocation location, CancellationToken ct);

    /// <summary>Forgets a location, and which files it holds.</summary>
    Task RemoveAsync(string id, CancellationToken ct);

    /// <summary>The files a location holds, checked: each by its SHA-256 in lower-case hex.</summary>
    Task<IReadOnlyDictionary<string, LocationCopy>> CopiesAsync(string id, CancellationToken ct);

    /// <summary>Keeps that a location holds a file, checked.</summary>
    Task KeepCopyAsync(string id, string sha256, LocationCopy copy, CancellationToken ct);

    /// <summary>Forgets that a location holds a file, as a check found it missing or changed.</summary>
    Task ForgetCopyAsync(string id, string sha256, CancellationToken ct);

    /// <summary>On how many locations a copy is checked before a photo counts as backed up: 1 (any) or 2.</summary>
    Task<int> CountAtAsync(CancellationToken ct);

    /// <summary>Keeps on how many locations a copy is checked before a photo counts as backed up.</summary>
    Task SaveCountAtAsync(int locations, CancellationToken ct);
}

/// <summary>
/// Where a phone keeps a location's secrets: its keys and passwords, or the tokens a cloud service gave. The iPhone's
/// Keychain, or files sealed by the Android Keystore; they are only ever sent to the location itself.
/// </summary>
public interface ILocationSecrets
{
    /// <summary>A location's secrets; empty when none are kept.</summary>
    Task<IReadOnlyDictionary<string, string>> ReadAsync(string id, CancellationToken ct);

    /// <summary>Keeps a location's secrets, in place of those before.</summary>
    Task SaveAsync(string id, IReadOnlyDictionary<string, string> secrets, CancellationToken ct);

    /// <summary>Forgets a location's secrets.</summary>
    Task DeleteAsync(string id, CancellationToken ct);
}

/// <summary>The checksums a location may keep of what it holds.</summary>
public enum ChecksumKind
{
    /// <summary>SHA-256, as Amazon S3 and Google Drive keep it.</summary>
    Sha256,
    /// <summary>MD5, as Azure Blob Storage keeps it.</summary>
    Md5,
    /// <summary>Dropbox's own: the SHA-256 of the SHA-256s of each 4 MB of the file.</summary>
    DropboxContent,
}

/// <summary>A checksum a location keeps of a file it holds.</summary>
/// <param name="Kind">Which checksum.</param>
/// <param name="Value">Its value, in lower-case hex.</param>
public sealed record RemoteChecksum(ChecksumKind Kind, string Value);

/// <summary>A file a location holds, checked.</summary>
/// <param name="Name">Its name in the location's PhotoSense folder.</param>
/// <param name="Checksum">The location's own checksum of it, as it was when checked; null when the location keeps none and it was read back.</param>
public sealed record LocationCopy(string Name, RemoteChecksum? Checksum);

/// <summary>A file in a location's PhotoSense folder.</summary>
/// <param name="Name">Its name there.</param>
/// <param name="Size">Its size.</param>
public sealed record RemoteFile(string Name, long Size);

/// <summary>How much a location holds, as it says.</summary>
/// <param name="Total">All it can hold.</param>
/// <param name="Used">What is used, PhotoSense's among it.</param>
public sealed record RemoteSpace(long Total, long Used);

/// <summary>
/// The PhotoSense folder at a location, as the phone reaches it: what is in it, and putting, reading and deleting a file
/// there. A file put there under a name already taken would replace it, so the caller always chooses a free name.
/// </summary>
public interface IRemoteFolder
{
    /// <summary>Where the folder is, as the person would recognise it: "photos-archive/PhotoSense", "My Drive/PhotoSense".</summary>
    string Where { get; }

    /// <summary>The files in the folder.</summary>
    Task<IReadOnlyList<RemoteFile>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Puts a file in the folder under a name not taken there, and says the location's own checksum of what it now
    /// holds; null when the location keeps none, and what it holds can only be read back.
    /// </summary>
    Task<RemoteChecksum?> PutAsync(string name, Stream content, long length, CancellationToken ct);

    /// <summary>The location's own checksum of a file it holds; null when it keeps none; throws <see cref="FileNotFoundException"/> when the file is not there.</summary>
    Task<RemoteChecksum?> ChecksumAsync(string name, CancellationToken ct);

    /// <summary>Reads a file back; throws <see cref="FileNotFoundException"/> when it is not there.</summary>
    Task<Stream> OpenReadAsync(string name, CancellationToken ct);

    /// <summary>Deletes a file from the folder; one already gone is no error.</summary>
    Task DeleteAsync(string name, CancellationToken ct);

    /// <summary>How much the location holds; null when it does not say.</summary>
    Task<RemoteSpace?> SpaceAsync(CancellationToken ct);
}

/// <summary>Reaches a location's PhotoSense folder, with its secrets.</summary>
public interface IRemoteFolders
{
    /// <summary>The location's folder, reached with these secrets; a cloud service's renewed tokens are handed to <paramref name="renewed"/> to keep.</summary>
    IRemoteFolder Open(BackupLocation location, IReadOnlyDictionary<string, string> secrets, Func<IReadOnlyDictionary<string, string>, CancellationToken, Task> renewed);
}

/// <summary>Signing in to a cloud service begun: where the person signs in, and what proves the answer is to this sign-in.</summary>
/// <param name="Start">The service's own page, opened in the phone's browser.</param>
/// <param name="CallbackScheme">The scheme the service sends the person back to the app by.</param>
/// <param name="State">What the service sends back, to tie the answer to this sign-in.</param>
/// <param name="Verifier">The secret half of the sign-in's proof (PKCE), kept on the phone.</param>
public sealed record CloudSignInStart(Uri Start, string CallbackScheme, string State, string Verifier);

/// <summary>A cloud account signed in to.</summary>
/// <param name="Name">The account, as the person would recognise it: their email address or name.</param>
/// <param name="Secrets">The tokens the service gave, kept as the location's secrets.</param>
public sealed record CloudAccount(string Name, IReadOnlyDictionary<string, string> Secrets);

/// <summary>Signing in to the cloud services, each in the system's browser with the narrowest access it offers.</summary>
public interface ICloudAccounts
{
    /// <summary>PhotoSense is registered with the service, so that it can be signed in to.</summary>
    bool Available(LocationKind kind);

    /// <summary>Begins signing in to a service.</summary>
    CloudSignInStart Begin(LocationKind kind);

    /// <summary>Finishes signing in from where the service sent the person back.</summary>
    /// <exception cref="IOException">The service refused, or said something PhotoSense does not understand.</exception>
    Task<CloudAccount> FinishAsync(LocationKind kind, CloudSignInStart begun, Uri callback, CancellationToken ct);
}

/// <summary>The system's own sheet for signing in on a website: the person signs in there, and PhotoSense never sees the password.</summary>
public interface IWebSignIn
{
    /// <summary>Opens the page and waits for the service to send the person back; returns where it sent them.</summary>
    /// <exception cref="OperationCanceledException">The person closed the sheet.</exception>
    Task<Uri> SignInAsync(Uri start, string callbackScheme, CancellationToken ct);
}
