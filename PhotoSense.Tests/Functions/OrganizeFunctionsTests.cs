using System.Net;
using System.Text.Json;
using Moq;
using PhotoSense.Application.Organizing;
using PhotoSense.Domain.Services;
using PhotoSense.Functions.Api;
using PhotoSense.Tests.Application;

namespace PhotoSense.Tests.Functions;

public sealed class OrganizeFunctionsTests : IDisposable
{
    private readonly OrganizeFixture _f = new();
    private readonly InMemoryThumbnailStore _thumbnails = new();
    private readonly Mock<IImageAnalyzer> _analyzer = new();
    private readonly Mock<ISystemViewer> _viewer = new();
    private readonly OrganizeFunctions _api;

    public OrganizeFunctionsTests()
        => _api = new OrganizeFunctions(_f.Library, _f.Planner, _f.Mover(), _f.Batches, new OrganizeListingMapper(_f.Places), _thumbnails, _analyzer.Object, _viewer.Object);

    public void Dispose() => _f.Dispose();

    private static string Body(object request) => JsonSerializer.Serialize(request);
    private static FakeHttpRequest Files(string? root) => Http.Get($"organize/files?root={Uri.EscapeDataString(root ?? string.Empty)}").FromClient();

    // ---- only the UI itself is answered

    [Test]
    public async Task Requests_About_Someones_Files_Are_Refused_Without_The_Client_Header()
    {
        var id = await _f.IdAsync(_f.Put("IMG_1.JPG"));
        await Assert.That((await _api.FilesAsync(Http.Get($"organize/files?root={_f.Root.FullName}"))).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await _api.PlanAsync(Http.Post("organize/plan", "{}"))).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await _api.ApplyAsync(Http.Post("organize/apply", "{}"))).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await _api.RemoveAsync(Http.Post("organize/remove", Body(new { root = _f.Root.FullName, files = new[] { new { id } } })))).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await _api.UndoAsync(Http.Post("organize/undo/x"), "x")).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await _api.BatchesAsync(Http.Get("organize/batches"))).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await _api.OpenAsync(Http.Post("organize/files/x/open"), id)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        _viewer.VerifyNoOtherCalls();
        await Assert.That(_f.Files()).IsEquivalentTo(new[] { "IMG_1.JPG" }, CollectionOrdering.Matching);
    }

    // ---- listing

    [Test]
    public async Task Lists_A_Folders_Files_With_When_And_Where_Each_Was_Taken()
    {
        _f.Put("IMG_1.JPG", year: 2021);
        _f.Put("2023/IMG_2.HEIC", year: 2023);
        _f.Put("2023/IMG_3.HEIC", year: 2022);
        _f.Put("clip.MOV", year: 2020);
        _f.Reader.Says["IMG_2.HEIC"] = new MediaDetails(new DateTime(2023, 12, 31, 14, 3, 22), 4032, 3024, null, 46.0138, -112.5362);
        _f.Reader.Says["IMG_3.HEIC"] = new MediaDetails(null, 0, 0, null, 46.0139, -112.5361);
        _f.Reader.Says["IMG_1.JPG"] = new MediaDetails(null, 0, 0, null, 18.5, -68.4);
        _f.Reader.Says["clip.MOV"] = new MediaDetails(null, 1920, 1080, 84, 0.5, 0.5);
        _f.Resolver.Setup(r => r.Locate(It.IsInRange(46.0, 46.1, Moq.Range.Inclusive), It.IsAny<double>()))
            .Returns(new PlaceMatch("Butte", "Montana", "US", 46.0038, -112.5348, "Uptown Butte", 46.0138, -112.5362));
        _f.Resolver.Setup(r => r.Locate(18.5, -68.4)).Returns(new PlaceMatch("Punta Cana", "", "DO", 18.58, -68.4));

        var response = await _api.FilesAsync(Files(_f.Root.FullName));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var listing = response.Json();
        await Assert.That((listing.GetProperty("root").GetString(), listing.GetProperty("fromScan").GetBoolean())).IsEqualTo((_f.Root.FullName, false));
        var places = listing.GetProperty("places").EnumerateArray().ToList();
        // A landmark is a place of its own, at the landmark; a town with no region is given its country for one.
        await Assert.That(places.Select(p => (p.GetProperty("town").GetString()!, p.GetProperty("state").GetString()!, p.GetProperty("country").GetString()!, p.GetProperty("area").GetString(), p.GetProperty("latitude").GetDouble(), p.GetProperty("longitude").GetDouble())))
            .IsEquivalentTo(new[] { ("Butte", "Montana", "US", "Uptown Butte", 46.0138, -112.5362), ("Punta Cana", "DO", "DO", (string?)null, 18.58, -68.4) }, CollectionOrdering.Matching);

        var files = listing.GetProperty("files").EnumerateArray().ToList();
        await Assert.That(files.Select(f => (f.GetProperty("name").GetString(), f.GetProperty("date").GetString(), f.GetProperty("dateFromFile").GetBoolean(), f.GetProperty("place").ValueKind == JsonValueKind.Null ? (int?)null : f.GetProperty("place").GetInt32())))
            .IsEquivalentTo(new (string?, string?, bool, int?)[]
            {
                ("IMG_2.HEIC", "2023-12-31T14:03:22", false, 0), ("IMG_3.HEIC", "2022-01-01T12:00:00", true, 0), ("IMG_1.JPG", "2021-01-01T12:00:00", true, 1), ("clip.MOV", "2020-01-01T12:00:00", true, null),
            }, CollectionOrdering.Matching);
        var first = files[0];
        await Assert.That((first.GetProperty("folder").GetString(), first.GetProperty("sizeBytes").GetInt64(), first.GetProperty("isVideo").GetBoolean(), first.GetProperty("width").GetInt32(), first.GetProperty("height").GetInt32(), first.GetProperty("id").GetString()!.Length))
            .IsEqualTo((_f.In("2023"), 4L, false, 4032, 3024, 24));
        await Assert.That((files[3].GetProperty("isVideo").GetBoolean(), files[3].GetProperty("durationSeconds").GetDouble())).IsEqualTo((true, 84.0));
    }

    [Test]
    public async Task Says_When_Everything_Was_Already_On_Record_From_A_Scan()
    {
        await _f.ScannedAsync(_f.Put("IMG_1.JPG"));
        await Assert.That((await _api.FilesAsync(Files(_f.Root.FullName))).Json().GetProperty("fromScan").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task A_Folder_That_Cannot_Be_Listed_Is_Said_Not_To_Be_Found()
    {
        foreach (var root in new[] { _f.In("nowhere"), null, "bad\0path" })
        {
            var response = await _api.FilesAsync(Files(root));
            await Assert.That((response.StatusCode, response.Text())).IsEqualTo((HttpStatusCode.NotFound, $"Folder not found: {root ?? string.Empty}"));
        }
        // No folder named at all.
        var unnamed = await _api.FilesAsync(Http.Get("organize/files").FromClient());
        await Assert.That((unnamed.StatusCode, unnamed.Text())).IsEqualTo((HttpStatusCode.NotFound, "Folder not found: "));
    }

    [Test]
    public async Task Says_How_Far_The_Reading_Of_A_Folder_Has_Got()
    {
        for (var i = 1; i <= 4; i++) _f.Put($"IMG_{i}.JPG");
        async Task<(bool, int, int)> Progress(string query)
        {
            var response = await _api.ProgressAsync(Http.Get($"organize/progress{query}").FromClient());
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            return (response.Json().GetProperty("reading").GetBoolean(), response.Json().GetProperty("total").GetInt32(), response.Json().GetProperty("done").GetInt32());
        }
        var root = $"?root={Uri.EscapeDataString(_f.Root.FullName)}";
        await Assert.That((await _api.ProgressAsync(Http.Get($"organize/progress{root}"))).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        // Not being read, no folder named, and something that is no path at all: nothing is in progress.
        foreach (var query in new[] { root, "", "?root=%20", $"?root={Uri.EscapeDataString("bad\0path")}" })
            await Assert.That(await Progress(query)).IsEqualTo((false, 0, 0));

        using var hold = new ManualResetEventSlim();
        using var begun = new SemaphoreSlim(0);
        _f.Reader.OnRead = _ => { begun.Release(); hold.Wait(); };
        var listing = _api.FilesAsync(Files(_f.Root.FullName));
        await begun.WaitAsync();
        await Assert.That(await Progress(root)).IsEqualTo((true, 4, 0));
        hold.Set();
        await listing;
        await Assert.That(await Progress(root)).IsEqualTo((false, 0, 0));
    }

    [Test]
    public async Task Hands_Over_The_Files_Read_So_Far_To_A_Page_That_Says_What_It_Already_Has()
    {
        foreach (var name in new[] { "IMG_1.JPG", "IMG_2.JPG", "IMG_3.JPG", "IMG_4.JPG" }) _f.Put(name);
        _f.Reader.Says["IMG_1.JPG"] = new MediaDetails(new DateTime(2023, 6, 9, 14, 3, 22), 64, 48, null, 46.0, -112.5);
        _f.Reader.Says["IMG_2.JPG"] = new MediaDetails(null, 0, 0, null, 46.0, -112.5);
        _f.Reader.Says["IMG_3.JPG"] = new MediaDetails(null, 0, 0, null, 18.5, -68.4);
        _f.Resolver.Setup(r => r.Locate(46.0, -112.5)).Returns(new PlaceMatch("Butte", "Montana", "US", 46.0038, -112.5348));
        _f.Resolver.Setup(r => r.Locate(18.5, -68.4)).Returns(new PlaceMatch("Punta Cana", "", "DO", 18.58, -68.4));
        var root = Uri.EscapeDataString(_f.Root.FullName);
        async Task<System.Text.Json.JsonElement> Progress(string more = "")
            => (await _api.ProgressAsync(Http.Get($"organize/progress?root={root}{more}").FromClient())).Json();
        static List<string> Names(System.Text.Json.JsonElement answer) => answer.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("name").GetString()!).ToList();

        // Not being read: nothing to hand over.
        var idle = await Progress();
        await Assert.That((idle.GetProperty("reading").GetBoolean(), idle.GetProperty("readingId").ValueKind, idle.GetProperty("files").GetArrayLength(), idle.GetProperty("places").GetArrayLength(), idle.GetProperty("more").GetBoolean()))
            .IsEqualTo((false, JsonValueKind.Null, 0, 0, false));

        // Three of the four are read; the reading then waits on the last.
        using var hold = new ManualResetEventSlim();
        _f.Reader.OnRead = path => { if (path.EndsWith("IMG_4.JPG", StringComparison.Ordinal)) hold.Wait(); };
        var listing = _api.FilesAsync(Files(_f.Root.FullName));
        for (var tries = 0; (_f.Library.ProgressOf(_f.Root.FullName)?.Done ?? 0) < 3 && tries < 500; tries++) await Task.Delay(10);

        var first = await Progress();
        await Assert.That((first.GetProperty("reading").GetBoolean(), first.GetProperty("total").GetInt32(), first.GetProperty("done").GetInt32(), first.GetProperty("from").GetInt32(), first.GetProperty("more").GetBoolean()))
            .IsEqualTo((true, 4, 3, 0, false));
        await Assert.That(first.GetProperty("readingId").GetString()!.Length).IsEqualTo(32);
        await Assert.That(Names(first).Order()).IsEquivalentTo(new[] { "IMG_1.JPG", "IMG_2.JPG", "IMG_3.JPG" }, CollectionOrdering.Matching);
        // Each file says which of the reading's places it was taken at, and each place is told once.
        var towns = first.GetProperty("places").EnumerateArray().Select(p => p.GetProperty("town").GetString()!).ToList();
        await Assert.That(towns.Order()).IsEquivalentTo(new[] { "Butte", "Punta Cana" }, CollectionOrdering.Matching);
        foreach (var file in first.GetProperty("files").EnumerateArray())
        {
            var town = file.GetProperty("name").GetString() == "IMG_3.JPG" ? "Punta Cana" : "Butte";
            await Assert.That(towns[file.GetProperty("place").GetInt32()]).IsEqualTo(town);
        }

        // A page that has two files and one place is given the third file and the second place, numbered as before.
        var next = await Progress("&from=2&places=1");
        await Assert.That((next.GetProperty("from").GetInt32(), Names(next).Single(), next.GetProperty("readingId").GetString())).IsEqualTo((2, Names(first)[2], first.GetProperty("readingId").GetString()));
        await Assert.That(next.GetProperty("places").EnumerateArray().Single().GetProperty("town").GetString()).IsEqualTo(towns[1]);
        // One that has everything so far is given nothing new; nonsense for where it has got to is taken as the beginning.
        var nothing = await Progress("&from=3&places=2");
        await Assert.That((nothing.GetProperty("files").GetArrayLength(), nothing.GetProperty("places").GetArrayLength(), nothing.GetProperty("done").GetInt32())).IsEqualTo((0, 0, 3));
        var again = await Progress("&from=x&places=-5");
        await Assert.That((again.GetProperty("files").GetArrayLength(), again.GetProperty("places").GetArrayLength())).IsEqualTo((3, 2));

        hold.Set();
        var whole = (await listing).Json();
        await Assert.That(whole.GetProperty("files").GetArrayLength()).IsEqualTo(4);
        await Assert.That((await Progress()).GetProperty("reading").GetBoolean()).IsFalse();
    }

    // ---- planning

    [Test]
    public async Task Plans_A_Move_Naming_The_Files_Whose_Names_Are_Taken()
    {
        var moving = _f.Put("IMG_6435.JPG", "same");
        var other = _f.Put("IMG_7.JPG", year: 2021);
        _f.Put("Anaconda/IMG_6435.JPG", "same", year: 2019);
        _f.Reader.Says["IMG_6435.JPG"] = new MediaDetails(null, 3024, 4032, null, 46.1283, -112.9423);
        _f.Resolver.Setup(r => r.Describe(46.1283, -112.9423)).Returns("Anaconda, Montana, US");
        var ids = new[] { await _f.IdAsync(moving), await _f.IdAsync(other) };

        var response = await _api.PlanAsync(Http.Post("organize/plan", Body(new { basePath = _f.Root.FullName, folderName = "Anaconda", files = ids.Select(id => new { id }) })).FromClient());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var plan = response.Json();
        await Assert.That((plan.GetProperty("destination").GetString(), plan.GetProperty("exists").GetBoolean(), plan.GetProperty("companions").GetInt32())).IsEqualTo((_f.In("Anaconda"), true, 0));
        await Assert.That(plan.GetProperty("items").EnumerateArray().Select(i => (i.GetProperty("id").GetString()!, i.GetProperty("folder").GetString()!)))
            .IsEquivalentTo(new[] { (ids[0], _f.In("Anaconda")), (ids[1], _f.In("Anaconda")) }, CollectionOrdering.Matching);
        await Assert.That(plan.GetProperty("taken").GetProperty(_f.In("Anaconda")).EnumerateArray().Single().GetString()).IsEqualTo("IMG_6435.JPG");
        var clash = plan.GetProperty("clashes").EnumerateArray().Single();
        var there = clash.GetProperty("existing").EnumerateArray().Single();
        await Assert.That((clash.GetProperty("id").GetString(), clash.GetProperty("nextFree").GetString())).IsEqualTo((ids[0], "IMG_6435 (1).JPG"));
        await Assert.That((there.GetProperty("name").GetString(), there.GetProperty("sizeBytes").GetInt64(), there.GetProperty("date").GetString(), there.GetProperty("width").GetInt32(), there.GetProperty("height").GetInt32(),
                there.GetProperty("isVideo").GetBoolean(), there.GetProperty("placeName").GetString(), there.GetProperty("identical").GetBoolean()))
            .IsEqualTo(("IMG_6435.JPG", 4L, "2019-01-01T12:00:00", 3024, 4032, false, "Anaconda, Montana, US", true));
        await Assert.That((await _f.Library.FindAsync(there.GetProperty("id").GetString()!))!.Path).IsEqualTo(_f.In("Anaconda", "IMG_6435.JPG"));
    }

    [Test]
    public async Task A_Request_That_Cannot_Be_Carried_Out_Says_Why()
    {
        async Task<(HttpStatusCode, string)> Ask(string body)
        {
            var plan = await _api.PlanAsync(Http.Post("organize/plan", body).FromClient());
            var apply = await _api.ApplyAsync(Http.Post("organize/apply", body).FromClient());
            await Assert.That((apply.StatusCode, apply.Text())).IsEqualTo((plan.StatusCode, plan.Text()));
            return (plan.StatusCode, plan.Text());
        }

        await Assert.That(await Ask("not json")).IsEqualTo((HttpStatusCode.BadRequest, "The request could not be read."));
        await Assert.That(await Ask("null")).IsEqualTo((HttpStatusCode.BadRequest, "The request could not be read."));
        await Assert.That(await Ask("{\"files\":null}")).IsEqualTo((HttpStatusCode.BadRequest, "The request could not be read."));
        await Assert.That(await Ask(Body(new { basePath = "", folderName = "x" }))).IsEqualTo((HttpStatusCode.BadRequest, "Choose where the files go."));
        await Assert.That(await Ask(Body(new { basePath = _f.Root.FullName, folderName = " " }))).IsEqualTo((HttpStatusCode.BadRequest, "Give the folder a name first."));
        await Assert.That(await Ask(Body(new { basePath = _f.In("nowhere"), folderName = "x" }))).IsEqualTo((HttpStatusCode.NotFound, $"Folder not found: {_f.In("nowhere")}"));
        await Assert.That(await Ask(Body(new { basePath = _f.Root.FullName, folderName = "x", files = new[] { new { id = "a", subfolder = "2022\\What?" } } })))
            .IsEqualTo((HttpStatusCode.BadRequest, "Folder names cannot contain < > : \" / | ? or *"));
    }

    [Test]
    public async Task Plans_And_Moves_Each_File_Into_The_Subfolder_Asked_For_It()
    {
        var a = _f.Put("IMG_1.JPG", year: 2022);
        var b = _f.Put("IMG_2.JPG", year: 2023);
        var ids = new[] { await _f.IdAsync(a), await _f.IdAsync(b) };
        var request = new { basePath = _f.Root.FullName, folderName = "2022", mode = "move", label = "2022", files = new object[] { new { id = ids[0], subfolder = "Myrtle Beach" }, new { id = ids[1] } } };

        var plan = (await _api.PlanAsync(Http.Post("organize/plan", Body(request)).FromClient())).Json();
        await Assert.That(plan.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("folder").GetString()!)).IsEquivalentTo(new[] { _f.In("2022", "Myrtle Beach"), _f.In("2022") }, CollectionOrdering.Matching);
        var applied = (await _api.ApplyAsync(Http.Post("organize/apply", Body(request)).FromClient())).Json();
        await Assert.That(applied.GetProperty("done").GetInt32()).IsEqualTo(2);
        await Assert.That((_f.Files("2022", "Myrtle Beach").Single(), _f.Files("2022").Single())).IsEqualTo(("IMG_1.JPG", "IMG_2.JPG"));
    }

    [Test]
    public async Task Removes_Files_Into_The_Holding_Folder_Lists_That_And_Undoes_It()
    {
        var a = _f.Put("IMG_1.JPG", "aaaa");
        var b = _f.Put("Camera Roll/IMG_2.JPG", "bb");
        var ids = new[] { await _f.IdAsync(a), await _f.IdAsync(b) };

        var removed = (await _api.RemoveAsync(Http.Post("organize/remove", Body(new { root = _f.Root.FullName, files = new object[] { new { id = ids[0] }, new { id = ids[1] }, new { id = (string?)null } } })).FromClient())).Json();

        await Assert.That((removed.GetProperty("done").GetInt32(), removed.GetProperty("bytes").GetInt64(), removed.GetProperty("companions").GetInt32(), removed.GetProperty("skipped").GetInt32())).IsEqualTo((2, 6L, 0, 1));
        await Assert.That(removed.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("path").GetString()!))
            .IsEquivalentTo(new[] { _f.In("_PhotoSense_Removed", "IMG_1.JPG"), _f.In("_PhotoSense_Removed", "Camera Roll", "IMG_2.JPG") }, CollectionOrdering.Matching);
        await Assert.That(File.Exists(a) || File.Exists(b)).IsFalse();
        var batchId = removed.GetProperty("batchId").GetString()!;
        var batch = (await _api.BatchesAsync(Http.Get("organize/batches").FromClient())).Json().EnumerateArray().Single();
        await Assert.That((batch.GetProperty("id").GetString(), batch.GetProperty("label").GetString(), batch.GetProperty("mode").GetString(), batch.GetProperty("count").GetInt32())).IsEqualTo((batchId, _f.Root.FullName, "remove", 2));

        var undone = (await _api.UndoAsync(Http.Post($"organize/undo/{batchId}").FromClient(), batchId)).Json();
        await Assert.That((undone.GetProperty("restored").GetInt32(), File.Exists(a), File.Exists(b))).IsEqualTo((2, true, true));
    }

    [Test]
    public async Task A_Removal_That_Cannot_Be_Carried_Out_Says_Why()
    {
        async Task<(HttpStatusCode, string)> Ask(string body)
        {
            var answer = await _api.RemoveAsync(Http.Post("organize/remove", body).FromClient());
            return (answer.StatusCode, answer.Text());
        }
        var id = await _f.IdAsync(_f.Put("IMG_1.JPG"));

        await Assert.That(await Ask("not json")).IsEqualTo((HttpStatusCode.BadRequest, "The request could not be read."));
        await Assert.That(await Ask("null")).IsEqualTo((HttpStatusCode.BadRequest, "The request could not be read."));
        await Assert.That(await Ask("{\"root\":\"x\",\"files\":null}")).IsEqualTo((HttpStatusCode.BadRequest, "The request could not be read."));
        await Assert.That(await Ask(Body(new { root = " ", files = new[] { new { id } } }))).IsEqualTo((HttpStatusCode.NotFound, "Folder not found:  "));
        await Assert.That(await Ask(Body(new { root = "Pictures", files = new[] { new { id } } }))).IsEqualTo((HttpStatusCode.NotFound, "Folder not found: Pictures"));
        await Assert.That(await Ask(Body(new { root = _f.In("nowhere"), files = new[] { new { id } } }))).IsEqualTo((HttpStatusCode.NotFound, $"Folder not found: {_f.In("nowhere")}"));
        await Assert.That(_f.Files()).IsEquivalentTo(new[] { "IMG_1.JPG" }, CollectionOrdering.Matching);
    }

    // ---- moving, copying and undoing

    [Test]
    public async Task Moves_Files_Then_Lists_The_Move_And_Undoes_It()
    {
        var a = _f.Put("IMG_1.JPG", "aaaa", year: 2022);
        var b = _f.Put("IMG_2.JPG", "bb", year: 2023);
        var ids = new[] { await _f.IdAsync(a), await _f.IdAsync(b) };
        var request = new { basePath = _f.Root.FullName, folderName = "Trips\\Glacier", yearSplit = true, mode = "move", companions = true, label = " Trips\\Glacier ", files = new object[] { new { id = ids[0], name = "First.JPG" }, new { id = ids[1] } } };

        var applied = (await _api.ApplyAsync(Http.Post("organize/apply", Body(request)).FromClient())).Json();

        await Assert.That((applied.GetProperty("done").GetInt32(), applied.GetProperty("bytes").GetInt64(), applied.GetProperty("companions").GetInt32(), applied.GetProperty("renamed").GetInt32(),
            applied.GetProperty("skipped").GetInt32(), applied.GetProperty("problems").GetArrayLength())).IsEqualTo((2, 6L, 0, 1, 0, 0));
        await Assert.That(applied.GetProperty("items").EnumerateArray().Select(i => (i.GetProperty("id").GetString()!, i.GetProperty("path").GetString()!)))
            .IsEquivalentTo(new[] { (ids[0], _f.In("Trips", "Glacier", "2022", "First.JPG")), (ids[1], _f.In("Trips", "Glacier", "2023", "IMG_2.JPG")) }, CollectionOrdering.Matching);
        var batchId = applied.GetProperty("batchId").GetString()!;

        var batch = (await _api.BatchesAsync(Http.Get("organize/batches").FromClient())).Json().EnumerateArray().Single();
        await Assert.That((batch.GetProperty("id").GetString(), batch.GetProperty("label").GetString(), batch.GetProperty("mode").GetString(), batch.GetProperty("count").GetInt32(), batch.GetProperty("bytes").GetInt64()))
            .IsEqualTo((batchId, "Trips\\Glacier", "move", 2, 6L));
        await Assert.That(DateTime.UtcNow - batch.GetProperty("utc").GetDateTime().ToUniversalTime()).IsLessThan(TimeSpan.FromMinutes(1));

        var undone = await _api.UndoAsync(Http.Post($"organize/undo/{batchId}").FromClient(), batchId);
        await Assert.That((undone.StatusCode, undone.Json().GetProperty("restored").GetInt32(), undone.Json().GetProperty("skipped").GetInt32(), undone.Json().GetProperty("problems").GetArrayLength())).IsEqualTo((HttpStatusCode.OK, 2, 0, 0));
        await Assert.That(_f.Files()).IsEquivalentTo(new[] { "IMG_1.JPG", "IMG_2.JPG" }, CollectionOrdering.Matching);
        await Assert.That((await _api.BatchesAsync(Http.Get("organize/batches").FromClient())).Json().GetArrayLength()).IsEqualTo(0);

        // Undone already, never done, and not an id at all: each is said to be nothing to undo.
        foreach (var id in new[] { batchId, Guid.NewGuid().ToString(), "x" })
        {
            var again = await _api.UndoAsync(Http.Post($"organize/undo/{id}").FromClient(), id);
            await Assert.That((again.StatusCode, again.Text())).IsEqualTo((HttpStatusCode.NotFound, "There is nothing to undo: this was undone already, or was never done."));
        }
    }

    [Test]
    public async Task Copies_When_Asked_And_Names_The_Batch_After_The_Folder_When_No_Label_Is_Given()
    {
        var a = _f.Put("IMG_1.JPG", "aaaa");
        var request = new { basePath = _f.Root.FullName, folderName = "Copies", mode = "COPY", files = new[] { new { id = (string?)await _f.IdAsync(a) }, new { id = (string?)null } } };

        var applied = (await _api.ApplyAsync(Http.Post("organize/apply", Body(request)).FromClient())).Json();

        await Assert.That((applied.GetProperty("done").GetInt32(), applied.GetProperty("skipped").GetInt32(), applied.GetProperty("problems").GetArrayLength())).IsEqualTo((1, 1, 1));
        await Assert.That((File.Exists(a), File.Exists(_f.In("Copies", "IMG_1.JPG")))).IsEqualTo((true, true));
        var batch = (await _api.BatchesAsync(Http.Get("organize/batches").FromClient())).Json().EnumerateArray().Single();
        await Assert.That((batch.GetProperty("label").GetString(), batch.GetProperty("mode").GetString())).IsEqualTo(("Copies", "copy"));

        // Nothing to do: no batch, and so no id to undo by.
        var nothing = (await _api.ApplyAsync(Http.Post("organize/apply", Body(new { basePath = _f.Root.FullName, direct = true, files = new[] { new { id = await _f.IdAsync(a) } } })).FromClient())).Json();
        await Assert.That((nothing.GetProperty("batchId").ValueKind, nothing.GetProperty("done").GetInt32())).IsEqualTo((JsonValueKind.Null, 0));
        // A plan may leave an id out as well; it is simply a file that is not there.
        var plan = (await _api.PlanAsync(Http.Post("organize/plan", Body(new { basePath = _f.Root.FullName, folderName = "x", files = new[] { new { id = (string?)null } } })).FromClient())).Json();
        await Assert.That(plan.GetProperty("items").GetArrayLength()).IsEqualTo(0);
    }

    // ---- showing and opening a file

    [Test]
    public async Task A_Preview_Comes_From_The_Scans_Cache_Or_Is_Made_Once_And_Kept()
    {
        var scanned = _f.Put("IMG_1.JPG");
        var unscanned = _f.Put("IMG_2.JPG");
        await _f.ScannedAsync(scanned, contentHash: "ABCDEF");
        _thumbnails.Saved["ABCDEF"] = [9, 8, 7];
        _analyzer.Setup(a => a.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ImageAnalysis(4, 3, "JPEG", null, 0, [], [1, 2]));

        var cached = await _api.ThumbnailAsync(Http.Get("t"), await _f.IdAsync(scanned));
        await Assert.That(cached.Bytes()).IsEquivalentTo(new byte[] { 9, 8, 7 }, CollectionOrdering.Matching);
        await Assert.That((cached.Header("Content-Type"), cached.Header("ETag"))).IsEqualTo(("image/jpeg", "\"ABCDEF\""));
        _analyzer.VerifyNoOtherCalls();

        var id = await _f.IdAsync(unscanned);
        var made = await _api.ThumbnailAsync(Http.Get("t"), id);
        await Assert.That(made.Bytes()).IsEquivalentTo(new byte[] { 1, 2 }, CollectionOrdering.Matching);
        await Assert.That((await _api.ThumbnailAsync(Http.Get("t"), id)).Bytes()).IsEquivalentTo(new byte[] { 1, 2 }, CollectionOrdering.Matching);
        _analyzer.Verify(a => a.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        // Kept under a name made from its size and when it last changed, which go with it when it is moved.
        var key = _thumbnails.Saved.Keys.Single(k => k != "ABCDEF");
        await Assert.That((key.Length, key.All(char.IsAsciiHexDigit), made.Header("ETag"))).IsEqualTo((64, true, $"\"{key}\""));
        Directory.CreateDirectory(_f.In("moved"));
        File.Move(unscanned, _f.In("moved", "Another name.JPG"));
        await Assert.That((await _api.ThumbnailAsync(Http.Get("t"), await _f.IdAsync(_f.In("moved", "Another name.JPG")))).Header("ETag")).IsEqualTo($"\"{key}\"");
        _analyzer.Verify(a => a.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task There_Is_No_Preview_For_A_Video_An_Unknown_File_Or_One_That_Cannot_Be_Decoded()
    {
        var video = await _f.IdAsync(_f.Put("clip.MOV"));
        var broken = await _f.IdAsync(_f.Put("broken.JPG"));
        _analyzer.Setup(a => a.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidDataException("not an image"));
        foreach (var id in new[] { video, broken, "never-given-out" })
        {
            await Assert.That((await _api.ThumbnailAsync(Http.Get("t"), id)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            if (id != broken) await Assert.That((await _api.ImageAsync(Http.Get("i"), id)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }

        _analyzer.Setup(a => a.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => _api.ThumbnailAsync(Http.Get("t"), broken));
    }

    [Test]
    public async Task A_Picture_Is_Shown_At_Full_Size_As_The_Browser_Can_Take_It()
    {
        var jpeg = await _f.IdAsync(_f.Put("IMG_1.jpg", "jpeg bytes"));
        var heic = await _f.IdAsync(_f.Put("IMG_2.HEIC"));
        _analyzer.Setup(a => a.RenderJpegAsync(It.IsAny<Stream>(), 2560, It.IsAny<CancellationToken>())).ReturnsAsync([5, 5]);

        var asItIs = await _api.ImageAsync(Http.Get("i"), jpeg);
        await Assert.That((asItIs.Text(), asItIs.Header("Content-Type"), asItIs.Header("ETag"))).IsEqualTo(("jpeg bytes", "image/jpeg", null));
        var converted = await _api.ImageAsync(Http.Get("i"), heic);
        await Assert.That((converted.StatusCode, converted.Header("Content-Type"))).IsEqualTo((HttpStatusCode.OK, "image/jpeg"));
        await Assert.That(converted.Bytes()).IsEquivalentTo(new byte[] { 5, 5 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_File_Is_Handed_To_The_Systems_Default_Viewer()
    {
        var path = _f.Put("IMG_1.HEIC");
        var id = await _f.IdAsync(path);
        await Assert.That((await _api.OpenAsync(Http.Post("o").FromClient(), id)).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        _viewer.Verify(v => v.Open(path), Times.Once);

        var unknown = await _api.OpenAsync(Http.Post("o").FromClient(), "never-given-out");
        await Assert.That((unknown.StatusCode, unknown.Text())).IsEqualTo((HttpStatusCode.NotFound, "The file is no longer there. Choose the folder again."));

        _viewer.Setup(v => v.Open(It.IsAny<string>())).Throws(new FileNotFoundException("gone"));
        var missing = await _api.OpenAsync(Http.Post("o").FromClient(), id);
        await Assert.That((missing.StatusCode, missing.Text())).IsEqualTo((HttpStatusCode.NotFound, "The file is no longer there. Choose the folder again."));

        _viewer.Setup(v => v.Open(It.IsAny<string>())).Throws(new InvalidOperationException("No application could be started for IMG_1.HEIC"));
        var failed = await _api.OpenAsync(Http.Post("o").FromClient(), id);
        await Assert.That((failed.StatusCode, failed.Text())).IsEqualTo((HttpStatusCode.InternalServerError, "No application could be started for IMG_1.HEIC"));
    }
}
