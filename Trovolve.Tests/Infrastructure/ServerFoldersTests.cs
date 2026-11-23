using System.Net;
using System.Net.Sockets;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Moq;
using Trovolve.Domain.Services;
using Trovolve.Infrastructure.Locations;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace Trovolve.Tests.Infrastructure;

/// <summary>
/// The folders Connect to server reaches: Amazon S3 and services like it, Azure Blob Storage, WebDAV and SFTP. Each lists,
/// puts without ever replacing a name, says the checksum it keeps, reads back and deletes, and says in plain words what
/// went wrong and what to change.
/// </summary>
public class ServerFoldersTests
{
    private static async Task<string> MessageAsync(Func<Task> act) => (await Assert.ThrowsAsync<IOException>(act))!.Message;

    [Test]
    public async Task S3_Lists_Only_Its_Folders_Own_Files_A_Page_At_A_Time(CancellationToken ct)
    {
        var s3 = new Mock<IAmazonS3>();
        s3.SetupSequence(x => x.ListObjectsV2Async(It.Is<ListObjectsV2Request>(r => r.BucketName == "photos" && r.Prefix == "Trovolve/"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListObjectsV2Response
            {
                S3Objects = [new S3Object { Key = "Trovolve/", Size = 0 }, new S3Object { Key = "Trovolve/IMG_1.JPG", Size = 5 }, new S3Object { Key = "Trovolve/old/IMG_2.JPG", Size = 1 }],
                IsTruncated = true, NextContinuationToken = "next",
            })
            .ReturnsAsync(new ListObjectsV2Response { S3Objects = [new S3Object { Key = "Trovolve/IMG_3.JPG" }], IsTruncated = false })
            .ReturnsAsync(new ListObjectsV2Response());
        var folder = new S3Folder(s3.Object, "photos", "Trovolve", true, null);
        await Assert.That(folder.Where).IsEqualTo("photos/Trovolve");
        await Assert.That(await folder.ListAsync(ct)).IsEquivalentTo([new RemoteFile("IMG_1.JPG", 5), new RemoteFile("IMG_3.JPG", 0)], CollectionOrdering.Matching);
        await Assert.That(await folder.ListAsync(ct)).IsEmpty();
        await Assert.That(await folder.SpaceAsync(ct)).IsNull();
    }

    [Test]
    public async Task S3_Puts_Only_Under_A_Free_Name_And_Says_The_SHA256_Amazon_Checked_While_A_Service_Like_It_Is_Asked_For_None(CancellationToken ct)
    {
        var s3 = new Mock<IAmazonS3>();
        PutObjectRequest? put = null;
        s3.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>())).Callback<PutObjectRequest, CancellationToken>((r, _) => put = r)
            .ReturnsAsync(new PutObjectResponse { ChecksumSHA256 = Convert.ToBase64String([0xab, 0xcd]) });
        var amazon = new S3Folder(s3.Object, "photos", "Trovolve", true, "Standard-IA");
        await Assert.That(await amazon.PutAsync("IMG_1.JPG", new MemoryStream([1]), 1, ct)).IsEqualTo(new RemoteChecksum(ChecksumKind.Sha256, "abcd"));
        await Assert.That((put!.Key, put.IfNoneMatch, put.ChecksumAlgorithm, put.StorageClass, put.Headers.ContentLength)).IsEqualTo(("Trovolve/IMG_1.JPG", "*", ChecksumAlgorithm.SHA256, S3StorageClass.StandardInfrequentAccess, 1L));
        await new S3Folder(s3.Object, "photos", "Trovolve", true, "Glacier Instant Retrieval").PutAsync("a", new MemoryStream(), 0, ct);
        await Assert.That(put.StorageClass).IsEqualTo(S3StorageClass.GlacierInstantRetrieval);
        await new S3Folder(s3.Object, "photos", "Trovolve", true, "Standard").PutAsync("a", new MemoryStream(), 0, ct);
        await Assert.That(put.StorageClass).IsEqualTo(S3StorageClass.Standard);

        s3.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>())).Callback<PutObjectRequest, CancellationToken>((r, _) => put = r).ReturnsAsync(new PutObjectResponse());
        var compatible = new S3Folder(s3.Object, "photos", "Trovolve", false, "Anything");
        await Assert.That(await compatible.PutAsync("IMG_1.JPG", new MemoryStream([1]), 1, ct)).IsNull();
        await Assert.That(put.ChecksumAlgorithm).IsNull();
        await Assert.That(put.StorageClass).IsNull();
        await Assert.That((put.UseChunkEncoding, put.DisablePayloadSigning)).IsEqualTo((false, (bool?)true));
    }

    [Test]
    public async Task S3_Says_A_Files_Checksum_Reads_It_Back_And_Deletes_It(CancellationToken ct)
    {
        var s3 = new Mock<IAmazonS3>();
        s3.SetupSequence(x => x.GetObjectMetadataAsync(It.Is<GetObjectMetadataRequest>(r => r.Key == "Trovolve/IMG_1.JPG" && r.ChecksumMode == ChecksumMode.ENABLED), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectMetadataResponse { ChecksumSHA256 = Convert.ToBase64String([1]) })
            .ReturnsAsync(new GetObjectMetadataResponse());
        s3.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new GetObjectResponse { ResponseStream = new MemoryStream([7]) });
        s3.Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new DeleteObjectResponse());
        var folder = new S3Folder(s3.Object, "photos", "Trovolve", true, null);
        await Assert.That(await folder.ChecksumAsync("IMG_1.JPG", ct)).IsEqualTo(new RemoteChecksum(ChecksumKind.Sha256, "01"));
        await Assert.That(await folder.ChecksumAsync("IMG_1.JPG", ct)).IsNull();
        await using var read = await folder.OpenReadAsync("IMG_1.JPG", ct);
        await Assert.That(read.ReadByte()).IsEqualTo(7);
        await folder.DeleteAsync("IMG_1.JPG", ct);
        s3.Verify(x => x.DeleteObjectAsync(It.Is<DeleteObjectRequest>(r => r.BucketName == "photos" && r.Key == "Trovolve/IMG_1.JPG"), It.IsAny<CancellationToken>()));
    }

    [Test]
    public async Task S3_Says_What_Went_Wrong_And_What_To_Change(CancellationToken ct)
    {
        var s3 = new Mock<IAmazonS3>();
        var folder = new S3Folder(s3.Object, "photos", "Trovolve", true, null);
        void Fails(string code, HttpStatusCode status = HttpStatusCode.BadRequest)
            => s3.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new AmazonS3Exception("Said no.") { ErrorCode = code, StatusCode = status });
        Task Put() => folder.PutAsync("IMG_1.JPG", new MemoryStream(), 0, ct);
        Fails("AccessDenied", HttpStatusCode.Forbidden);
        await Assert.That(await MessageAsync(Put)).IsEqualTo("This access key is not allowed to write to the folder. Allow s3:PutObject on photos/Trovolve/* and try again.");
        Fails("NoSuchBucket", HttpStatusCode.NotFound);
        await Assert.That(await MessageAsync(Put)).IsEqualTo("There is no bucket named photos. Check its name and region.");
        Fails("InvalidAccessKeyId");
        await Assert.That(await MessageAsync(Put)).IsEqualTo("This access key ID is not known. Check it and try again.");
        Fails("SignatureDoesNotMatch");
        await Assert.That(await MessageAsync(Put)).IsEqualTo("The secret key does not match the access key ID. Check it and try again.");
        Fails("PreconditionFailed", HttpStatusCode.PreconditionFailed);
        await Assert.That(await MessageAsync(Put)).IsEqualTo("A file named IMG_1.JPG is already there, and Trovolve never replaces one.");
        Fails("SlowDown");
        await Assert.That(await MessageAsync(Put)).IsEqualTo("Said no.");
        Fails("NoSuchKey", HttpStatusCode.NotFound);
        await Assert.ThrowsAsync<FileNotFoundException>(Put);
        s3.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new AmazonClientException("No route to host."));
        await Assert.That(await MessageAsync(Put)).IsEqualTo("Trovolve could not reach the bucket photos: No route to host.");
    }

    private static (Mock<BlobContainerClient> Container, Mock<BlobClient> Blob) Azure()
    {
        var container = new Mock<BlobContainerClient>();
        var blob = new Mock<BlobClient>();
        container.SetupGet(c => c.Name).Returns("photos");
        container.SetupGet(c => c.AccountName).Returns("mystorage");
        container.Setup(c => c.GetBlobClient(It.IsAny<string>())).Returns(blob.Object);
        return (container, blob);
    }

    private static Response<BlobProperties> Properties(byte[]? hash) => Response.FromValue(BlobsModelFactory.BlobProperties(contentHash: hash), Mock.Of<Response>());

    [Test]
    public async Task Azure_Lists_Puts_With_Its_MD5_Checked_And_Never_Over_A_Name_Reads_Back_And_Deletes(CancellationToken ct)
    {
        var (container, blob) = Azure();
        var listed = new[]
        {
            BlobsModelFactory.BlobItem("Trovolve/IMG_1.JPG", false, BlobsModelFactory.BlobItemProperties(false, contentLength: 5)),
            BlobsModelFactory.BlobItem("Trovolve/old/IMG_2.JPG", false, BlobsModelFactory.BlobItemProperties(false, contentLength: 1)),
            BlobsModelFactory.BlobItem("Trovolve/IMG_3.JPG", false, BlobsModelFactory.BlobItemProperties(false)),
        };
        container.Setup(c => c.GetBlobsAsync(It.Is<GetBlobsOptions>(o => o.Prefix == "Trovolve/"), It.IsAny<CancellationToken>()))
            .Returns(AsyncPageable<BlobItem>.FromPages([Page<BlobItem>.FromValues(listed, null, Mock.Of<Response>())]));
        BlobUploadOptions? options = null;
        blob.Setup(b => b.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, BlobUploadOptions, CancellationToken>((_, o, _) => options = o).ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());
        blob.SetupSequence(b => b.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Properties([0xab])).ReturnsAsync(Properties(null)).ReturnsAsync(Properties([])).ReturnsAsync(Properties([1]));
        blob.Setup(b => b.DownloadStreamingAsync(It.IsAny<BlobDownloadOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(BlobsModelFactory.BlobDownloadStreamingResult(new MemoryStream([7])), Mock.Of<Response>()));
        var folder = new AzureFolder(container.Object, "Trovolve", "Cool");
        await Assert.That(folder.Where).IsEqualTo("photos/Trovolve");
        await Assert.That(await folder.ListAsync(ct)).IsEquivalentTo([new RemoteFile("IMG_1.JPG", 5), new RemoteFile("IMG_3.JPG", 0)], CollectionOrdering.Matching);
        await Assert.That(await folder.PutAsync("IMG_1.JPG", new MemoryStream([1]), 1, ct)).IsEqualTo(new RemoteChecksum(ChecksumKind.Md5, "ab"));
        await Assert.That((options!.AccessTier, options.Conditions.IfNoneMatch, options.TransferValidation.ChecksumAlgorithm))
            .IsEqualTo(((AccessTier?)AccessTier.Cool, (ETag?)ETag.All, global::Azure.Storage.StorageChecksumAlgorithm.MD5));
        // A file put in parts has no MD5 of its own: it is read back.
        await Assert.That(await folder.PutAsync("IMG_2.JPG", new MemoryStream([1]), 1, ct)).IsNull();
        await Assert.That(await folder.ChecksumAsync("IMG_2.JPG", ct)).IsNull();
        await Assert.That(await folder.ChecksumAsync("IMG_1.JPG", ct)).IsEqualTo(new RemoteChecksum(ChecksumKind.Md5, "01"));
        await using (var read = await folder.OpenReadAsync("IMG_1.JPG", ct)) await Assert.That(read.ReadByte()).IsEqualTo(7);
        await folder.DeleteAsync("IMG_1.JPG", ct);
        blob.Verify(b => b.DeleteIfExistsAsync(It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()));
        await Assert.That(await folder.SpaceAsync(ct)).IsNull();
        container.Verify(c => c.GetBlobClient("Trovolve/IMG_1.JPG"));
        // Each tier as the page names it, or the account's own.
        blob.Setup(b => b.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>())).ReturnsAsync(Properties(null));
        foreach (var (tier, expected) in new[] { ("Hot", AccessTier.Hot), ("Cold", AccessTier.Cold) })
        {
            await new AzureFolder(container.Object, "Trovolve", tier).PutAsync("x", new MemoryStream(), 0, ct);
            await Assert.That(options.AccessTier).IsEqualTo(expected);
        }
        await new AzureFolder(container.Object, "Trovolve", null).PutAsync("x", new MemoryStream(), 0, ct);
        await Assert.That(options.AccessTier).IsNull();
    }

    [Test]
    public async Task Azure_Says_What_Went_Wrong_And_What_To_Change(CancellationToken ct)
    {
        var (container, blob) = Azure();
        var folder = new AzureFolder(container.Object, "Trovolve", null);
        void Fails(int status, string? code) => blob.Setup(b => b.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(status, "Said no.", code, null));
        Task Check() => folder.ChecksumAsync("IMG_1.JPG", ct);
        Fails(404, "BlobNotFound");
        await Assert.ThrowsAsync<FileNotFoundException>(Check);
        Fails(404, "ContainerNotFound");
        await Assert.That(await MessageAsync(Check)).IsEqualTo("There is no container named photos in mystorage. Check its name.");
        Fails(403, "AuthenticationFailed");
        await Assert.That(await MessageAsync(Check)).IsEqualTo("The key was not accepted. Check it and try again.");
        Fails(403, "AuthorizationPermissionMismatch");
        await Assert.That(await MessageAsync(Check)).IsEqualTo("This key is not allowed to read the container. Give it read, write, list and delete permissions on photos and try again.");
        Fails(403, "AuthorizationFailure");
        await Assert.That(await MessageAsync(Check)).StartsWith("This key is not allowed to read the container.");
        Fails(409, "BlobAlreadyExists");
        await Assert.That(await MessageAsync(Check)).IsEqualTo("A file named IMG_1.JPG is already there, and Trovolve never replaces one.");
        Fails(412, "ConditionNotMet");
        await Assert.That(await MessageAsync(Check)).StartsWith("A file named IMG_1.JPG");
        Fails(0, null);
        await Assert.That(await MessageAsync(Check)).IsEqualTo("Trovolve could not reach mystorage: Said no.");
        Fails(500, "InternalError");
        await Assert.That(await MessageAsync(Check)).IsEqualTo("Said no.");
    }

    private static (WebDavFolder Folder, FakeNetwork Network) WebDav(string folder = "/Photos/Trovolve")
    {
        var network = new FakeNetwork();
        return (new WebDavFolder(new HttpClient(network), new Uri("https://nas.local/dav"), folder, "alex", "secret"), network);
    }

    private const string Listing = """
        <?xml version="1.0"?>
        <d:multistatus xmlns:d="DAV:">
          <d:response><d:href>/dav/Photos/Trovolve/</d:href><d:propstat><d:prop><d:resourcetype><d:collection/></d:resourcetype></d:prop></d:propstat></d:response>
          <d:response><d:href>/dav/Photos/Trovolve/IMG%201.JPG</d:href><d:propstat><d:prop><d:getcontentlength>5</d:getcontentlength><d:resourcetype/></d:prop></d:propstat></d:response>
          <d:response><d:href>/dav/Photos/Trovolve/IMG_2.JPG</d:href><d:propstat><d:prop><d:resourcetype/></d:prop></d:propstat></d:response>
        </d:multistatus>
        """;

    [Test]
    public async Task WebDav_Lists_The_Folder_Signed_In_And_Makes_It_The_First_Time(CancellationToken ct)
    {
        var (folder, network) = WebDav();
        var propfind = new HttpMethod("PROPFIND");
        network.On(propfind, "https://nas.local/dav/Photos/Trovolve/", _ => FakeNetwork.Json(Listing, HttpStatusCode.MultiStatus));
        await Assert.That(folder.Where).IsEqualTo("/Photos/Trovolve");
        await Assert.That(await folder.ListAsync(ct)).IsEquivalentTo([new RemoteFile("IMG 1.JPG", 5), new RemoteFile("IMG_2.JPG", 0)], CollectionOrdering.Matching);
        var asked = network.Asked.Single();
        await Assert.That((asked.Headers["Depth"], asked.Headers["Authorization"])).IsEqualTo(("1", "Basic " + Convert.ToBase64String("alex:secret"u8.ToArray())));

        // Not there yet: made, each folder it is in too, one already there taken as it is.
        var (fresh, made) = WebDav();
        var mkcol = new HttpMethod("MKCOL");
        made.On(mkcol, "https://nas.local/dav/Photos/", _ => FakeNetwork.Status(HttpStatusCode.MethodNotAllowed))
            .On(mkcol, "https://nas.local/dav/Photos/Trovolve/", _ => FakeNetwork.Status(HttpStatusCode.Created));
        await Assert.That(await fresh.ListAsync(ct)).IsEmpty();
        await Assert.That(made.Asked.Where(h => h.Method == mkcol).Select(h => h.Url)).IsEquivalentTo(["https://nas.local/dav/Photos/", "https://nas.local/dav/Photos/Trovolve/"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task WebDav_Puts_Never_Over_A_Name_Keeps_No_Checksum_Reads_Back_Deletes_And_Says_How_Full_It_Is(CancellationToken ct)
    {
        var (folder, network) = WebDav();
        network.On(HttpMethod.Put, "https://nas.local/dav/Photos/Trovolve/", _ => FakeNetwork.Status(HttpStatusCode.Created))
            .On(HttpMethod.Head, "https://nas.local/dav/Photos/Trovolve/IMG_1.JPG", _ => FakeNetwork.Status(HttpStatusCode.OK))
            .On(HttpMethod.Get, "https://nas.local/dav/Photos/Trovolve/IMG_1.JPG", _ => FakeNetwork.Bytes([7]))
            .On(HttpMethod.Delete, "https://nas.local/dav/Photos/Trovolve/IMG_1.JPG", _ => FakeNetwork.Status(HttpStatusCode.NoContent))
            .On(new HttpMethod("PROPFIND"), "https://nas.local/dav/Photos/Trovolve/", h => h.Headers["Depth"] == "0"
                ? FakeNetwork.Json("""<d:multistatus xmlns:d="DAV:"><d:response><d:propstat><d:prop><d:quota-available-bytes>300</d:quota-available-bytes><d:quota-used-bytes>100</d:quota-used-bytes></d:prop></d:propstat></d:response></d:multistatus>""", HttpStatusCode.MultiStatus)
                : FakeNetwork.Status(HttpStatusCode.InternalServerError));
        await Assert.That(await folder.PutAsync("IMG 1.JPG", new MemoryStream([1, 2]), 2, ct)).IsNull();
        var put = network.Asked.Single();
        await Assert.That((put.Url, put.Headers["If-None-Match"], put.Headers["Content-Length"], put.Bytes.Length)).IsEqualTo(("https://nas.local/dav/Photos/Trovolve/IMG%201.JPG", "*", "2", 2));
        await Assert.That(await folder.ChecksumAsync("IMG_1.JPG", ct)).IsNull();
        await Assert.ThrowsAsync<FileNotFoundException>(() => folder.ChecksumAsync("gone.JPG", ct));
        await using (var read = await folder.OpenReadAsync("IMG_1.JPG", ct)) await Assert.That(read.ReadByte()).IsEqualTo(7);
        await folder.DeleteAsync("IMG_1.JPG", ct);
        await folder.DeleteAsync("gone.JPG", ct);
        await Assert.That(await folder.SpaceAsync(ct)).IsEqualTo(new RemoteSpace(400, 100));
        network.On(new HttpMethod("PROPFIND"), "https://nas.local/dav/", _ => FakeNetwork.Json("""<d:multistatus xmlns:d="DAV:"/>""", HttpStatusCode.MultiStatus));
        await Assert.That(await folder.SpaceAsync(ct)).IsNull();
        network.On(new HttpMethod("PROPFIND"), "https://nas.local/dav/", _ => FakeNetwork.Json("""<d:multistatus xmlns:d="DAV:"><d:quota-available-bytes>3</d:quota-available-bytes></d:multistatus>""", HttpStatusCode.MultiStatus));
        await Assert.That(await folder.SpaceAsync(ct)).IsNull();
    }

    [Test]
    public async Task WebDav_Says_What_Went_Wrong_And_What_To_Change(CancellationToken ct)
    {
        var (folder, network) = WebDav("Trovolve");
        Task Put() => folder.PutAsync("IMG_1.JPG", new MemoryStream(), 0, ct);
        foreach (var (status, says) in new[]
        {
            (HttpStatusCode.Unauthorized, "The user name or password was not accepted. Check them and try again."),
            (HttpStatusCode.Forbidden, "This account is not allowed to write to the folder, /Trovolve. Check its permissions and try again."),
            (HttpStatusCode.PreconditionFailed, "A file named IMG_1.JPG is already there, and Trovolve never replaces one."),
            (HttpStatusCode.InsufficientStorage, "nas.local is full."),
            (HttpStatusCode.BadGateway, "nas.local answered 502 BadGateway."),
        })
        {
            network.On(null, "https://", _ => FakeNetwork.Status(status));
            await Assert.That(await MessageAsync(Put)).IsEqualTo(says);
        }
        network.On(null, "https://", _ => FakeNetwork.Status(HttpStatusCode.NotFound));
        await Assert.ThrowsAsync<FileNotFoundException>(Put);
        network.Down = true;
        await Assert.That(await MessageAsync(Put)).IsEqualTo("Trovolve could not reach nas.local: No route to host.");
    }

    private static Mock<ISftpClient> Sftp(bool connected = true)
    {
        var sftp = new Mock<ISftpClient>();
        sftp.SetupGet(s => s.IsConnected).Returns(connected);
        return sftp;
    }

    private static async IAsyncEnumerable<ISftpFile> Files(params ISftpFile[] files)
    {
        await Task.Yield();
        foreach (var file in files) yield return file;
    }

    private static ISftpFile File(string name, bool regular, long length)
        => Mock.Of<ISftpFile>(f => f.Name == name && f.IsRegularFile == regular && f.Length == length);

    [Test]
    public async Task Sftp_Connects_Once_Lists_Only_Files_And_Makes_The_Folder_The_First_Time(CancellationToken ct)
    {
        var sftp = Sftp(connected: false);
        sftp.Setup(s => s.ExistsAsync("/home/alex/Trovolve", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        sftp.Setup(s => s.ListDirectoryAsync("/home/alex/Trovolve", It.IsAny<CancellationToken>())).Returns(Files(File(".", false, 0), File("IMG_1.JPG", true, 5)));
        using (var folder = new SftpFolder(sftp.Object, "/home/alex/Trovolve/", "nas.local"))
        {
            await Assert.That(folder.Where).IsEqualTo("/home/alex/Trovolve");
            await Assert.That(await folder.ListAsync(ct)).IsEquivalentTo([new RemoteFile("IMG_1.JPG", 5)]);
            await Assert.That(await folder.SpaceAsync(ct)).IsNull();
        }
        sftp.Verify(s => s.ConnectAsync(It.IsAny<CancellationToken>()));
        sftp.Verify(s => s.Dispose());

        // In the home folder, made with each folder it is in that is not there yet.
        var fresh = Sftp();
        fresh.Setup(s => s.ExistsAsync("./Photos", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        using var made = new SftpFolder(fresh.Object, "Photos/Trovolve", "nas.local");
        await Assert.That(await made.ListAsync(ct)).IsEmpty();
        fresh.Verify(s => s.CreateDirectoryAsync("./Photos/Trovolve", It.IsAny<CancellationToken>()));
        fresh.Verify(s => s.CreateDirectoryAsync("./Photos", It.IsAny<CancellationToken>()), Times.Never);
        fresh.Verify(s => s.ConnectAsync(It.IsAny<CancellationToken>()), Times.Never);
        // An absolute folder, made from the top.
        var top = Sftp();
        using var rooted = new SftpFolder(top.Object, "/srv/photos", "nas.local");
        await Assert.That(await rooted.ListAsync(ct)).IsEmpty();
        top.Verify(s => s.CreateDirectoryAsync("/srv", It.IsAny<CancellationToken>()));
        top.Verify(s => s.CreateDirectoryAsync("/srv/photos", It.IsAny<CancellationToken>()));
    }

    [Test]
    public async Task Sftp_Puts_Never_Over_A_Name_Reads_Back_Through_A_File_Of_Its_Own_And_Deletes(CancellationToken ct)
    {
        var sftp = Sftp();
        sftp.Setup(s => s.ExistsAsync("Trovolve/IMG_1.JPG", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        sftp.Setup(s => s.DownloadFileAsync("Trovolve/IMG_1.JPG", It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<string, Stream, CancellationToken>((_, output, token) => output.WriteAsync(new byte[] { 7, 8 }, token).AsTask());
        sftp.Setup(s => s.DownloadFileAsync("Trovolve/gone.JPG", It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ThrowsAsync(new SftpPathNotFoundException("No such file"));
        using var folder = new SftpFolder(sftp.Object, "Trovolve", "nas.local");
        await Assert.That(await folder.PutAsync("IMG_1.JPG", new MemoryStream([1]), 1, ct)).IsNull();
        sftp.Verify(s => s.UploadFileAsync(It.IsAny<Stream>(), "Trovolve/IMG_1.JPG", false, null, It.IsAny<CancellationToken>()));
        await Assert.That(await folder.ChecksumAsync("IMG_1.JPG", ct)).IsNull();
        await Assert.ThrowsAsync<FileNotFoundException>(() => folder.ChecksumAsync("gone.JPG", ct));
        await using (var read = await folder.OpenReadAsync("IMG_1.JPG", ct))
        {
            var bytes = new MemoryStream();
            await read.CopyToAsync(bytes, ct);
            await Assert.That(bytes.ToArray()).IsEquivalentTo(new byte[] { 7, 8 });
        }
        await Assert.ThrowsAsync<FileNotFoundException>(() => folder.OpenReadAsync("gone.JPG", ct));
        await folder.DeleteAsync("IMG_1.JPG", ct);
        await folder.DeleteAsync("gone.JPG", ct);
        sftp.Verify(s => s.DeleteFileAsync("Trovolve/IMG_1.JPG", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Sftp_Says_What_Went_Wrong_And_What_To_Change(CancellationToken ct)
    {
        var sftp = Sftp();
        using var folder = new SftpFolder(sftp.Object, "Trovolve", "nas.local");
        void Fails(Exception failure) => sftp.Setup(s => s.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), false, null, It.IsAny<CancellationToken>())).ThrowsAsync(failure);
        Task Put() => folder.PutAsync("IMG_1.JPG", new MemoryStream(), 0, ct);
        Fails(new SshAuthenticationException("Denied."));
        await Assert.That(await MessageAsync(Put)).IsEqualTo("The user name, password or key was not accepted. Check them and try again.");
        Fails(new SftpPermissionDeniedException("Denied."));
        await Assert.That(await MessageAsync(Put)).IsEqualTo("This account is not allowed to write to Trovolve. Check its permissions and try again.");
        foreach (Exception down in new Exception[] { new SocketException(), new SshConnectionException("Lost."), new SshOperationTimeoutException("Slow.") })
        {
            Fails(down);
            await Assert.That(await MessageAsync(Put)).StartsWith("Trovolve could not reach nas.local:");
        }
        Fails(new SshException("The file already exists."));
        await Assert.That(await MessageAsync(Put)).IsEqualTo("A file named IMG_1.JPG is already there, and Trovolve never replaces one.");
        Fails(new SshException("Something else."));
        await Assert.That(await MessageAsync(Put)).IsEqualTo("Something else.");
    }
}
