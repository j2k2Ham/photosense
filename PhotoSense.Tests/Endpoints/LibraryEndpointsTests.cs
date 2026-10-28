using System.Net;
using PhotoSense.Api.Endpoints;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Files;

namespace PhotoSense.Tests.Endpoints;

public class LibraryEndpointsTests
{
    private readonly FakePhotoAccess _access = new();
    private readonly LibraryEndpoints _endpoints;

    public LibraryEndpointsTests() => _endpoints = new LibraryEndpoints(_access);

    [Test]
    [Arguments(PhotoAccess.Everything, "everything")]
    [Arguments(PhotoAccess.Selected, "selected")]
    [Arguments(PhotoAccess.Nothing, "nothing")]
    public async Task Says_How_Much_Of_The_Photos_PhotoSense_May_See(PhotoAccess access, string said)
    {
        _access.Current = access;
        var response = await _endpoints.AccessAsync(Http.Get("library/access"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Json().GetProperty("access").GetString()).IsEqualTo(said);
    }

    [Test]
    public async Task Shows_The_Systems_Picker_And_Answers_Once_It_Is_Put_Away_With_What_May_Be_Seen_Then()
    {
        _access.Current = PhotoAccess.Selected;
        var response = await _endpoints.ChooseMoreAsync(Http.Post("library/choose-more").FromClient());

        await Assert.That(_access.PickersShown).IsEqualTo(1);
        await Assert.That(response.Json().GetProperty("access").GetString()).IsEqualTo("everything");
    }

    [Test]
    public async Task Opens_PhotoSenses_Own_Settings_Where_Its_Access_Is_Changed()
    {
        var response = _endpoints.OpenSettings(Http.Post("library/open-settings").FromClient());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(_access.SettingsOpened).IsEqualTo(1);
    }

    [Test]
    public async Task Lets_No_Other_Web_Page_Show_The_Picker_Or_Open_The_Settings()
    {
        await Assert.That((await _endpoints.ChooseMoreAsync(Http.Post("library/choose-more"))).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(_endpoints.OpenSettings(Http.Post("library/open-settings")).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(_access.PickersShown).IsEqualTo(0);
        await Assert.That(_access.SettingsOpened).IsEqualTo(0);
    }

    [Test]
    public async Task On_A_Computer_Everything_It_Can_Reach_Is_Seen_And_There_Is_Nothing_To_Choose_Or_Allow()
    {
        var folders = new FolderAccess();
        await folders.ChooseMoreAsync(CancellationToken.None);
        folders.OpenSettings();
        await Assert.That(folders.Current).IsEqualTo(PhotoAccess.Everything);
    }
}
