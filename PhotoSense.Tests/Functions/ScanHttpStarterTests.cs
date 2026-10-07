using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Configuration;
using PhotoSense.Functions.Scanning;
using Xunit;

namespace PhotoSense.Tests.Functions;

public class ScanHttpStarterTests
{
    private static readonly PhotoStorageOptions Defaults = new() { PrimaryPath = "configured-primary", SecondaryPath = "configured-secondary" };

    [Fact]
    public void Reads_The_Fields_The_Web_Client_Sends()
    {
        var request = ScanHttpStarter.ParseBody("""{"primaryLocation":" C:\\Photos ","secondaryLocation":"D:\\Backup","recursive":false}""", Defaults);
        Assert.Equal(new ScanRequest("C:\\Photos", "D:\\Backup", false), request);
    }

    [Fact]
    public void Still_Reads_The_Shorter_Field_Names()
    {
        var request = ScanHttpStarter.ParseBody("""{"primary":"C:\\Photos","secondary":"D:\\Backup"}""", Defaults);
        Assert.Equal(new ScanRequest("C:\\Photos", "D:\\Backup", true), request);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"primaryLocation":"  ","secondaryLocation":null,"recursive":"yes"}""")]
    public void Falls_Back_To_The_Configured_Folders(string body)
        => Assert.Equal(new ScanRequest("configured-primary", "configured-secondary", true), ScanHttpStarter.ParseBody(body, Defaults));

    [Fact]
    public void A_Scan_Is_Running_From_Its_Start_Until_It_Completes()
    {
        var progress = new PhotoSense.Application.Scanning.InMemoryScanProgressStore();
        Assert.False(ScanHttpStarter.IsRunning(progress.GetLatest()));
        progress.ScanStarted("a");
        Assert.True(ScanHttpStarter.IsRunning(progress.GetLatest()));
        progress.ScanCompleted("a");
        Assert.False(ScanHttpStarter.IsRunning(progress.GetLatest()));
    }

    [Theory]
    [InlineData("""{"primaryLocation":"C:\\Photos","startOver":true}""", true)]
    [InlineData("""{"primaryLocation":"C:\\Photos","startOver":false}""", false)]
    [InlineData("""{"primaryLocation":"C:\\Photos"}""", false)]                      // not asked for: earlier results are built on
    [InlineData("""{"primaryLocation":"C:\\Photos","startOver":"yes"}""", false)]     // only a plain true forgets anything
    [InlineData("not json", false)]
    public void A_Scan_Starts_Over_Only_When_Plainly_Asked_To(string body, bool startOver)
        => Assert.Equal(startOver, ScanHttpStarter.ParseBody(body, Defaults).StartOver);

    [Fact]
    public void No_Secondary_Folder_Anywhere_Means_None()
    {
        var request = ScanHttpStarter.ParseBody("""{"primaryLocation":"C:\\Photos"}""", new PhotoStorageOptions());
        Assert.Equal(new ScanRequest("C:\\Photos", null, true), request);
    }
}
