using System.Net;
using Moq;
using PhotoSense.Domain.Services;
using PhotoSense.Functions.Api;

namespace PhotoSense.Tests.Functions;

public class FoldersFunctionsTests
{
    private readonly Mock<IFolderBrowser> _browser = new();
    private readonly FoldersFunctions _functions;

    public FoldersFunctionsTests() => _functions = new FoldersFunctions(_browser.Object);

    [Test]
    public async Task Lists_The_Folders_Inside_The_One_Asked_For()
    {
        const string pictures = @"C:\Users\someone\Pictures";
        _browser.Setup(b => b.Browse(pictures)).Returns(new FolderListing(pictures, @"C:\Users\someone",
            [new FolderEntry("Jamie's Phone", pictures + @"\Jamie's Phone"), new FolderEntry("Trips & days out", pictures + @"\Trips & days out")]));

        var response = await _functions.BrowseAsync(Http.Get("folders?path=" + Uri.EscapeDataString(pictures)).FromClient());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var json = response.Json();
        await Assert.That(json.GetProperty("path").GetString()).IsEqualTo(pictures);
        await Assert.That(json.GetProperty("parent").GetString()).IsEqualTo(@"C:\Users\someone");
        var folders = json.GetProperty("folders").EnumerateArray().Select(f => (f.GetProperty("name").GetString()!, f.GetProperty("path").GetString()!)).ToList();
        await Assert.That(folders).IsEquivalentTo(new[] { ("Jamie's Phone", pictures + @"\Jamie's Phone"), ("Trips & days out", pictures + @"\Trips & days out") }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task With_No_Folder_Named_It_Lists_The_Places_To_Start_From()
    {
        _browser.Setup(b => b.Browse(null)).Returns(new FolderListing(null, null, [new FolderEntry("Pictures", @"C:\Users\someone\Pictures"), new FolderEntry(@"C:\", @"C:\")]));

        var json = (await _functions.BrowseAsync(Http.Get("folders").FromClient())).Json();

        await Assert.That(json.GetProperty("path").ValueKind).IsEqualTo(System.Text.Json.JsonValueKind.Null);
        await Assert.That(json.GetProperty("parent").ValueKind).IsEqualTo(System.Text.Json.JsonValueKind.Null);
        await Assert.That(json.GetProperty("folders").GetArrayLength()).IsEqualTo(2);
    }

    [Test]
    [Arguments(typeof(DirectoryNotFoundException))]
    [Arguments(typeof(PathTooLongException))]
    [Arguments(typeof(ArgumentException))]         // not something that could be a path
    public async Task A_Folder_That_Cannot_Be_Found_Is_Reported_By_Name(Type failure)
    {
        _browser.Setup(b => b.Browse(It.IsAny<string?>())).Throws((Exception)Activator.CreateInstance(failure)!);

        var response = await _functions.BrowseAsync(Http.Get("folders?path=" + Uri.EscapeDataString(@"D:\gone")).FromClient());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(response.Text()).IsEqualTo(@"Folder not found: D:\gone");
    }

    [Test]
    public async Task Other_Failures_Are_Not_Passed_Off_As_A_Missing_Folder()
    {
        _browser.Setup(b => b.Browse(It.IsAny<string?>())).Throws(new InvalidOperationException("something else"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _functions.BrowseAsync(Http.Get("folders?path=x").FromClient()));
    }

    [Test]
    public async Task Folder_Names_Are_Told_Only_To_The_UI_Itself()
    {
        // A page on some other site can make a browser ask, but cannot make it send the UI's header.
        var response = await _functions.BrowseAsync(Http.Get("folders"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Bytes()).IsEmpty();
        _browser.Verify(b => b.Browse(It.IsAny<string?>()), Times.Never);
    }
}
