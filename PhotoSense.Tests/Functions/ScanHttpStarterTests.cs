using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Configuration;
using PhotoSense.Functions.Scanning;

namespace PhotoSense.Tests.Functions;

public class ScanHttpStarterTests
{
    private static readonly PhotoStorageOptions Defaults = new() { PrimaryPath = "configured-primary", SecondaryPath = "configured-secondary" };

    [Test]
    public async Task Reads_The_Fields_The_Web_Client_Sends()
    {
        var request = ScanHttpStarter.ParseBody("""{"primaryLocation":" C:\\Photos ","secondaryLocation":"D:\\Backup","recursive":false}""", Defaults);
        await Assert.That(request).IsEqualTo(new ScanRequest("C:\\Photos", "D:\\Backup", false));
    }

    [Test]
    public async Task Still_Reads_The_Shorter_Field_Names()
    {
        var request = ScanHttpStarter.ParseBody("""{"primary":"C:\\Photos","secondary":"D:\\Backup"}""", Defaults);
        await Assert.That(request).IsEqualTo(new ScanRequest("C:\\Photos", "D:\\Backup", true));
    }

    [Test]
    [Arguments("")]
    [Arguments("not json")]
    [Arguments("[]")]
    [Arguments("""{"primaryLocation":"  ","secondaryLocation":null,"recursive":"yes"}""")]
    public async Task Falls_Back_To_The_Configured_Folders(string body)
        => await Assert.That(ScanHttpStarter.ParseBody(body, Defaults)).IsEqualTo(new ScanRequest("configured-primary", "configured-secondary", true));

    [Test]
    public async Task A_Scan_Is_Running_From_Its_Start_Until_It_Completes()
    {
        var progress = new PhotoSense.Application.Scanning.InMemoryScanProgressStore();
        await Assert.That(ScanHttpStarter.IsRunning(progress.GetLatest())).IsFalse();
        progress.ScanStarted("a");
        await Assert.That(ScanHttpStarter.IsRunning(progress.GetLatest())).IsTrue();
        progress.ScanCompleted("a");
        await Assert.That(ScanHttpStarter.IsRunning(progress.GetLatest())).IsFalse();
    }

    [Test]
    [Arguments("""{"primaryLocation":"C:\\Photos","startOver":true}""", true)]
    [Arguments("""{"primaryLocation":"C:\\Photos","startOver":false}""", false)]
    [Arguments("""{"primaryLocation":"C:\\Photos"}""", false)]                      // not asked for: earlier results are built on
    [Arguments("""{"primaryLocation":"C:\\Photos","startOver":"yes"}""", false)]     // only a plain true forgets anything
    [Arguments("not json", false)]
    public async Task A_Scan_Starts_Over_Only_When_Plainly_Asked_To(string body, bool startOver)
        => await Assert.That(ScanHttpStarter.ParseBody(body, Defaults).StartOver).IsEqualTo(startOver);

    [Test]
    public async Task No_Secondary_Folder_Anywhere_Means_None()
    {
        var request = ScanHttpStarter.ParseBody("""{"primaryLocation":"C:\\Photos"}""", new PhotoStorageOptions());
        await Assert.That(request).IsEqualTo(new ScanRequest("C:\\Photos", null, true));
    }
}
