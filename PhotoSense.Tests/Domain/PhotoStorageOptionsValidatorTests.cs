using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using PhotoSense.Domain.Configuration;

namespace PhotoSense.Tests.Domain;

public class PhotoStorageOptionsValidatorTests
{
    [Test]
    public async Task Validate_Success_When_Valid()
    {
        var validator = new PhotoStorageOptionsValidator();
        var result = validator.Validate(null, new PhotoStorageOptions{ DatabasePath="ok.db" });
        await Assert.That(result).IsEqualTo(ValidateOptionsResult.Success);
    }

    [Test]
    public async Task Validate_Fails_When_DatabasePath_Missing()
    {
        var validator = new PhotoStorageOptionsValidator();
        var result = validator.Validate(null, new PhotoStorageOptions{ DatabasePath="" });
        await Assert.That(result.Succeeded).IsFalse();
    }

    [Test]
    public async Task Data_Inside_The_Folder_The_Service_Runs_From_Is_Refused()
    {
        // Writing there makes the Functions host restart under a running scan, after which it answers nothing.
        var program = Path.Combine(Path.GetTempPath(), "photosense-app", "bin");
        var validator = new PhotoStorageOptionsValidator(program + Path.DirectorySeparatorChar);

        var database = validator.Validate(null, new PhotoStorageOptions { DatabasePath = Path.Combine(program, "photosense.db") });
        await Assert.That(database.Failed).IsTrue();
        await Assert.That(database.FailureMessage).Contains("DatabasePath");
        await Assert.That(database.FailureMessage).Contains("inside the folder the service runs from");
        await Assert.That(validator.Validate(null, new PhotoStorageOptions { DatabasePath = Path.Combine(program, "data", "library", "photosense.db") }).Failed).IsTrue();

        var thumbnails = validator.Validate(null, new PhotoStorageOptions { ThumbnailPath = Path.Combine(program, "thumbs") });
        await Assert.That(thumbnails.Failed).IsTrue();
        await Assert.That(thumbnails.FailureMessage).Contains("ThumbnailPath");

        // Beside it is fine, even under a name that begins the same way.
        await Assert.That(validator.Validate(null, new PhotoStorageOptions { DatabasePath = Path.Combine(program + "-data", "photosense.db") }).Succeeded).IsTrue();
        await Assert.That(validator.Validate(null, new PhotoStorageOptions { DatabasePath = Path.Combine(Path.GetTempPath(), "photosense-app", "photosense.db") }).Succeeded).IsTrue();
        // And so is the default, for the service as it really runs.
        await Assert.That(new PhotoStorageOptionsValidator().Validate(null, new PhotoStorageOptions()).Succeeded).IsTrue();
    }

    [Test]
    [Arguments(null, FormatPreference.WidelyCompatible)]               // nothing set: the JPEG is kept
    [Arguments("CameraOriginal", FormatPreference.CameraOriginal)]
    [Arguments("WidelyCompatible", FormatPreference.WidelyCompatible)]
    [Arguments("cameraoriginal", FormatPreference.CameraOriginal)]
    public async Task The_Format_To_Keep_Is_Read_From_The_Settings(string? setting, FormatPreference expected)
    {
        // As the service reads it: an environment variable PhotoStorage__KeepFormat arrives under this key.
        // A variable that is not set is simply not there. (A key that is there with no value is another matter:
        // from .NET 10 it is read as "nothing", which for this setting would be the first of the two choices.)
        var settings = new Dictionary<string, string?> { ["PhotoStorage:DatabasePath"] = "photosense.db" };
        if (setting is not null) settings["PhotoStorage:KeepFormat"] = setting;
        var options = new ConfigurationBuilder().AddInMemoryCollection(settings).Build().GetSection("PhotoStorage").Get<PhotoStorageOptions>()!;
        await Assert.That(options.KeepFormat).IsEqualTo(expected);
        await Assert.That(options.DatabasePath).IsEqualTo("photosense.db");
    }
}
