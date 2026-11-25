using Trovolve.Domain.Services;

namespace Trovolve.Application.Locations;

/// <summary>
/// What Connect to server asks for each kind of location, by the names the page and the locations' folders share: those
/// kept as its settings, those kept as secrets, and which must be filled in.
/// </summary>
public static class ServerFields
{
    /// <summary>A field of a kind of location.</summary>
    /// <param name="Key">Its name, as the page and the folder know it.</param>
    /// <param name="Label">What it is called on the page, for saying it is missing.</param>
    /// <param name="Secret">Kept as a secret, in the Keychain or the Android Keystore, and shown masked.</param>
    /// <param name="Required">It must be filled in.</param>
    public sealed record Field(string Key, string Label, bool Secret, bool Required);

    private static readonly Dictionary<LocationKind, Field[]> ByKind = new()
    {
        [LocationKind.AmazonS3] = [new("region", "Region", false, true), new("bucket", "Bucket", false, true), new("prefix", "Folder", false, false),
            new("akid", "Access key ID", false, true), new("secret", "Secret key", true, true), new("cls", "Storage class", false, false)],
        [LocationKind.S3Compatible] = [new("endpoint", "Endpoint", false, true), new("region", "Region", false, false), new("bucket", "Bucket", false, true),
            new("prefix", "Folder", false, false), new("akid", "Key ID", false, true), new("secret", "Secret key", true, true)],
        [LocationKind.AzureBlob] = [new("account", "Storage account", false, true), new("container", "Container", false, true), new("prefix", "Folder", false, false),
            new("auth", "Method", false, false), new("key", "Key", true, true), new("tier", "Access tier", false, false)],
        [LocationKind.WebDav] = [new("url", "Server URL", false, true), new("user", "User name", false, true), new("pass", "Password", true, true), new("folder", "Folder", false, false)],
        [LocationKind.Sftp] = [new("host", "Host", false, true), new("port", "Port", false, false), new("user", "User name", false, true), new("auth", "Sign in with", false, false),
            new("pass", "Password", true, true), new("folder", "Folder", false, false)],
    };

    /// <summary>The fields of a kind Connect to server adds.</summary>
    /// <exception cref="ArgumentException">The kind is not one Connect to server adds.</exception>
    public static IReadOnlyList<Field> Of(LocationKind kind) => ByKind.TryGetValue(kind, out var fields) ? fields : throw new ArgumentException($"{LocationKinds.NameOf(kind)} is not added by Connect to server.", nameof(kind));

    /// <summary>What the person left out that must be filled in, as the page should say it: "Enter the bucket."; null when nothing is.</summary>
    public static string? Missing(LocationKind kind, IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> secrets)
    {
        var field = Of(kind).FirstOrDefault(f => f.Required && string.IsNullOrWhiteSpace((f.Secret ? secrets : settings).GetValueOrDefault(f.Key)));
        return field is null ? null : $"Enter the {field.Label[..1].ToLowerInvariant()}{field.Label[1..]}.";
    }
}
