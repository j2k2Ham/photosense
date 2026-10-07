using System.Net;
using Moq;
using PhotoSense.Domain.Services;
using PhotoSense.Functions.Api;
using Xunit;

namespace PhotoSense.Tests.Functions;

public class FoldersFunctionsTests
{
    private readonly Mock<IFolderBrowser> _browser = new();
    private readonly FoldersFunctions _functions;

    public FoldersFunctionsTests() => _functions = new FoldersFunctions(_browser.Object);

    [Fact]
    public async Task Lists_The_Folders_Inside_The_One_Asked_For()
    {
        const string pictures = @"C:\Users\someone\Pictures";
        _browser.Setup(b => b.Browse(pictures)).Returns(new FolderListing(pictures, @"C:\Users\someone",
            [new FolderEntry("Jamie's Phone", pictures + @"\Jamie's Phone"), new FolderEntry("Trips & days out", pictures + @"\Trips & days out")]));

        var response = await _functions.BrowseAsync(Http.Get("folders?path=" + Uri.EscapeDataString(pictures)).FromClient());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = response.Json();
        Assert.Equal(pictures, json.GetProperty("path").GetString());
        Assert.Equal(@"C:\Users\someone", json.GetProperty("parent").GetString());
        var folders = json.GetProperty("folders").EnumerateArray().Select(f => (f.GetProperty("name").GetString()!, f.GetProperty("path").GetString()!)).ToList();
        Assert.Equal(new[] { ("Jamie's Phone", pictures + @"\Jamie's Phone"), ("Trips & days out", pictures + @"\Trips & days out") }, folders);
    }

    [Fact]
    public async Task With_No_Folder_Named_It_Lists_The_Places_To_Start_From()
    {
        _browser.Setup(b => b.Browse(null)).Returns(new FolderListing(null, null, [new FolderEntry("Pictures", @"C:\Users\someone\Pictures"), new FolderEntry(@"C:\", @"C:\")]));

        var json = (await _functions.BrowseAsync(Http.Get("folders").FromClient())).Json();

        Assert.Equal(System.Text.Json.JsonValueKind.Null, json.GetProperty("path").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, json.GetProperty("parent").ValueKind);
        Assert.Equal(2, json.GetProperty("folders").GetArrayLength());
    }

    [Theory]
    [InlineData(typeof(DirectoryNotFoundException))]
    [InlineData(typeof(PathTooLongException))]
    [InlineData(typeof(ArgumentException))]         // not something that could be a path
    public async Task A_Folder_That_Cannot_Be_Found_Is_Reported_By_Name(Type failure)
    {
        _browser.Setup(b => b.Browse(It.IsAny<string?>())).Throws((Exception)Activator.CreateInstance(failure)!);

        var response = await _functions.BrowseAsync(Http.Get("folders?path=" + Uri.EscapeDataString(@"D:\gone")).FromClient());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(@"Folder not found: D:\gone", response.Text());
    }

    [Fact]
    public async Task Other_Failures_Are_Not_Passed_Off_As_A_Missing_Folder()
    {
        _browser.Setup(b => b.Browse(It.IsAny<string?>())).Throws(new InvalidOperationException("something else"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _functions.BrowseAsync(Http.Get("folders?path=x").FromClient()));
    }

    [Fact]
    public async Task Folder_Names_Are_Told_Only_To_The_UI_Itself()
    {
        // A page on some other site can make a browser ask, but cannot make it send the UI's header.
        var response = await _functions.BrowseAsync(Http.Get("folders"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(response.Bytes());
        _browser.Verify(b => b.Browse(It.IsAny<string?>()), Times.Never);
    }
}
