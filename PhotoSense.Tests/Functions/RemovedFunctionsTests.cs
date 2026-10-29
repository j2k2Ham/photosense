using System.Net;
using System.Text.Json;
using PhotoSense.Domain.Configuration;
using PhotoSense.Functions.Api;
using PhotoSense.Tests.Application;

namespace PhotoSense.Tests.Functions;

public sealed class RemovedFunctionsTests : IDisposable
{
    private const string Held = PhotoStorageOptions.RemovedFolderName;
    private readonly OrganizeFixture _f = new();
    private readonly RemovedFunctions _api;

    public RemovedFunctionsTests() => _api = new RemovedFunctions(_f.Removed());

    public void Dispose() => _f.Dispose();

    private static string Body(object request) => JsonSerializer.Serialize(request);
    private static string Roots(params string[] roots) => "removed?" + string.Join('&', roots.Select(r => "root=" + Uri.EscapeDataString(r)));

    [Test]
    public async Task Only_The_Page_Itself_Is_Told_Or_Obeyed()
    {
        var held = _f.Put($"Phone/{Held}/IMG_1.JPG");

        await Assert.That((await _api.FindAsync(Http.Get(Roots(_f.In("Phone"))))).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await _api.EraseAsync(Http.Post("removed/erase", Body(new { folders = new[] { _f.In("Phone", Held) } })))).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(File.Exists(held)).IsTrue();
    }

    [Test]
    public async Task Lists_The_Folders_That_Hold_Removed_Files_With_What_They_Come_To()
    {
        _f.Put($"Phone/{Held}/IMG_1.JPG", "aaaa");
        _f.Put($"Phone/{Held}/2022/IMG_2.JPG", "bb");
        _f.Put($"Backup/{Held}/IMG_3.JPG", "c");

        var found = (await _api.FindAsync(Http.Get(Roots(_f.In("Phone"), _f.In("Backup"))).FromClient())).Json();

        await Assert.That(found.GetProperty("folders").EnumerateArray().Select(f => (f.GetProperty("path").GetString()!, f.GetProperty("files").GetInt32(), f.GetProperty("bytes").GetInt64())))
            .IsEquivalentTo(new[] { (_f.In("Backup", Held), 1, 1L), (_f.In("Phone", Held), 2, 6L) }, CollectionOrdering.Matching);
        await Assert.That((found.GetProperty("files").GetInt32(), found.GetProperty("bytes").GetInt64())).IsEqualTo((3, 7L));

        // Asked about no folder, it has only what is on record to go by.
        var nothing = (await _api.FindAsync(Http.Get("removed").FromClient())).Json();
        await Assert.That((nothing.GetProperty("folders").GetArrayLength(), nothing.GetProperty("files").GetInt32(), nothing.GetProperty("bytes").GetInt64())).IsEqualTo((0, 0, 0L));
    }

    [Test]
    public async Task Erases_What_Is_In_The_Folders_Named()
    {
        _f.Put($"Phone/{Held}/IMG_1.JPG", "aaaa");
        _f.Put($"Phone/{Held}/2022/IMG_2.JPG", "bb");
        var kept = _f.Put("Phone/IMG_9.JPG");

        var response = await _api.EraseAsync(Http.Post("removed/erase", Body(new { folders = new[] { _f.In("Phone", Held) } })).FromClient());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var erased = response.Json();
        await Assert.That((erased.GetProperty("erased").GetInt32(), erased.GetProperty("bytes").GetInt64(), erased.GetProperty("skipped").GetInt32(), erased.GetProperty("problems").GetArrayLength())).IsEqualTo((2, 6L, 0, 0));
        await Assert.That((Directory.Exists(_f.In("Phone", Held)), File.Exists(kept))).IsEqualTo((false, true));
    }

    [Test]
    public async Task Says_What_Could_Not_Be_Erased()
    {
        _f.Put($"Phone/{Held}/IMG_1.JPG", "aaaa");
        var api = new RemovedFunctions(_f.Removed(_ => throw new IOException("The file is in use.")));

        var erased = (await api.EraseAsync(Http.Post("removed/erase", Body(new { folders = new[] { _f.In("Phone", Held) } })).FromClient())).Json();

        await Assert.That((erased.GetProperty("erased").GetInt32(), erased.GetProperty("skipped").GetInt32(), erased.GetProperty("problems")[0].GetString())).IsEqualTo((0, 1, "IMG_1.JPG: The file is in use."));
    }

    [Test]
    public async Task A_Request_It_Cannot_Carry_Out_Erases_Nothing_And_Says_Why()
    {
        var held = _f.Put($"Phone/{Held}/IMG_1.JPG");
        var photo = _f.Put("Phone/IMG_2.JPG");
        async Task<(HttpStatusCode, string)> Ask(string body)
        {
            var answer = await _api.EraseAsync(Http.Post("removed/erase", body).FromClient());
            return (answer.StatusCode, answer.Text());
        }

        await Assert.That(await Ask("not json")).IsEqualTo((HttpStatusCode.BadRequest, "The request could not be read."));
        await Assert.That(await Ask("null")).IsEqualTo((HttpStatusCode.BadRequest, "The request could not be read."));
        await Assert.That(await Ask("{\"folders\":null}")).IsEqualTo((HttpStatusCode.BadRequest, "The request could not be read."));
        await Assert.That(await Ask(Body(new { folders = new[] { _f.In("Phone", Held), _f.In("Phone") } }))).IsEqualTo((HttpStatusCode.BadRequest, $"Not a folder of removed files: {_f.In("Phone")}"));
        await Assert.That((File.Exists(held), File.Exists(photo))).IsEqualTo((true, true));
    }
}
