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
using Xunit;

namespace PhotoSense.Tests.Functions;

public sealed class PhotosFunctionsTests : IDisposable
{
    private const string UnknownId = "00000000-0000-0000-0000-000000000001";
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("photosense-api-");
    private readonly InMemoryPhotoRepository _repo = new();
    private readonly InMemoryThumbnailStore _thumbnails = new();
    private readonly Mock<IImageAnalyzer> _analyzer = new();
    private readonly Mock<ISystemViewer> _viewer = new();
    private readonly Mock<IPhotoDeletionService> _deleter = new();
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

    private PhotosFunctions Api(IPhotoRepository repo) => new(repo, new PhotoSearchService(repo), _deleter.Object, _remover.Object, _thumbnails,
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

    [Fact]
    public async Task Requests_That_Change_Anything_Are_Refused_Without_The_Client_Header()
    {
        var photo = await AddAsync("IMG_1.JPG");
        var id = Id(photo);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.GetAuditAsync(Http.Get("audit"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.DeletePhotoAsync(Http.Delete($"photos/{id}?physical=true"), id)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.KeepPhotoAsync(Http.Post($"photos/{id}/keep"), id)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.MovePhotoAsync(Http.Post($"photos/{id}/move?target={_root.FullName}"), id)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.OpenPhotoAsync(Http.Post($"photos/{id}/open"), id)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.RemoveDuplicatesAsync(Http.Post("photos/bulk/remove-duplicates"))).StatusCode);

        _deleter.VerifyNoOtherCalls();
        _remover.VerifyNoOtherCalls();
        _viewer.VerifyNoOtherCalls();
        Assert.False((await _repo.GetAsync(photo.Id))!.IsKept);
        Assert.True(File.Exists(photo.SourcePath));
        Assert.Empty(_audited);
    }

    // ---- listing

    [Fact]
    public async Task Audit_Lists_What_Was_Done()
    {
        _audited.Add(new AuditEntry { Action = "Keep", PhotoId = "p1", Details = "d" });
        var entry = Assert.Single((await _api.GetAuditAsync(Http.Get("audit").FromClient())).Json().EnumerateArray());
        Assert.Equal("Keep", entry.GetProperty("Action").GetString());
    }

    [Fact]
    public async Task Photos_Are_Listed_A_Page_At_A_Time_And_Can_Be_Filtered()
    {
        await AddAsync("cat.JPG", contentHash: "AA");
        await AddAsync("dog.JPG", contentHash: "BB");
        await AddAsync("cat2.JPG", contentHash: "CC");

        var all = (await _api.GetPhotosAsync(Http.Get("photos"))).Json();
        Assert.Equal((1, 50, 3), (all.GetProperty("Page").GetInt32(), all.GetProperty("PageSize").GetInt32(), all.GetProperty("TotalCount").GetInt32()));

        var cats = (await _api.GetPhotosAsync(Http.Get("photos?text=cat&page=2&pageSize=1&set=Unknown"))).Json();
        Assert.Equal((2, 1, 2, 2), (cats.GetProperty("Page").GetInt32(), cats.GetProperty("PageSize").GetInt32(), cats.GetProperty("TotalCount").GetInt32(), cats.GetProperty("TotalPages").GetInt32()));
        Assert.StartsWith("cat", Assert.Single(cats.GetProperty("items").EnumerateArray()).GetProperty("fileName").GetString());

        var byHash = (await _api.GetPhotosAsync(Http.Get("photos?hash=BB&phash=&page=x&pageSize=y"))).Json();
        Assert.Equal((1, 50, 1), (byHash.GetProperty("Page").GetInt32(), byHash.GetProperty("PageSize").GetInt32(), byHash.GetProperty("TotalCount").GetInt32()));

        var capped = (await _api.GetPhotosAsync(Http.Get("photos?pageSize=9999"))).Json();
        Assert.Equal(500, capped.GetProperty("PageSize").GetInt32());
    }

    [Fact]
    public async Task One_Photo_Can_Be_Fetched_By_Id()
    {
        var photo = await AddAsync("IMG_1.JPG");
        Assert.Equal("IMG_1.JPG", (await _api.GetPhotoAsync(Http.Get("photos/x"), Id(photo))).Json().GetProperty("fileName").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await _api.GetPhotoAsync(Http.Get("photos/x"), UnknownId)).StatusCode);
    }

    // ---- thumbnails

    [Fact]
    public async Task A_Thumbnail_Made_By_The_Scan_Is_Served_From_The_Cache()
    {
        var photo = await AddAsync("IMG_1.HEIC");
        _thumbnails.Saved["ABCDEF"] = [9, 8, 7];

        var response = await _api.GetThumbnailAsync(Http.Get("t"), Id(photo));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new byte[] { 9, 8, 7 }, response.Bytes());
        Assert.Equal(("image/jpeg", "\"ABCDEF\""), (response.Header("Content-Type"), response.Header("ETag")));
        // The same bytes always carry the same hash, so the browser may keep them.
        Assert.Contains("max-age=86400", response.Header("Cache-Control"));
        Assert.Contains("private", response.Header("Cache-Control"));
        _analyzer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_Missing_Thumbnail_Is_Made_Again_From_The_File()
    {
        var photo = await AddAsync("IMG_1.HEIC");
        _analyzer.Setup(a => a.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageAnalysis(4, 3, "HEIC", null, 0, [], [1, 2]));

        var response = await _api.GetThumbnailAsync(Http.Get("t"), Id(photo));

        Assert.Equal(new byte[] { 1, 2 }, response.Bytes());
        Assert.Equal(new byte[] { 1, 2 }, _thumbnails.Saved["ABCDEF"]);
    }

    [Fact]
    public async Task There_Is_No_Thumbnail_For_What_Cannot_Be_Shown()
    {
        var unhashed = await AddAsync("empty.JPG", contentHash: null);
        var video = await AddAsync("clip.MOV");
        var gone = await AddAsync("gone.JPG", contentHash: "AA", onDisk: false);
        var broken = await AddAsync("broken.JPG", contentHash: "BB");
        _analyzer.Setup(a => a.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidDataException("not an image"));

        foreach (var id in new[] { UnknownId, Id(unhashed), Id(video), Id(gone), Id(broken) })
            Assert.Equal(HttpStatusCode.NotFound, (await _api.GetThumbnailAsync(Http.Get("t"), id)).StatusCode);
    }

    [Fact]
    public async Task A_Cancelled_Thumbnail_Request_Is_Not_Reported_As_A_Missing_Picture()
    {
        var photo = await AddAsync("IMG_1.JPG");
        _analyzer.Setup(a => a.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());
        await Assert.ThrowsAsync<OperationCanceledException>(() => _api.GetThumbnailAsync(Http.Get("t"), Id(photo)));
    }

    // ---- full pictures

    [Fact]
    public async Task A_Picture_Browsers_Can_Show_Is_Sent_As_It_Is()
    {
        var photo = await AddAsync("IMG_1.png", "the png bytes");
        var response = await _api.GetImageAsync(Http.Get("i"), Id(photo));
        Assert.Equal(("the png bytes", "image/png", "\"ABCDEF\""), (response.Text(), response.Header("Content-Type"), response.Header("ETag")));

        var unhashed = await AddAsync("IMG_2.jpg", "jpeg bytes", contentHash: null);
        var withoutTag = await _api.GetImageAsync(Http.Get("i"), Id(unhashed));
        Assert.Equal(("jpeg bytes", "image/jpeg", (string?)null), (withoutTag.Text(), withoutTag.Header("Content-Type"), withoutTag.Header("ETag")));
        _analyzer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_Heic_Is_Converted_To_Jpeg_For_Viewing()
    {
        var photo = await AddAsync("IMG_1.HEIC");
        _analyzer.Setup(a => a.RenderJpegAsync(It.IsAny<Stream>(), 2560, It.IsAny<CancellationToken>())).ReturnsAsync([5, 5]);
        var response = await _api.GetImageAsync(Http.Get("i"), Id(photo));
        Assert.Equal((HttpStatusCode.OK, "image/jpeg"), (response.StatusCode, response.Header("Content-Type")));
        Assert.Equal(new byte[] { 5, 5 }, response.Bytes());
    }

    [Fact]
    public async Task A_Picture_That_Cannot_Be_Converted_Says_Why()
    {
        var photo = await AddAsync("IMG_1.HEIC");
        _analyzer.Setup(a => a.RenderJpegAsync(It.IsAny<Stream>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidDataException("corrupt tile"));
        var response = await _api.GetImageAsync(Http.Get("i"), Id(photo));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("corrupt tile", response.Text());

        _analyzer.Setup(a => a.RenderJpegAsync(It.IsAny<Stream>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());
        await Assert.ThrowsAsync<OperationCanceledException>(() => _api.GetImageAsync(Http.Get("i"), Id(photo)));
    }

    [Fact]
    public async Task There_Is_No_Picture_For_A_Video_Or_A_File_That_Is_Gone()
    {
        var video = await AddAsync("clip.MOV");
        var gone = await AddAsync("gone.JPG", contentHash: "AA", onDisk: false);
        foreach (var id in new[] { UnknownId, Id(video), Id(gone) })
            Assert.Equal(HttpStatusCode.NotFound, (await _api.GetImageAsync(Http.Get("i"), id)).StatusCode);
    }

    // ---- videos

    [Theory]
    [InlineData("clip.MOV", "bytes=2-5", "2345", "bytes 2-5/10", "video/mp4")]
    [InlineData("clip.mp4", null, "0123456789", "bytes 0-9/10", "video/mp4")]
    [InlineData("clip.3gp", "bytes=-3", "789", "bytes 7-9/10", "video/3gpp")]
    public async Task A_Video_Is_Served_In_The_Pieces_The_Player_Asks_For(string name, string? range, string body, string contentRange, string contentType)
    {
        var video = await AddAsync(name, "0123456789");
        var request = Http.Get("v");
        if (range is not null) request.With("Range", range);

        var response = await _api.GetVideoAsync(request, Id(video));

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal((body, contentRange, contentType, "bytes"), (response.Text(), response.Header("Content-Range"), response.Header("Content-Type"), response.Header("Accept-Ranges")));
    }

    [Fact]
    public async Task A_Piece_Beyond_The_End_Of_A_Video_Is_Refused()
    {
        var video = await AddAsync("clip.MOV", "0123456789");
        var response = await _api.GetVideoAsync(Http.Get("v").With("Range", "bytes=50-"), Id(video));
        Assert.Equal((HttpStatusCode.RequestedRangeNotSatisfiable, "bytes */10"), (response.StatusCode, response.Header("Content-Range")));
    }

    [Fact]
    public async Task Only_A_Video_That_Is_Still_There_Can_Be_Played()
    {
        var picture = await AddAsync("IMG_1.JPG");
        var gone = await AddAsync("gone.MOV", contentHash: "AA", onDisk: false);
        foreach (var id in new[] { UnknownId, Id(picture), Id(gone) })
            Assert.Equal(HttpStatusCode.NotFound, (await _api.GetVideoAsync(Http.Get("v"), id)).StatusCode);
    }

    // ---- open in the system's viewer

    [Fact]
    public async Task A_File_Is_Handed_To_The_Systems_Default_Viewer()
    {
        var photo = await AddAsync("IMG_1.HEIC");
        var response = await _api.OpenPhotoAsync(Http.Post("o").FromClient(), Id(photo));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        _viewer.Verify(v => v.Open(photo.SourcePath), Times.Once);
    }

    [Fact]
    public async Task Opening_Says_What_Went_Wrong()
    {
        var photo = await AddAsync("IMG_1.HEIC");
        Assert.Equal(HttpStatusCode.NotFound, (await _api.OpenPhotoAsync(Http.Post("o").FromClient(), UnknownId)).StatusCode);

        _viewer.Setup(v => v.Open(It.IsAny<string>())).Throws(new FileNotFoundException("gone"));
        var missing = await _api.OpenPhotoAsync(Http.Post("o").FromClient(), Id(photo));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Contains("no longer there", missing.Text());

        _viewer.Setup(v => v.Open(It.IsAny<string>())).Throws(new InvalidOperationException("No application could be started for IMG_1.HEIC"));
        var failed = await _api.OpenPhotoAsync(Http.Post("o").FromClient(), Id(photo));
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Contains("No application could be started", failed.Text());
    }

    // ---- remove one file

    [Fact]
    public async Task Removing_A_File_Reports_Where_It_Went_And_What_Went_With_It()
    {
        var id = Guid.NewGuid();
        _deleter.Setup(d => d.DeleteAsync(new PhotoId(id), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemovalResult(RemovalOutcome.Removed, "held/IMG_1.HEIC") { Companions = ["a.MOV", "a.AAE"] });

        var response = await _api.DeletePhotoAsync(Http.Delete("p?physical=true").FromClient(), id.ToString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("held/IMG_1.HEIC", 2), (response.Json().GetProperty("heldAt").GetString(), response.Json().GetProperty("companions").GetInt32()));
        var entry = Assert.Single(_audited);
        Assert.Equal(("Delete", id.ToString(), "moved to held/IMG_1.HEIC with 2 linked files"), (entry.Action, entry.PhotoId, entry.Details));
    }

    [Fact]
    public async Task Forgetting_A_Record_Leaves_The_File_Alone()
    {
        var id = Guid.NewGuid();
        _deleter.Setup(d => d.DeleteAsync(new PhotoId(id), false, It.IsAny<CancellationToken>())).ReturnsAsync(new RemovalResult(RemovalOutcome.Removed));
        var response = await _api.DeletePhotoAsync(Http.Delete("p").FromClient(), id.ToString());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("logical", Assert.Single(_audited).Details);
    }

    [Theory]
    [InlineData(RemovalOutcome.NotFound, HttpStatusCode.NotFound, "")]
    [InlineData(RemovalOutcome.Changed, HttpStatusCode.Conflict, "changed since it was scanned")]
    [InlineData(RemovalOutcome.Failed, HttpStatusCode.InternalServerError, "in use by another process")]
    public async Task A_File_That_Was_Not_Removed_Says_Why(RemovalOutcome outcome, HttpStatusCode status, string message)
    {
        _deleter.Setup(d => d.DeleteAsync(It.IsAny<PhotoId>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemovalResult(outcome, Error: "in use by another process"));
        var response = await _api.DeletePhotoAsync(Http.Delete("p?physical=true").FromClient(), Guid.NewGuid().ToString());
        Assert.Equal(status, response.StatusCode);
        Assert.Contains(message, response.Text());
        Assert.Empty(_audited);
    }

    // ---- keep

    [Fact]
    public async Task A_Copy_Can_Be_Marked_Keep_And_Unmarked()
    {
        var photo = await AddAsync("IMG_1.JPG");
        var id = Id(photo);

        Assert.Equal(HttpStatusCode.NoContent, (await _api.KeepPhotoAsync(Http.Post("k").FromClient(), id)).StatusCode);
        Assert.True((await _repo.GetAsync(photo.Id))!.IsKept);
        await _api.KeepPhotoAsync(Http.Post("k").FromClient(), id); // already kept: nothing more to record
        Assert.Equal("Keep", Assert.Single(_audited).Action);

        Assert.Equal(HttpStatusCode.NoContent, (await _api.KeepPhotoAsync(Http.Post("k?kept=false").FromClient(), id)).StatusCode);
        Assert.False((await _repo.GetAsync(photo.Id))!.IsKept);
        Assert.Equal(new[] { "Keep", "Unkeep" }, _audited.Select(a => a.Action));

        Assert.Equal(HttpStatusCode.NotFound, (await _api.KeepPhotoAsync(Http.Post("k").FromClient(), UnknownId)).StatusCode);
    }

    // ---- move

    [Fact]
    public async Task A_Photo_Can_Be_Moved_To_Another_Folder()
    {
        var photo = await AddAsync("IMG_1.JPG", "bytes");
        var target = _root.CreateSubdirectory("album").FullName;

        var response = await _api.MovePhotoAsync(Http.Post($"m?target={Uri.EscapeDataString(target)}").FromClient(), Id(photo));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var moved = Path.Combine(target, "IMG_1.JPG");
        Assert.True(File.Exists(moved) && !File.Exists(photo.SourcePath));
        Assert.Equal(moved, (await _repo.GetAsync(photo.Id))!.SourcePath);
        Assert.Equal(("Move", target), (Assert.Single(_audited).Action, _audited[0].Details));
    }

    [Fact]
    public async Task A_Move_Needs_A_Folder_That_Exists_And_A_Photo_That_Is_Recorded()
    {
        var photo = await AddAsync("IMG_1.JPG");
        var missingFolder = Path.Combine(_root.FullName, "no-such-folder");
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.MovePhotoAsync(Http.Post("m").FromClient(), Id(photo))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.MovePhotoAsync(Http.Post("m?target=%20").FromClient(), Id(photo))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.MovePhotoAsync(Http.Post($"m?target={Uri.EscapeDataString(missingFolder)}").FromClient(), Id(photo))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.MovePhotoAsync(Http.Post($"m?target={Uri.EscapeDataString(_root.FullName)}").FromClient(), UnknownId)).StatusCode);
        Assert.True(File.Exists(photo.SourcePath));
    }

    [Fact]
    public async Task A_Move_Never_Overwrites_A_File_Already_There()
    {
        var photo = await AddAsync("IMG_1.JPG", "mine");
        var target = _root.CreateSubdirectory("album").FullName;
        await File.WriteAllTextAsync(Path.Combine(target, "IMG_1.JPG"), "somebody else's");

        var response = await _api.MovePhotoAsync(Http.Post($"m?target={Uri.EscapeDataString(target)}").FromClient(), Id(photo));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotEmpty(response.Text());
        Assert.Equal("somebody else's", await File.ReadAllTextAsync(Path.Combine(target, "IMG_1.JPG")));
        Assert.Equal(photo.SourcePath, (await _repo.GetAsync(photo.Id))!.SourcePath);
        Assert.Empty(_audited);
    }

    [Fact]
    public async Task A_Move_Into_A_Folder_That_Refuses_It_Is_Reported()
    {
        var photo = await AddAsync("IMG_1.JPG", "mine");
        var target = _root.CreateSubdirectory("locked").FullName;
        using var denied = TestFiles.DenyAccess(target);
        if (denied is null) return; // cannot be arranged when running as root

        var response = await _api.MovePhotoAsync(Http.Post($"m?target={Uri.EscapeDataString(target)}").FromClient(), Id(photo));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(File.Exists(photo.SourcePath));
    }

    [Fact]
    public async Task A_Move_That_Fails_For_Another_Reason_Is_Not_Hidden()
    {
        // A record with no path cannot have come from a scan; the failure must surface rather than read as "conflict".
        var broken = new Photo { SourcePath = string.Empty, FileName = "IMG_1.JPG" };
        var repo = new Mock<IPhotoRepository>();
        repo.Setup(r => r.GetAsync(broken.Id, It.IsAny<CancellationToken>())).ReturnsAsync(broken);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Api(repo.Object).MovePhotoAsync(Http.Post($"m?target={Uri.EscapeDataString(_root.FullName)}").FromClient(), Id(broken)));
    }

    // ---- remove duplicates in bulk

    [Theory]
    [InlineData("b", null)]
    [InlineData("b?group=", null)]
    [InlineData("b?group=%20", null)]
    [InlineData("b?group=abc", "abc")]
    public async Task Duplicates_Are_Removed_For_One_Group_Or_For_All(string url, string? expectedGroup)
    {
        _remover.Setup(r => r.RemoveDuplicatesAsync(expectedGroup, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BulkRemovalResult(3, 900, 1, ["one was left alone"], 2));

        var json = (await _api.RemoveDuplicatesAsync(Http.Post(url).FromClient())).Json();

        Assert.Equal((3, 900L, 1, 2), (json.GetProperty("removed").GetInt32(), json.GetProperty("bytes").GetInt64(), json.GetProperty("skipped").GetInt32(), json.GetProperty("companions").GetInt32()));
        Assert.Equal("one was left alone", Assert.Single(json.GetProperty("problems").EnumerateArray()).GetString());
        Assert.Equal(("RemoveDuplicates", expectedGroup, "removed=3 bytes=900 linked=2 skipped=1"), (_audited[0].Action, _audited[0].PhotoId, _audited[0].Details));
    }
}
