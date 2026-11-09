using System.Net;
using Moq;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Api.Endpoints;
using PhotoSense.Infrastructure.Persistence;
using PhotoSense.Tests.Application;

namespace PhotoSense.Tests.Endpoints;

/// <summary>The addresses of scanned photos: previews, pictures, videos, opening, keeping and removing them.</summary>
public sealed class PhotosEndpointsTests : IDisposable
{
    private const string UnknownId = "00000000-0000-0000-0000-000000000001";
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("photosense-api-");
    private readonly InMemoryPhotoRepository _repo = new();
    private readonly InMemoryThumbnailStore _thumbnails = new();
    private readonly Mock<IImageAnalyzer> _analyzer = new();
    private readonly Mock<ISystemViewer> _viewer = new();
    private readonly Mock<IDuplicateRemovalService> _remover = new();
    private readonly Mock<IAuditRepository> _audit = new();
    private readonly List<AuditEntry> _audited = [];
    private readonly PhotosEndpoints _api;

    public PhotosEndpointsTests()
    {
        _audit.Setup(a => a.AddAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>())).Callback<AuditEntry, CancellationToken>((e, _) => _audited.Add(e)).Returns(Task.CompletedTask);
        _api = Api(_repo);
    }

    public void Dispose()
    {
        _root.Delete(true);
    }

    private PhotosEndpoints Api(IPhotoRepository repo, IPhotoFileSystem? disk = null)
        => new(repo, _remover.Object, _thumbnails, _analyzer.Object, _audit.Object, Mock.Of<IScanLogSink>(), _viewer.Object, disk ?? TestFiles.Disk);

    // A real file with a record that matches it, as a scan would leave it.
    private async Task<Photo> AddAsync(string name, CancellationToken ct, string content = "picture bytes", string? contentHash = "ABCDEF", bool onDisk = true)
    {
        var path = Path.Combine(_root.FullName, name);
        if (onDisk) await File.WriteAllTextAsync(path, content);
        var photo = new Photo { SourcePath = path, FileName = name, FileSizeBytes = content.Length, ContentHash = contentHash, Format = Path.GetExtension(name).TrimStart('.').ToUpperInvariant(), Width = 4, Height = 3 };
        await _repo.AddOrUpdateAsync(photo, ct);
        return photo;
    }

    private static string Id(Photo photo) => photo.Id.Value.ToString();

    // ---- changes need the client header

    [Test]
    public async Task Requests_That_Show_Or_Change_Photos_Are_Refused_Without_The_Client_Header(CancellationToken ct)
    {
        var photo = await AddAsync("IMG_1.JPG", ct);
        var id = Id(photo);
        await Assert.That((await _api.DeletePhotoAsync(Http.Delete($"photos/{id}?physical=true"), id, ct)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await _api.KeepPhotoAsync(Http.Post($"photos/{id}/keep"), id, ct)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await _api.OpenPhotoAsync(Http.Post($"photos/{id}/open"), id, ct)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await _api.RemoveDuplicatesAsync(Http.Post("photos/bulk/remove-duplicates"), ct)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

        _remover.VerifyNoOtherCalls();
        _viewer.VerifyNoOtherCalls();
        await Assert.That((await _repo.GetAsync(photo.Id, ct))!.IsKept).IsFalse();
        await Assert.That(File.Exists(photo.SourcePath)).IsTrue();
        await Assert.That(_audited).IsEmpty();
    }

    // ---- thumbnails

    [Test]
    public async Task A_Thumbnail_Made_By_The_Scan_Is_Served_From_The_Cache(CancellationToken ct)
    {
        var photo = await AddAsync("IMG_1.HEIC", ct);
        _thumbnails.Saved["ABCDEF"] = [9, 8, 7];

        var response = await _api.GetThumbnailAsync(Http.Get("t"), Id(photo), ct);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Bytes()).IsEquivalentTo(new byte[] { 9, 8, 7 }, CollectionOrdering.Matching);
        await Assert.That((response.Header("Content-Type"), response.Header("ETag"))).IsEqualTo(("image/jpeg", "\"ABCDEF\""));
        // The same bytes always carry the same hash, so the browser may keep them.
        await Assert.That(response.Header("Cache-Control")).Contains("max-age=86400");
        await Assert.That(response.Header("Cache-Control")).Contains("private");
        _analyzer.VerifyNoOtherCalls();
    }

    [Test]
    public async Task A_Missing_Thumbnail_Is_Made_Again_From_The_File(CancellationToken ct)
    {
        var photo = await AddAsync("IMG_1.HEIC", ct);
        _analyzer.Setup(a => a.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageAnalysis(4, 3, "HEIC", null, 0, [], [1, 2]));

        var response = await _api.GetThumbnailAsync(Http.Get("t"), Id(photo), ct);

        await Assert.That(response.Bytes()).IsEquivalentTo(new byte[] { 1, 2 }, CollectionOrdering.Matching);
        await Assert.That(_thumbnails.Saved["ABCDEF"]).IsEquivalentTo(new byte[] { 1, 2 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task There_Is_No_Thumbnail_For_What_Cannot_Be_Shown(CancellationToken ct)
    {
        var unhashed = await AddAsync("empty.JPG", ct, contentHash: null);
        var video = await AddAsync("clip.MOV", ct);
        var gone = await AddAsync("gone.JPG", ct, contentHash: "AA", onDisk: false);
        var broken = await AddAsync("broken.JPG", ct, contentHash: "BB");
        _analyzer.Setup(a => a.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ThrowsAsync(new UnreadableImageException("not an image"));

        foreach (var id in new[] { UnknownId, Id(unhashed), Id(video), Id(gone), Id(broken) })
            await Assert.That((await _api.GetThumbnailAsync(Http.Get("t"), id, ct)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task A_Cancelled_Thumbnail_Request_Is_Not_Reported_As_A_Missing_Picture(CancellationToken ct)
    {
        var photo = await AddAsync("IMG_1.JPG", ct);
        _analyzer.Setup(a => a.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => _api.GetThumbnailAsync(Http.Get("t"), Id(photo), ct));
    }

    // ---- full pictures

    [Test]
    public async Task A_Picture_Browsers_Can_Show_Is_Sent_As_It_Is(CancellationToken ct)
    {
        var photo = await AddAsync("IMG_1.png", ct, "the png bytes");
        var response = await _api.GetImageAsync(Http.Get("i"), Id(photo), ct);
        await Assert.That((response.Text(), response.Header("Content-Type"), response.Header("ETag"))).IsEqualTo(("the png bytes", "image/png", "\"ABCDEF\""));

        var unhashed = await AddAsync("IMG_2.jpg", ct, "jpeg bytes", contentHash: null);
        var withoutTag = await _api.GetImageAsync(Http.Get("i"), Id(unhashed), ct);
        await Assert.That((withoutTag.Text(), withoutTag.Header("Content-Type"), withoutTag.Header("ETag"))).IsEqualTo(("jpeg bytes", "image/jpeg", (string?)null));
        _analyzer.VerifyNoOtherCalls();
    }

    [Test]
    public async Task A_Heic_Is_Converted_To_Jpeg_For_Viewing(CancellationToken ct)
    {
        var photo = await AddAsync("IMG_1.HEIC", ct);
        _analyzer.Setup(a => a.RenderJpegAsync(It.IsAny<Stream>(), 2560, It.IsAny<CancellationToken>())).ReturnsAsync([5, 5]);
        var response = await _api.GetImageAsync(Http.Get("i"), Id(photo), ct);
        await Assert.That((response.StatusCode, response.Header("Content-Type"))).IsEqualTo((HttpStatusCode.OK, "image/jpeg"));
        await Assert.That(response.Bytes()).IsEquivalentTo(new byte[] { 5, 5 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_Picture_That_Cannot_Be_Converted_Says_Why(CancellationToken ct)
    {
        var photo = await AddAsync("IMG_1.HEIC", ct);
        _analyzer.Setup(a => a.RenderJpegAsync(It.IsAny<Stream>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(new UnreadableImageException("corrupt tile"));
        var response = await _api.GetImageAsync(Http.Get("i"), Id(photo), ct);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.UnprocessableEntity);
        await Assert.That(response.Text()).Contains("corrupt tile");

        _analyzer.Setup(a => a.RenderJpegAsync(It.IsAny<Stream>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => _api.GetImageAsync(Http.Get("i"), Id(photo), ct));
    }

    [Test]
    public async Task There_Is_No_Picture_For_A_Video_Or_A_File_That_Is_Gone(CancellationToken ct)
    {
        var video = await AddAsync("clip.MOV", ct);
        var gone = await AddAsync("gone.JPG", ct, contentHash: "AA", onDisk: false);
        foreach (var id in new[] { UnknownId, Id(video), Id(gone) })
            await Assert.That((await _api.GetImageAsync(Http.Get("i"), id, ct)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    // ---- videos

    [Test]
    [Arguments("clip.MOV", "bytes=2-5", "2345", "bytes 2-5/10", "video/mp4")]
    [Arguments("clip.mp4", null, "0123456789", "bytes 0-9/10", "video/mp4")]
    [Arguments("clip.3gp", "bytes=-3", "789", "bytes 7-9/10", "video/3gpp")]
    public async Task A_Video_Is_Served_In_The_Pieces_The_Player_Asks_For(string name, string? range, string body, string contentRange, string contentType, CancellationToken ct)
    {
        var video = await AddAsync(name, ct, "0123456789");
        var request = Http.Get("v");
        if (range is not null) request.With("Range", range);

        var response = await _api.GetVideoAsync(request, Id(video), ct);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.PartialContent);
        await Assert.That((response.Text(), response.Header("Content-Range"), response.Header("Content-Type"), response.Header("Accept-Ranges"))).IsEqualTo((body, contentRange, contentType, "bytes"));
    }

    [Test]
    public async Task A_Piece_Beyond_The_End_Of_A_Video_Is_Refused(CancellationToken ct)
    {
        var video = await AddAsync("clip.MOV", ct, "0123456789");
        var response = await _api.GetVideoAsync(Http.Get("v").With("Range", "bytes=50-"), Id(video), ct);
        await Assert.That((response.StatusCode, response.Header("Content-Range"))).IsEqualTo((HttpStatusCode.RequestedRangeNotSatisfiable, "bytes */10"));
    }

    [Test]
    public async Task Only_A_Video_That_Is_Still_There_Can_Be_Played(CancellationToken ct)
    {
        var picture = await AddAsync("IMG_1.JPG", ct);
        var gone = await AddAsync("gone.MOV", ct, contentHash: "AA", onDisk: false);
        foreach (var id in new[] { UnknownId, Id(picture), Id(gone) })
            await Assert.That((await _api.GetVideoAsync(Http.Get("v"), id, ct)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    // ---- showing a file does not hold on to it

    [Test]
    public async Task A_File_Can_Be_Moved_Away_While_It_Is_Being_Shown(CancellationToken ct)
    {
        // A deletion moves the file. One that arrives while the page is still being sent the file must not
        // fail because of that: each of these is moved at the very moment the service has it open.
        var held = Directory.CreateDirectory(Path.Combine(_root.FullName, "held")).FullName;
        string Moved(Photo photo)
        {
            var to = Path.Combine(held, photo.FileName);
            File.Move(photo.SourcePath, to);
            return to;
        }

        var heic = await AddAsync("IMG_1.HEIC", ct, contentHash: "H1");
        _analyzer.Setup(a => a.RenderJpegAsync(It.IsAny<Stream>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Moved(heic); return Task.FromResult<byte[]>([5, 5]); });
        await Assert.That((await _api.GetImageAsync(Http.Get("i"), Id(heic), ct)).StatusCode).IsEqualTo(HttpStatusCode.OK);

        var uncached = await AddAsync("IMG_2.JPG", ct, contentHash: "H2");
        _analyzer.Setup(a => a.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Moved(uncached); return Task.FromResult(new ImageAnalysis(4, 3, "JPEG", null, 0, [], [1, 2])); });
        await Assert.That((await _api.GetThumbnailAsync(Http.Get("t"), Id(uncached), ct)).StatusCode).IsEqualTo(HttpStatusCode.OK);

        // A video is read a piece at a time, each read as short as the disk allows; the same opening serves it.
        var video = await AddAsync("clip.MOV", ct, "0123456789", contentHash: "H3");
        await using (var playing = TestFiles.Disk.OpenRead(video.SourcePath))
        {
            var to = Moved(video);
            await Assert.That(new StreamReader(playing).ReadToEnd()).IsEqualTo("0123456789");
            await Assert.That(File.Exists(to)).IsTrue();
        }

        await Assert.That(Directory.GetFiles(held).Select(f => Path.GetFileName(f)!).Order()).IsEquivalentTo(new[] { "IMG_1.HEIC", "IMG_2.JPG", "clip.MOV" }.Order(), CollectionOrdering.Matching);
        await Assert.That(new[] { heic, uncached, video }.Any(p => File.Exists(p.SourcePath))).IsFalse();
    }

    // ---- open in the system's viewer

    [Test]
    public async Task A_File_Is_Handed_To_The_Systems_Default_Viewer(CancellationToken ct)
    {
        var photo = await AddAsync("IMG_1.HEIC", ct);
        var response = await _api.OpenPhotoAsync(Http.Post("o").FromClient(), Id(photo), ct);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        _viewer.Verify(v => v.Open(photo.SourcePath), Times.Once);
    }

    [Test]
    public async Task Opening_Says_What_Went_Wrong(CancellationToken ct)
    {
        var photo = await AddAsync("IMG_1.HEIC", ct);
        await Assert.That((await _api.OpenPhotoAsync(Http.Post("o").FromClient(), UnknownId, ct)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        _viewer.Setup(v => v.Open(It.IsAny<string>())).Throws(new FileNotFoundException("gone"));
        var missing = await _api.OpenPhotoAsync(Http.Post("o").FromClient(), Id(photo), ct);
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(missing.Text()).Contains("no longer there");

        _viewer.Setup(v => v.Open(It.IsAny<string>())).Throws(new InvalidOperationException("No application could be started for IMG_1.HEIC"));
        var failed = await _api.OpenPhotoAsync(Http.Post("o").FromClient(), Id(photo), ct);
        await Assert.That(failed.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
        await Assert.That(failed.Text()).Contains("No application could be started");
    }

    // ---- remove one file

    [Test]
    public async Task Removing_A_File_Reports_Where_It_Went_And_What_Went_With_It(CancellationToken ct)
    {
        var id = Guid.NewGuid();
        _remover.Setup(r => r.RemoveAsync(new PhotoId(id), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemovalResult(RemovalOutcome.Removed, "held/IMG_1.HEIC") { Companions = ["a.MOV", "a.AAE"], BatchId = id });

        var response = await _api.DeletePhotoAsync(Http.Delete("p?physical=true").FromClient(), id.ToString(), ct);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var json = response.Json();
        await Assert.That((json.GetProperty("heldAt").GetString(), json.GetProperty("companions").GetInt32(), json.GetProperty("batchId").GetString())).IsEqualTo(("held/IMG_1.HEIC", 2, id.ToString()));
        var entry = _audited.Single();
        await Assert.That((entry.Action, entry.PhotoId, entry.Details)).IsEqualTo(("Delete", id.ToString(), "moved to held/IMG_1.HEIC with 2 linked files"));
    }

    [Test]
    public async Task Forgetting_A_Record_Leaves_The_File_Alone(CancellationToken ct)
    {
        var id = Guid.NewGuid();
        _remover.Setup(r => r.RemoveAsync(new PhotoId(id), false, It.IsAny<CancellationToken>())).ReturnsAsync(new RemovalResult(RemovalOutcome.Removed));
        var response = await _api.DeletePhotoAsync(Http.Delete("p").FromClient(), id.ToString(), ct);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        // No file moved, so there is nothing to undo.
        await Assert.That(response.Json().GetProperty("batchId").ValueKind).IsEqualTo(System.Text.Json.JsonValueKind.Null);
        await Assert.That(_audited.Single().Details).IsEqualTo("logical");
    }

    [Test]
    [Arguments(RemovalOutcome.NotFound, HttpStatusCode.NotFound, "")]
    [Arguments(RemovalOutcome.Changed, HttpStatusCode.Conflict, "changed since it was scanned")]
    [Arguments(RemovalOutcome.LastCopy, HttpStatusCode.Conflict, "this may be the only copy left")]
    [Arguments(RemovalOutcome.Failed, HttpStatusCode.Conflict, "in use by another process")]
    public async Task A_File_That_Was_Not_Removed_Says_Why(RemovalOutcome outcome, HttpStatusCode status, string message, CancellationToken ct)
    {
        _remover.Setup(r => r.RemoveAsync(It.IsAny<PhotoId>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemovalResult(outcome, Error: "in use by another process"));
        var response = await _api.DeletePhotoAsync(Http.Delete("p?physical=true").FromClient(), Guid.NewGuid().ToString(), ct);
        await Assert.That(response.StatusCode).IsEqualTo(status);
        await Assert.That(response.Text()).Contains(message);
        await Assert.That(_audited).IsEmpty();
    }

    // ---- keep

    [Test]
    public async Task A_Copy_Can_Be_Marked_Keep_And_Unmarked(CancellationToken ct)
    {
        var photo = await AddAsync("IMG_1.JPG", ct);
        var id = Id(photo);

        await Assert.That((await _api.KeepPhotoAsync(Http.Post("k").FromClient(), id, ct)).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That((await _repo.GetAsync(photo.Id, ct))!.IsKept).IsTrue();
        await _api.KeepPhotoAsync(Http.Post("k").FromClient(), id, ct); // already kept: nothing more to record
        await Assert.That(_audited.Single().Action).IsEqualTo("Keep");

        await Assert.That((await _api.KeepPhotoAsync(Http.Post("k?kept=false").FromClient(), id, ct)).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That((await _repo.GetAsync(photo.Id, ct))!.IsKept).IsFalse();
        await Assert.That(_audited.Select(a => a.Action)).IsEquivalentTo(new[] { "Keep", "Unkeep" }, CollectionOrdering.Matching);

        await Assert.That((await _api.KeepPhotoAsync(Http.Post("k").FromClient(), UnknownId, ct)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    // ---- remove duplicates in bulk

    [Test]
    [Arguments("b", null)]
    [Arguments("b?group=", null)]
    [Arguments("b?group=%20", null)]
    [Arguments("b?group=abc", "abc")]
    public async Task Duplicates_Are_Removed_For_One_Group_Or_For_All(string url, string? expectedGroup, CancellationToken ct)
    {
        _remover.Setup(r => r.RemoveDuplicatesAsync(expectedGroup, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BulkRemovalResult(3, 900, 1, ["one was left alone"], 2, Guid.Empty));

        var json = (await _api.RemoveDuplicatesAsync(Http.Post(url).FromClient(), ct)).Json();

        await Assert.That((json.GetProperty("removed").GetInt32(), json.GetProperty("bytes").GetInt64(), json.GetProperty("skipped").GetInt32(), json.GetProperty("companions").GetInt32())).IsEqualTo((3, 900L, 1, 2));
        await Assert.That(json.GetProperty("batchId").GetString()).IsEqualTo(Guid.Empty.ToString());
        await Assert.That(json.GetProperty("problems").EnumerateArray().Single().GetString()).IsEqualTo("one was left alone");
        await Assert.That((_audited[0].Action, _audited[0].PhotoId, _audited[0].Details)).IsEqualTo(("RemoveDuplicates", expectedGroup, "removed=3 bytes=900 linked=2 skipped=1"));
    }

    [Test]
    [Arguments("b?group=abc&mode=similar")]
    [Arguments("b?mode=SIMILAR&group=abc")]
    public async Task The_Similar_Shots_Of_A_Group_Are_Removed_When_The_Group_Is_Named(string url, CancellationToken ct)
    {
        _remover.Setup(r => r.RemoveDuplicatesAsync("abc", true, It.IsAny<CancellationToken>())).ReturnsAsync(new BulkRemovalResult(2, 600, 0, [], 0));

        var json = (await _api.RemoveDuplicatesAsync(Http.Post(url).FromClient(), ct)).Json();

        await Assert.That((json.GetProperty("removed").GetInt32(), json.GetProperty("bytes").GetInt64())).IsEqualTo((2, 600L));
        await Assert.That((_audited[0].Action, _audited[0].PhotoId)).IsEqualTo(("RemoveSimilar", "abc"));
    }

    [Test]
    [Arguments("b?group=abc&group=%20&group=def", false, "RemoveDuplicates")]
    [Arguments("b?group=abc&mode=similar&group=def", true, "RemoveSimilar")]
    public async Task Several_Groups_Named_At_Once_Are_Removed_Together(string url, bool similar, string action, CancellationToken ct)
    {
        _remover.Setup(r => r.RemoveGroupsAsync(It.Is<IReadOnlyCollection<string>>(g => g.SequenceEqual(new[] { "abc", "def" })), similar, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BulkRemovalResult(5, 1500, 0, [], 1));

        var json = (await _api.RemoveDuplicatesAsync(Http.Post(url).FromClient(), ct)).Json();

        await Assert.That((json.GetProperty("removed").GetInt32(), json.GetProperty("bytes").GetInt64(), json.GetProperty("companions").GetInt32())).IsEqualTo((5, 1500L, 1));
        await Assert.That((_audited[0].Action, _audited[0].PhotoId, _audited[0].Details)).IsEqualTo((action, (string?)null, "removed=5 bytes=1500 linked=1 skipped=0 groups=2"));
    }

    [Test]
    public async Task Many_Groups_Can_Be_Named_In_The_Body_And_Go_As_One_Change(CancellationToken ct)
    {
        _remover.Setup(r => r.RemoveGroupsAsync(It.Is<IReadOnlyCollection<string>>(g => g.SequenceEqual(new[] { "abc", "def", "ghi" })), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BulkRemovalResult(4, 1200, 0, [], 0, Guid.Empty));

        var json = (await _api.RemoveDuplicatesAsync(Http.Post("b?group=abc&mode=similar", """{"groups":["def"," ","ghi"]}""").FromClient(), ct)).Json();

        await Assert.That((json.GetProperty("removed").GetInt32(), json.GetProperty("batchId").GetString())).IsEqualTo((4, Guid.Empty.ToString()));
    }

    [Test]
    [Arguments("not the groups")]
    [Arguments("""{"group":["abc"]}""")]
    public async Task Groups_Named_In_A_Body_That_Cannot_Be_Read_Are_Refused(string body, CancellationToken ct)
    {
        var response = await _api.RemoveDuplicatesAsync(Http.Post("b", body).FromClient(), ct);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(response.Text()).IsEqualTo("The groups to remove could not be read.");
        _remover.VerifyNoOtherCalls();
    }

    [Test]
    [Arguments("b?mode=similar")]
    [Arguments("b?mode=similar&group=%20")]
    public async Task Similar_Shots_Are_Never_Removed_Without_Naming_A_Group(string url, CancellationToken ct)
    {
        var response = await _api.RemoveDuplicatesAsync(Http.Post(url).FromClient(), ct);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(response.Text()).Contains("name the group");
        _remover.VerifyNoOtherCalls();
        await Assert.That(_audited).IsEmpty();
    }
}
