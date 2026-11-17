using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PhotoSense.Domain.Configuration;
using PhotoSense.Server;

namespace PhotoSense.Tests.Server;

/// <summary>Where the program keeps its data, as its settings say, refusing settings that would keep it where it could be lost.</summary>
public sealed class StorageSettingsTests
{
    // The settings as the program reads them at start-up, from the command line given.
    private static PhotoStorageOptions Read(params string[] args)
    {
        var root = Directory.CreateTempSubdirectory("photosense-settings-");
        try
        {
            using var program = PhotoSenseServer.Build(args, root.FullName);
            return program.Services.GetRequiredService<IOptions<PhotoStorageOptions>>().Value;
        }
        finally
        {
            TestFiles.Remove(root);
        }
    }

    [Test]
    public async Task Phones_Back_Up_Into_PhotoSense_In_The_Persons_Pictures_Or_The_Folder_The_Settings_Name(CancellationToken ct)
    {
        var home = Path.Combine(Path.GetTempPath(), "photosense-home");
        var pictures = Path.Combine(home, "My Pictures");
        await Assert.That(Read().ResolveBackupPath(pictures, home)).IsEqualTo(Path.Combine(pictures, "PhotoSense"));
        await Assert.That(Read("--PhotoStorage:BackupPath=Backups").ResolveBackupPath(pictures, home)).IsEqualTo(Path.Combine(pictures, "Backups"));
    }

    [Test]
    public async Task Where_The_System_Names_No_Pictures_Folder_The_One_In_The_Persons_Home_Stands_In(CancellationToken ct)
    {
        // As on a Linux account without one: the program still starts, with a full path in the settings or none.
        var home = Path.Combine(Path.GetTempPath(), "photosense-home");
        var elsewhere = Path.Combine(Path.GetTempPath(), "photosense-backups");
        await Assert.That(Read().ResolveBackupPath("", home)).IsEqualTo(Path.Combine(home, "Pictures", "PhotoSense"));
        await Assert.That(Read($"--PhotoStorage:BackupPath={elsewhere}").ResolveBackupPath("", home)).IsEqualTo(elsewhere);
        await Assert.That(Read().ResolveBackupPath("", "")).IsEqualTo(Path.Combine(Path.GetTempPath(), "Pictures", "PhotoSense"));
    }

    [Test]
    public async Task Settings_That_Name_No_Database_Are_Refused(CancellationToken ct)
    {
        var refused = Assert.Throws<OptionsValidationException>(() => Read("--PhotoStorage:DatabasePath= "));
        await Assert.That(refused!.Message).IsEqualTo("DatabasePath must be provided");
    }

    [Test]
    public async Task Data_Inside_The_Folder_The_Service_Runs_From_Is_Refused(CancellationToken ct)
    {
        // That folder is replaced whenever the program is built or published again, and the data would go with it.
        var program = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

        var database = Assert.Throws<OptionsValidationException>(() => Read($"--PhotoStorage:DatabasePath={Path.Combine(program, "photosense.db")}"));
        await Assert.That(database!.Message).Contains("DatabasePath");
        await Assert.That(database.Message).Contains("inside the folder the service runs from");
        await Assert.That(() => Read($"--PhotoStorage:DatabasePath={Path.Combine(program, "data", "library", "photosense.db")}")).Throws<OptionsValidationException>();

        var thumbnails = Assert.Throws<OptionsValidationException>(() => Read($"--PhotoStorage:ThumbnailPath={Path.Combine(program, "thumbs")}"));
        await Assert.That(thumbnails!.Message).Contains("ThumbnailPath");

        // Beside it is fine, even under a name that begins the same way.
        await Assert.That(Read($"--PhotoStorage:DatabasePath={Path.Combine(program + "-data", "photosense.db")}").ResolveDatabasePath())
            .IsEqualTo(Path.Combine(program + "-data", "photosense.db"));
        // And so is the default, for the service as it really runs.
        await Assert.That(Read().ResolveDatabasePath()).IsEqualTo(Path.Combine(PhotoStorageOptions.DefaultDataFolder, "photosense.db"));
    }

    [Test]
    [Arguments(null, FormatPreference.WidelyCompatible)]               // nothing set: the JPEG is kept
    [Arguments("CameraOriginal", FormatPreference.CameraOriginal)]
    [Arguments("WidelyCompatible", FormatPreference.WidelyCompatible)]
    [Arguments("cameraoriginal", FormatPreference.CameraOriginal)]
    public async Task The_Format_To_Keep_Is_Read_From_The_Settings(string? setting, FormatPreference expected, CancellationToken ct)
    {
        // A setting that is not given is simply not there. (One given with no value is another matter: from .NET 10
        // it is read as "nothing", which for this setting would be the first of the two choices.)
        var options = Read(["--PhotoStorage:DatabasePath=photosense.db", .. setting is null ? Array.Empty<string>() : [$"--PhotoStorage:KeepFormat={setting}"]]);
        await Assert.That(options.KeepFormat).IsEqualTo(expected);
        await Assert.That(options.DatabasePath).IsEqualTo("photosense.db");
    }
}
