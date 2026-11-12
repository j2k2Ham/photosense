using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Files;

namespace PhotoSense.Tests.Endpoints;

/// <summary>What the person has let PhotoSense see of a phone's photos, and the ways of letting it see more.</summary>
public class LibraryEndpointsTests
{
    private readonly FakePhotoAccess _access = new();
    private readonly AboutApi _endpoints;

    public LibraryEndpointsTests() => _endpoints = new AboutApi(new TestApi(s => s.AddSingleton<IPhotoAccess>(_access)));

    [Test]
    [Arguments(PhotoAccess.Everything, "everything")]
    [Arguments(PhotoAccess.Selected, "selected")]
    [Arguments(PhotoAccess.Nothing, "nothing")]
    public async Task Says_How_Much_Of_The_Photos_PhotoSense_May_See(PhotoAccess access, string said, CancellationToken ct)
    {
        _access.Current = access;
        var response = await _endpoints.AccessAsync(Http.Get("library/access"), ct);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Json().GetProperty("access").GetString()).IsEqualTo(said);
    }

    [Test]
    [Arguments(PhotoAccess.Selected, "selected")]
    [Arguments(PhotoAccess.Nothing, "nothing")]
    public async Task Asks_The_Systems_Question_And_Answers_Once_It_Is_Answered_With_What_May_Be_Seen_Then(PhotoAccess answer, string said, CancellationToken ct)
    {
        _access.Current = PhotoAccess.Nothing;
        _access.Answer = answer;
        var response = await _endpoints.AskAsync(Http.Post("library/ask").FromClient(), ct);

        await Assert.That(_access.Asked).IsEqualTo(1);
        await Assert.That(response.Json().GetProperty("access").GetString()).IsEqualTo(said);
    }

    [Test]
    public async Task Shows_The_Systems_Picker_And_Answers_Once_It_Is_Put_Away_With_What_May_Be_Seen_Then(CancellationToken ct)
    {
        _access.Current = PhotoAccess.Selected;
        var response = await _endpoints.ChooseMoreAsync(Http.Post("library/choose-more").FromClient(), ct);

        await Assert.That(_access.PickersShown).IsEqualTo(1);
        await Assert.That(response.Json().GetProperty("access").GetString()).IsEqualTo("everything");
    }

    [Test]
    public async Task Opens_PhotoSenses_Own_Settings_Where_Its_Access_Is_Changed(CancellationToken ct)
    {
        var response = await _endpoints.OpenSettingsAsync(Http.Post("library/open-settings").FromClient(), ct);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(_access.SettingsOpened).IsEqualTo(1);
    }

    [Test]
    public async Task Lets_No_Other_Web_Page_Ask_Show_The_Picker_Or_Open_The_Settings(CancellationToken ct)
    {
        await Assert.That((await _endpoints.AskAsync(Http.Post("library/ask"), ct)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(_access.Asked).IsEqualTo(0);
        await Assert.That((await _endpoints.ChooseMoreAsync(Http.Post("library/choose-more"), ct)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await _endpoints.OpenSettingsAsync(Http.Post("library/open-settings"), ct)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(_access.PickersShown).IsEqualTo(0);
        await Assert.That(_access.SettingsOpened).IsEqualTo(0);
    }

    [Test]
    public async Task On_A_Computer_Everything_It_Can_Reach_Is_Seen_And_There_Is_Nothing_To_Choose_Or_Allow(CancellationToken ct)
    {
        var folders = new FolderAccess();
        await folders.AskAsync(ct);
        await folders.ChooseMoreAsync(ct);
        folders.OpenSettings();
        await Assert.That(folders.Current).IsEqualTo(PhotoAccess.Everything);
    }
}
