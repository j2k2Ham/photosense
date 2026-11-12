using Microsoft.AspNetCore.Builder;
using PhotoSense.Server;

namespace PhotoSense.Tests.Server;

/// <summary>The program as it is built to start, with a data folder of the test's own and its stores in memory.</summary>
internal sealed class ServerFixture : IDisposable
{
    /// <summary>The fixture's own folder, deleted when it ends.</summary>
    public DirectoryInfo Root { get; } = Directory.CreateTempSubdirectory("photosense-server-");
    /// <summary>Where the database goes: in a folder not there yet, which the service has to make.</summary>
    public string DatabasePath => Path.Combine(Root.FullName, "not-there-yet", "photosense.db");
    /// <summary>Where the previews go.</summary>
    public string ThumbnailPath => Path.Combine(Root.FullName, "thumbnails");
    /// <summary>The program, built but not started.</summary>
    public WebApplication Program { get; }
    /// <summary>The program's services.</summary>
    public IServiceProvider Services => Program.Services;

    /// <summary>The program with its data in the fixture's folder, and any other settings given, as on the command line.</summary>
    public ServerFixture(params string[] settings)
        => Program = PhotoSenseServer.Build(
        [
            $"--PhotoStorage:DatabasePath={DatabasePath}",
            $"--PhotoStorage:ThumbnailPath={ThumbnailPath}",
            "--PhotoStorage:KeepFormat=WidelyCompatible",
            .. settings,
        ], Root.FullName, s => s.AddStandInStores());

    /// <summary>A path in the fixture's folder.</summary>
    public string In(params string[] parts) => Path.Combine([Root.FullName, .. parts]);

    /// <summary>The program, started on a port of its own.</summary>
    public static async Task<ServerFixture> StartedAsync(CancellationToken ct)
    {
        var started = new ServerFixture("--urls=http://127.0.0.1:0");
        await started.Program.StartAsync(ct);
        return started;
    }

    /// <summary>An address of the program, once started.</summary>
    public Uri At(string pathAndQuery) => new(new Uri(Program.Urls.Single()), pathAndQuery);

    public void Dispose()
    {
        // The database file is let go of first, so that the folder can go.
        ((IDisposable)Program).Dispose();
        TestFiles.Remove(Root);
    }
}
