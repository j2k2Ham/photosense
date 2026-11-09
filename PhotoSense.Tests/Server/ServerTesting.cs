using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Api;
using PhotoSense.Server;

namespace PhotoSense.Tests.Server;

/// <summary>The service's registrations, with a data folder of the test's own.</summary>
internal sealed class ServerFixture : IDisposable
{
    /// <summary>The fixture's own folder, deleted when it ends.</summary>
    public DirectoryInfo Root { get; } = Directory.CreateTempSubdirectory("photosense-server-");
    /// <summary>Where the database goes: in a folder not there yet, which the service has to make.</summary>
    public string DatabasePath => Path.Combine(Root.FullName, "not-there-yet", "photosense.db");
    /// <summary>Where the previews go.</summary>
    public string ThumbnailPath => Path.Combine(Root.FullName, "thumbnails");
    /// <summary>The service's registrations.</summary>
    public ServiceProvider Services { get; }

    public ServerFixture()
    {
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PhotoStorage:DatabasePath"] = DatabasePath,
            ["PhotoStorage:ThumbnailPath"] = ThumbnailPath,
            ["PhotoStorage:KeepFormat"] = "WidelyCompatible",
        }).Build();
        Services = new ServiceCollection().AddLogging().AddPhotoSenseComputer(settings).AddPhotoSenseApi().BuildServiceProvider();
    }

    public void Dispose()
    {
        // The database file is let go of first, so that the folder can go.
        Services.Dispose();
        TestFiles.Remove(Root);
    }
}
