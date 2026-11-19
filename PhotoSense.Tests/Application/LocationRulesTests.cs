using System.Security.Cryptography;
using PhotoSense.Application.Locations;
using PhotoSense.Domain.Services;

namespace PhotoSense.Tests.Application;

/// <summary>
/// What every location shares: the checksums worked out as a file is read once, the fields Connect to server asks for,
/// and what each kind of location is called.
/// </summary>
public class LocationRulesTests
{
    [Test]
    public async Task Reading_A_File_Once_Works_Out_Each_Checksum_A_Location_May_Keep(CancellationToken ct)
    {
        // Two of Dropbox's blocks and a little more, read in pieces of every size.
        var bytes = RandomNumberGenerator.GetBytes(HashingStream.DropboxBlock * 2 + 1000);
        using var hashing = new HashingStream(new MemoryStream(bytes));
        var buffer = new byte[3_000_000];
        await Assert.That(hashing.Read(buffer, 0, 10)).IsEqualTo(10);
        await Assert.That(await hashing.ReadAsync(buffer, 0, 100, ct)).IsEqualTo(100);
        await hashing.CopyToAsync(Stream.Null, ct);
        await Assert.That(hashing.Sha256).IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(bytes)));
        await Assert.That(hashing.Of(ChecksumKind.Md5)).IsEqualTo(Convert.ToHexStringLower(MD5.HashData(bytes)));
        await Assert.That(hashing.Of(ChecksumKind.Sha1)).IsEqualTo(Convert.ToHexStringLower(SHA1.HashData(bytes)));
        var blocks = bytes.Chunk(HashingStream.DropboxBlock).SelectMany(b => SHA256.HashData(b)).ToArray();
        await Assert.That(hashing.Of(ChecksumKind.DropboxContent)).IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(blocks)));
        await Assert.That(hashing.Matches(new RemoteChecksum(ChecksumKind.Md5, hashing.Of(ChecksumKind.Md5).ToUpperInvariant()))).IsTrue();
        await Assert.That(hashing.Matches(new RemoteChecksum(ChecksumKind.Sha1, "0"))).IsFalse();
    }

    [Test]
    public async Task An_Empty_File_Has_Each_Checksum_Of_Nothing_And_Dropbox_One_Of_No_Blocks(CancellationToken ct)
    {
        using var hashing = await HashingStream.OfAsync(new MemoryStream(), ct);
        await Assert.That(hashing.Sha256).IsEqualTo(Convert.ToHexStringLower(SHA256.HashData([])));
        await Assert.That(hashing.Of(ChecksumKind.DropboxContent)).IsEqualTo(Convert.ToHexStringLower(SHA256.HashData([])));
    }

    [Test]
    public async Task It_Only_Reads_And_Says_How_Far_It_Has_Read(CancellationToken ct)
    {
        using var hashing = new HashingStream(new MemoryStream(new byte[5]));
        await Assert.That((hashing.CanRead, hashing.CanSeek, hashing.CanWrite, hashing.Length)).IsEqualTo((true, false, false, 5L));
        await hashing.ReadExactlyAsync(new byte[3], ct);
        await Assert.That(hashing.Position).IsEqualTo(3L);
        hashing.Flush();
        await Assert.That(() => hashing.Position = 0).Throws<NotSupportedException>();
        await Assert.That(() => hashing.Seek(0, SeekOrigin.Begin)).Throws<NotSupportedException>();
        await Assert.That(() => hashing.SetLength(0)).Throws<NotSupportedException>();
        await Assert.That(() => hashing.Write([1], 0, 1)).Throws<NotSupportedException>();
    }

    [Test]
    public async Task Connect_To_Server_Asks_For_What_Each_Kind_Needs_And_Says_What_Is_Missing()
    {
        await Assert.That(ServerFields.Of(LocationKind.WebDav).Select(f => f.Key)).IsEquivalentTo(["url", "user", "pass", "folder"], CollectionOrdering.Matching);
        await Assert.That(ServerFields.Of(LocationKind.Sftp).Single(f => f.Key == "pass").Secret).IsTrue();
        await Assert.That(() => ServerFields.Of(LocationKind.Dropbox)).Throws<ArgumentException>();
        var none = new Dictionary<string, string>();
        await Assert.That(ServerFields.Missing(LocationKind.S3Compatible, none, none)).IsEqualTo("Enter the endpoint.");
        await Assert.That(ServerFields.Missing(LocationKind.AzureBlob, new Dictionary<string, string> { ["account"] = "a", ["container"] = "c" }, none)).IsEqualTo("Enter the key.");
        await Assert.That(ServerFields.Missing(LocationKind.AzureBlob, new Dictionary<string, string> { ["account"] = "a", ["container"] = "c" }, new Dictionary<string, string> { ["key"] = "k" })).IsNull();
        await Assert.That(ServerFields.Missing(LocationKind.Sftp, new Dictionary<string, string> { ["host"] = " " }, none)).IsEqualTo("Enter the host.");
    }

    [Test]
    public async Task Each_Kind_Of_Location_Is_Named_And_Known_As_The_Person_Sees_It()
    {
        await Assert.That(Enum.GetValues<LocationKind>().Select(LocationKinds.NameOf)).IsEquivalentTo(
            ["Computer", "Google Drive", "OneDrive", "Dropbox", "Box", "Amazon S3", "S3-compatible", "Azure Blob Storage", "WebDAV", "SFTP"], CollectionOrdering.Matching);
        await Assert.That(new[] { LocationKind.Computer }.Concat(LocationKinds.Cloud).Select(LocationKinds.IdOf)).IsEquivalentTo(["computer", "google-drive", "onedrive", "dropbox", "box"], CollectionOrdering.Matching);
        await Assert.That(() => LocationKinds.IdOf(LocationKind.Sftp)).Throws<ArgumentException>();
        await Assert.That((LocationKinds.IsCloud(LocationKind.Box), LocationKinds.IsServer(LocationKind.Box), LocationKinds.IsServer(LocationKind.WebDav))).IsEqualTo((true, false, true));
    }
}
