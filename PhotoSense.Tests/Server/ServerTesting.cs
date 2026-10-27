using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Server;

namespace PhotoSense.Tests.Server;

/// <summary>The service's registrations, with a data folder of the test's own.</summary>
internal sealed class ServerFixture : IDisposable
{
    public DirectoryInfo Root { get; } = Directory.CreateTempSubdirectory("photosense-server-");
    public string DatabasePath => Path.Combine(Root.FullName, "not-there-yet", "photosense.db");
    public string ThumbnailPath => Path.Combine(Root.FullName, "thumbnails");
    public ServiceProvider Services { get; }

    public ServerFixture()
    {
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PhotoStorage:DatabasePath"] = DatabasePath,
            ["PhotoStorage:ThumbnailPath"] = ThumbnailPath,
            ["PhotoStorage:KeepFormat"] = "WidelyCompatible",
        }).Build();
        Services = new ServiceCollection().AddLogging().AddPhotoSenseCore(settings).AddPhotoSenseApi().BuildServiceProvider();
    }

    public void Dispose()
    {
        // The database file is let go of first, so that the folder can go.
        Services.Dispose();
        TestFiles.Remove(Root);
    }
}
