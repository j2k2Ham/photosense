using System.Net;
using Moq;
using PhotoSense.Application.Photos.Services;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Functions.Api;
using PhotoSense.Functions.Scanning;
using PhotoSense.Infrastructure.Persistence;

namespace PhotoSense.Tests.Functions;

public sealed class PhotosFunctionsTests : IDisposable
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
    private readonly PhotosFunctions _api;

    public PhotosFunctionsTests()
    {
        _audit.Setup(a => a.AddAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>())).Callback<AuditEntry, CancellationToken>((e, _) => _audited.Add(e)).Returns(Task.CompletedTask);
        _audit.Setup(a => a.RecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _audited);
        _api = Api(_repo);
    }

    public void Dispose() => _root.Delete(true);

    private PhotosFunctions Api(IPhotoRepository repo) => new(repo, new PhotoSearchService(repo), _remover.Object, _thumbnails,
        _analyzer.Object, _audit.Object, Mock.Of<IScanLogSink>(), new PhotoDtoMapper(Mock.Of<IPlaceNameResolver>()), _viewer.Object);

    // A real file with a record that matches it, as a scan would leave it.
    private async Task<Photo> AddAsync(string name, string content = "picture bytes", string? contentHash = "ABCDEF", bool onDisk = true)
    {
        var path = Path.Combine(_root.FullName, name);
        if (onDisk) await File.WriteAllTextAsync(path, content);
        var photo = new Photo { SourcePath = path, FileName = name, FileSizeBytes = content.Length, ContentHash = contentHash, Format = Path.GetExtension(name).TrimStart('.').ToUpperInvariant(), Width = 4, Height = 3 };
        await _repo.AddOrUpdateAsync(photo);
        return photo;
    }

    private static string Id(Photo photo) => photo.Id.Value.ToString();

    // ---- changes need the client header

    [Test]
    public async Task Requests_That_Change_Anything_Are_Refused_Without_The_Client_Header()
    {
        var photo = await AddAsync("IMG_1.JPG");
        var id = Id(photo);
        await Assert.That((await _api.GetAuditAsync(Http.Get("audit"))).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await _api.DeletePhotoAsync(Http.Delete($"photos/{id}?physical=true"), id)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await _api.KeepPhotoAsync(Http.Post($"photos/{id}/keep"), id)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await _api.MovePhotoAsync(Http.Post($"photos/{id}/move?target={_root.FullName}"), id)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await _api.OpenPhotoAsync(Http.Post($"photos/{id}/open"), id)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await _api.RemoveDuplicatesAsync(Http.Post("photos/bulk/remove-duplicates"))).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

        _remover.VerifyNoOtherCalls();
        _viewer.VerifyNoOtherCalls();
        await Assert.That((await _repo.GetAsync(photo.Id))!.IsKept).IsFalse();
        await Assert.That(File.Exists(photo.SourcePath)).IsTrue();
        await Assert.That(_audited).IsEmpty();
    }

    // ---- listing

    [Test]
    public async Task Audit_Lists_What_Was_Done()
    {
        _audited.Add(new AuditEntry { Action = "Keep", PhotoId = "p1", Details = "d" });
        var entry = (await _api.GetAuditAsync(Http.Get("audit").FromClient())).Json().EnumerateArray().Single();
        await Assert.That(entry.GetProperty("Action").GetString()).IsEqualTo("Keep");
    }

    [Test]
    public async Task Photos_Are_Listed_A_Page_At_A_Time_And_Can_Be_Filtered()
    {
        await AddAsync("cat.JPG", contentHash: "AA");
        await AddAsync("dog.JPG", contentHash: "BB");
        await AddAsync("cat2.JPG", contentHash: "CC");

        var all = (await _api.GetPhotosAsync(Http.Get("photos"))).Json();
        await Assert.That((all.GetProperty("Page").GetInt32(), all.GetProperty("PageSize").GetInt32(), all.GetProperty("TotalCount").GetInt32())).IsEqualTo((1, 50, 3));

        var cats = (await _api.GetPhotosAsync(Http.Get("photos?text=cat&page=2&pageSize=1&set=Unknown"))).Json();
        await Assert.That((cats.GetProperty("Page").GetInt32(), cats.GetProperty("PageSize").GetInt32(), cats.GetProperty("TotalCount").GetInt32(), cats.GetProperty("TotalPages").GetInt32())).IsEqualTo((2, 1, 2, 2));
        await Assert.That(cats.GetProperty("items").EnumerateArray().Single().GetProperty("fileName").GetString()).StartsWith("cat");

        var byHash = (await _api.GetPhotosAsync(Http.Get("photos?hash=BB&phash=&page=x&pageSize=y"))).Json();
        await Assert.That((byHash.GetProperty("Page").GetInt32(), byHash.GetProperty("PageSize").GetInt32(), byHash.GetProperty("TotalCount").GetInt32())).IsEqualTo((1, 50, 1));

        var capped = (await _api.GetPhotosAsync(Http.Get("photos?pageSize=9999"))).Json();
        await Assert.That(capped.GetProperty("PageSize").GetInt32()).IsEqualTo(500);
    }

    [Test]
    public async Task One_Photo_Can_Be_Fetched_By_Id()
    {
        var photo = await AddAsync("IMG_1.JPG");
        await Assert.That((await _api.GetPhotoAsync(Http.Get("photos/x"), Id(photo))).Json().GetProperty("fileName").GetString()).IsEqualTo("IMG_1.JPG");
        await Assert.That((await _api.GetPhotoAsync(Http.Get("photos/x"), UnknownId)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    // ---- thumbnails

    [Test]
    public async Task A_Thumbnail_Made_By_The_Scan_Is_Served_From_The_Cache()
    {
        var photo = await AddAsync("IMG_1.HEIC");
        _thumbnails.Saved["ABCDEF"] = [9, 8, 7];

        var response = await _api.GetThumbnailAsync(Http.Get("t"), Id(photo));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Bytes()).IsEquivalentTo(new byte[] { 9, 8, 7 }, CollectionOrdering.Matching);
        await Assert.That((response.Header("Content-Type"), response.Header("ETag"))).IsEqualTo(("image/jpeg", "\"ABCDEF\""));
        // The same bytes always carry the same hash, so the browser may keep them.
        await Assert.That(response.Header("Cache-Control")).Contains("max-age=86400");
        await Assert.That(response.Header("Cache-Control")).Contains("private");
        _analyzer.VerifyNoOtherCalls();
    }

    [Test]
    public async Task A_Missing_Thumbnail_Is_Made_Again_From_The_File()
    {
        var photo = await AddAsync("IMG_1.HEIC");
        _analyzer.Setup(a => a.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageAnalysis(4, 3, "HEIC", null, 0, [], [1, 2]));

        var response = await _api.GetThumbnailAsync(Http.Get("t"), Id(photo));

        await Assert.That(response.Bytes()).IsEquivalentTo(new byte[] { 1, 2 }, CollectionOrdering.Matching);
        await Assert.That(_thumbnails.Saved["ABCDEF"]).IsEquivalentTo(new byte[] { 1, 2 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task There_Is_No_Thumbnail_For_What_Cannot_Be_Shown()
    {
        var unhashed = await AddAsync("empty.JPG", contentHash: null);
        var video = await AddAsync("clip.MOV");
        var gone = await AddAsync("gone.JPG", contentHash: "AA", onDisk: false);
        var broken = await AddAsync("broken.JPG", contentHash: "BB");
        _analyzer.Setup(a => a.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidDataException("not an image"));

        foreach (var id in new[] { UnknownId, Id(unhashed), Id(video), Id(gone), Id(broken) })
            await Assert.That((await _api.GetThumbnailAsync(Http.Get("t"), id)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task A_Cancelled_Thumbnail_Request_Is_Not_Reported_As_A_Missing_Picture()
    {
        var photo = await AddAsync("IMG_1.JPG");
        _analyzer.Setup(a => a.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => _api.GetThumbnailAsync(Http.Get("t"), Id(photo)));
    }

    // ---- full pictures

    [Test]
    public async Task A_Picture_Browsers_Can_Show_Is_Sent_As_It_Is()
    {
        var photo = await AddAsync("IMG_1.png", "the png bytes");
        var response = await _api.GetImageAsync(Http.Get("i"), Id(photo));
        await Assert.That((response.Text(), response.Header("Content-Type"), response.Header("ETag"))).IsEqualTo(("the png bytes", "image/png", "\"ABCDEF\""));

        var unhashed = await AddAsync("IMG_2.jpg", "jpeg bytes", contentHash: null);
        var withoutTag = await _api.GetImageAsync(Http.Get("i"), Id(unhashed));
        await Assert.That((withoutTag.Text(), withoutTag.Header("Content-Type"), withoutTag.Header("ETag"))).IsEqualTo(("jpeg bytes", "image/jpeg", (string?)null));
        _analyzer.VerifyNoOtherCalls();
    }

    [Test]
    public async Task A_Heic_Is_Converted_To_Jpeg_For_Viewing()
    {
        var photo = await AddAsync("IMG_1.HEIC");
        _analyzer.Setup(a => a.RenderJpegAsync(It.IsAny<Stream>(), 2560, It.IsAny<CancellationToken>())).ReturnsAsync([5, 5]);
        var response = await _api.GetImageAsync(Http.Get("i"), Id(photo));
        await Assert.That((response.StatusCode, response.Header("Content-Type"))).IsEqualTo((HttpStatusCode.OK, "image/jpeg"));
        await Assert.That(response.Bytes()).IsEquivalentTo(new byte[] { 5, 5 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_Picture_That_Cannot_Be_Converted_Says_Why()
    {
        var photo = await AddAsync("IMG_1.HEIC");
        _analyzer.Setup(a => a.RenderJpegAsync(It.IsAny<Stream>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidDataException("corrupt tile"));
        var response = await _api.GetImageAsync(Http.Get("i"), Id(photo));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.UnprocessableEntity);
        await Assert.That(response.Text()).Contains("corrupt tile");

        _analyzer.Setup(a => a.RenderJpegAsync(It.IsAny<Stream>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => _api.GetImageAsync(Http.Get("i"), Id(photo)));
    }

    [Test]
    public async Task There_Is_No_Picture_For_A_Video_Or_A_File_That_Is_Gone()
    {
        var video = await AddAsync("clip.MOV");
        var gone = await AddAsync("gone.JPG", contentHash: "AA", onDisk: false);
        foreach (var id in new[] { UnknownId, Id(video), Id(gone) })
            await Assert.That((await _api.GetImageAsync(Http.Get("i"), id)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    // ---- videos

    [Test]
    [Arguments("clip.MOV", "bytes=2-5", "2345", "bytes 2-5/10", "video/mp4")]
    [Arguments("clip.mp4", null, "0123456789", "bytes 0-9/10", "video/mp4")]
    [Arguments("clip.3gp", "bytes=-3", "789", "bytes 7-9/10", "video/3gpp")]
    public async Task A_Video_Is_Served_In_The_Pieces_The_Player_Asks_For(string name, string? range, string body, string contentRange, string contentType)
    {
        var video = await AddAsync(name, "0123456789");
        var request = Http.Get("v");
        if (range is not null) request.With("Range", range);

        var response = await _api.GetVideoAsync(request, Id(video));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.PartialContent);
        await Assert.That((response.Text(), response.Header("Content-Range"), response.Header("Content-Type"), response.Header("Accept-Ranges"))).IsEqualTo((body, contentRange, contentType, "bytes"));
    }

    [Test]
    public async Task A_Piece_Beyond_The_End_Of_A_Video_Is_Refused()
    {
        var video = await AddAsync("clip.MOV", "0123456789");
        var response = await _api.GetVideoAsync(Http.Get("v").With("Range", "bytes=50-"), Id(video));
        await Assert.That((response.StatusCode, response.Header("Content-Range"))).IsEqualTo((HttpStatusCode.RequestedRangeNotSatisfiable, "bytes */10"));
    }

    [Test]
    public async Task Only_A_Video_That_Is_Still_There_Can_Be_Played()
    {
        var picture = await AddAsync("IMG_1.JPG");
        var gone = await AddAsync("gone.MOV", contentHash: "AA", onDisk: false);
        foreach (var id in new[] { UnknownId, Id(picture), Id(gone) })
            await Assert.That((await _api.GetVideoAsync(Http.Get("v"), id)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    // ---- open in the system's viewer

    [Test]
    public async Task A_File_Is_Handed_To_The_Systems_Default_Viewer()
    {
        var photo = await AddAsync("IMG_1.HEIC");
        var response = await _api.OpenPhotoAsync(Http.Post("o").FromClient(), Id(photo));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        _viewer.Verify(v => v.Open(photo.SourcePath), Times.Once);
    }

    [Test]
    public async Task Opening_Says_What_Went_Wrong()
    {
        var photo = await AddAsync("IMG_1.HEIC");
        await Assert.That((await _api.OpenPhotoAsync(Http.Post("o").FromClient(), UnknownId)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        _viewer.Setup(v => v.Open(It.IsAny<string>())).Throws(new FileNotFoundException("gone"));
        var missing = await _api.OpenPhotoAsync(Http.Post("o").FromClient(), Id(photo));
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(missing.Text()).Contains("no longer there");

        _viewer.Setup(v => v.Open(It.IsAny<string>())).Throws(new InvalidOperationException("No application could be started for IMG_1.HEIC"));
        var failed = await _api.OpenPhotoAsync(Http.Post("o").FromClient(), Id(photo));
        await Assert.That(failed.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
        await Assert.That(failed.Text()).Contains("No application could be started");
    }

    // ---- remove one file

    [Test]
    public async Task Removing_A_File_Reports_Where_It_Went_And_What_Went_With_It()
    {
        var id = Guid.NewGuid();
        _remover.Setup(r => r.RemoveAsync(new PhotoId(id), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemovalResult(RemovalOutcome.Removed, "held/IMG_1.HEIC") { Companions = ["a.MOV", "a.AAE"] });

        var response = await _api.DeletePhotoAsync(Http.Delete("p?physical=true").FromClient(), id.ToString());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((response.Json().GetProperty("heldAt").GetString(), response.Json().GetProperty("companions").GetInt32())).IsEqualTo(("held/IMG_1.HEIC", 2));
        var entry = _audited.Single();
        await Assert.That((entry.Action, entry.PhotoId, entry.Details)).IsEqualTo(("Delete", id.ToString(), "moved to held/IMG_1.HEIC with 2 linked files"));
    }

    [Test]
    public async Task Forgetting_A_Record_Leaves_The_File_Alone()
    {
        var id = Guid.NewGuid();
        _remover.Setup(r => r.RemoveAsync(new PhotoId(id), false, It.IsAny<CancellationToken>())).ReturnsAsync(new RemovalResult(RemovalOutcome.Removed));
        var response = await _api.DeletePhotoAsync(Http.Delete("p").FromClient(), id.ToString());
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(_audited.Single().Details).IsEqualTo("logical");
    }

    [Test]
    [Arguments(RemovalOutcome.NotFound, HttpStatusCode.NotFound, "")]
    [Arguments(RemovalOutcome.Changed, HttpStatusCode.Conflict, "changed since it was scanned")]
    [Arguments(RemovalOutcome.LastCopy, HttpStatusCode.Conflict, "this may be the only copy left")]
    [Arguments(RemovalOutcome.Failed, HttpStatusCode.InternalServerError, "in use by another process")]
    public async Task A_File_That_Was_Not_Removed_Says_Why(RemovalOutcome outcome, HttpStatusCode status, string message)
    {
        _remover.Setup(r => r.RemoveAsync(It.IsAny<PhotoId>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemovalResult(outcome, Error: "in use by another process"));
        var response = await _api.DeletePhotoAsync(Http.Delete("p?physical=true").FromClient(), Guid.NewGuid().ToString());
        await Assert.That(response.StatusCode).IsEqualTo(status);
        await Assert.That(response.Text()).Contains(message);
        await Assert.That(_audited).IsEmpty();
    }

    // ---- keep

    [Test]
    public async Task A_Copy_Can_Be_Marked_Keep_And_Unmarked()
    {
        var photo = await AddAsync("IMG_1.JPG");
        var id = Id(photo);

        await Assert.That((await _api.KeepPhotoAsync(Http.Post("k").FromClient(), id)).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That((await _repo.GetAsync(photo.Id))!.IsKept).IsTrue();
        await _api.KeepPhotoAsync(Http.Post("k").FromClient(), id); // already kept: nothing more to record
        await Assert.That(_audited.Single().Action).IsEqualTo("Keep");

        await Assert.That((await _api.KeepPhotoAsync(Http.Post("k?kept=false").FromClient(), id)).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That((await _repo.GetAsync(photo.Id))!.IsKept).IsFalse();
        await Assert.That(_audited.Select(a => a.Action)).IsEquivalentTo(new[] { "Keep", "Unkeep" }, CollectionOrdering.Matching);

        await Assert.That((await _api.KeepPhotoAsync(Http.Post("k").FromClient(), UnknownId)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    // ---- move

    [Test]
    public async Task A_Photo_Can_Be_Moved_To_Another_Folder()
    {
        var photo = await AddAsync("IMG_1.JPG", "bytes");
        var target = _root.CreateSubdirectory("album").FullName;

        var response = await _api.MovePhotoAsync(Http.Post($"m?target={Uri.EscapeDataString(target)}").FromClient(), Id(photo));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        var moved = Path.Combine(target, "IMG_1.JPG");
        await Assert.That(File.Exists(moved) && !File.Exists(photo.SourcePath)).IsTrue();
        await Assert.That((await _repo.GetAsync(photo.Id))!.SourcePath).IsEqualTo(moved);
        await Assert.That((_audited.Single().Action, _audited[0].Details)).IsEqualTo(("Move", target));
    }

    [Test]
    public async Task A_Move_Needs_A_Folder_That_Exists_And_A_Photo_That_Is_Recorded()
    {
        var photo = await AddAsync("IMG_1.JPG");
        var missingFolder = Path.Combine(_root.FullName, "no-such-folder");
        await Assert.That((await _api.MovePhotoAsync(Http.Post("m").FromClient(), Id(photo))).StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await _api.MovePhotoAsync(Http.Post("m?target=%20").FromClient(), Id(photo))).StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await _api.MovePhotoAsync(Http.Post($"m?target={Uri.EscapeDataString(missingFolder)}").FromClient(), Id(photo))).StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await _api.MovePhotoAsync(Http.Post($"m?target={Uri.EscapeDataString(_root.FullName)}").FromClient(), UnknownId)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(File.Exists(photo.SourcePath)).IsTrue();
    }

    [Test]
    public async Task A_Move_Never_Overwrites_A_File_Already_There()
    {
        var photo = await AddAsync("IMG_1.JPG", "mine");
        var target = _root.CreateSubdirectory("album").FullName;
        await File.WriteAllTextAsync(Path.Combine(target, "IMG_1.JPG"), "somebody else's");

        var response = await _api.MovePhotoAsync(Http.Post($"m?target={Uri.EscapeDataString(target)}").FromClient(), Id(photo));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(response.Text()).IsNotEmpty();
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(target, "IMG_1.JPG"))).IsEqualTo("somebody else's");
        await Assert.That((await _repo.GetAsync(photo.Id))!.SourcePath).IsEqualTo(photo.SourcePath);
        await Assert.That(_audited).IsEmpty();
    }

    [Test]
    public async Task A_Move_Into_A_Folder_That_Refuses_It_Is_Reported()
    {
        var photo = await AddAsync("IMG_1.JPG", "mine");
        var target = _root.CreateSubdirectory("locked").FullName;
        using var denied = TestFiles.DenyAccess(target);
        if (denied is null) return; // cannot be arranged when running as root

        var response = await _api.MovePhotoAsync(Http.Post($"m?target={Uri.EscapeDataString(target)}").FromClient(), Id(photo));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(File.Exists(photo.SourcePath)).IsTrue();
    }

    [Test]
    public async Task A_Move_That_Fails_For_Another_Reason_Is_Not_Hidden()
    {
        // A record with no path cannot have come from a scan; the failure must surface rather than read as "conflict".
        var broken = new Photo { SourcePath = string.Empty, FileName = "IMG_1.JPG" };
        var repo = new Mock<IPhotoRepository>();
        repo.Setup(r => r.GetAsync(broken.Id, It.IsAny<CancellationToken>())).ReturnsAsync(broken);
        await Assert.ThrowsAsync<ArgumentException>(() => Api(repo.Object).MovePhotoAsync(Http.Post($"m?target={Uri.EscapeDataString(_root.FullName)}").FromClient(), Id(broken)));
    }

    // ---- remove duplicates in bulk

    [Test]
    [Arguments("b", null)]
    [Arguments("b?group=", null)]
    [Arguments("b?group=%20", null)]
    [Arguments("b?group=abc", "abc")]
    public async Task Duplicates_Are_Removed_For_One_Group_Or_For_All(string url, string? expectedGroup)
    {
        _remover.Setup(r => r.RemoveDuplicatesAsync(expectedGroup, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BulkRemovalResult(3, 900, 1, ["one was left alone"], 2));

        var json = (await _api.RemoveDuplicatesAsync(Http.Post(url).FromClient())).Json();

        await Assert.That((json.GetProperty("removed").GetInt32(), json.GetProperty("bytes").GetInt64(), json.GetProperty("skipped").GetInt32(), json.GetProperty("companions").GetInt32())).IsEqualTo((3, 900L, 1, 2));
        await Assert.That(json.GetProperty("problems").EnumerateArray().Single().GetString()).IsEqualTo("one was left alone");
        await Assert.That((_audited[0].Action, _audited[0].PhotoId, _audited[0].Details)).IsEqualTo(("RemoveDuplicates", expectedGroup, "removed=3 bytes=900 linked=2 skipped=1"));
    }

    [Test]
    [Arguments("b?group=abc&mode=similar")]
    [Arguments("b?mode=SIMILAR&group=abc")]
    public async Task The_Similar_Shots_Of_A_Group_Are_Removed_When_The_Group_Is_Named(string url)
    {
        _remover.Setup(r => r.RemoveDuplicatesAsync("abc", true, It.IsAny<CancellationToken>())).ReturnsAsync(new BulkRemovalResult(2, 600, 0, [], 0));

        var json = (await _api.RemoveDuplicatesAsync(Http.Post(url).FromClient())).Json();

        await Assert.That((json.GetProperty("removed").GetInt32(), json.GetProperty("bytes").GetInt64())).IsEqualTo((2, 600L));
        await Assert.That((_audited[0].Action, _audited[0].PhotoId)).IsEqualTo(("RemoveSimilar", "abc"));
    }

    [Test]
    [Arguments("b?mode=similar")]
    [Arguments("b?mode=similar&group=%20")]
    public async Task Similar_Shots_Are_Never_Removed_Without_Naming_A_Group(string url)
    {
        var response = await _api.RemoveDuplicatesAsync(Http.Post(url).FromClient());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(response.Text()).Contains("name the group");
        _remover.VerifyNoOtherCalls();
        await Assert.That(_audited).IsEmpty();
    }
}
