using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using PhotoSense.Domain.Configuration;
using Xunit;

namespace PhotoSense.Tests.Domain;

public class PhotoStorageOptionsValidatorTests
{
    [Fact]
    public void Validate_Success_When_Valid()
    {
        var validator = new PhotoStorageOptionsValidator();
        var result = validator.Validate(null, new PhotoStorageOptions{ DatabasePath="ok.db" });
        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void Validate_Fails_When_DatabasePath_Missing()
    {
        var validator = new PhotoStorageOptionsValidator();
        var result = validator.Validate(null, new PhotoStorageOptions{ DatabasePath="" });
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Data_Inside_The_Folder_The_Service_Runs_From_Is_Refused()
    {
        // Writing there makes the Functions host restart under a running scan, after which it answers nothing.
        var program = Path.Combine(Path.GetTempPath(), "photosense-app", "bin");
        var validator = new PhotoStorageOptionsValidator(program + Path.DirectorySeparatorChar);

        var database = validator.Validate(null, new PhotoStorageOptions { DatabasePath = Path.Combine(program, "photosense.db") });
        Assert.True(database.Failed);
        Assert.Contains("DatabasePath", database.FailureMessage);
        Assert.Contains("inside the folder the service runs from", database.FailureMessage);
        Assert.True(validator.Validate(null, new PhotoStorageOptions { DatabasePath = Path.Combine(program, "data", "library", "photosense.db") }).Failed);

        var thumbnails = validator.Validate(null, new PhotoStorageOptions { ThumbnailPath = Path.Combine(program, "thumbs") });
        Assert.True(thumbnails.Failed);
        Assert.Contains("ThumbnailPath", thumbnails.FailureMessage);

        // Beside it is fine, even under a name that begins the same way.
        Assert.True(validator.Validate(null, new PhotoStorageOptions { DatabasePath = Path.Combine(program + "-data", "photosense.db") }).Succeeded);
        Assert.True(validator.Validate(null, new PhotoStorageOptions { DatabasePath = Path.Combine(Path.GetTempPath(), "photosense-app", "photosense.db") }).Succeeded);
        // And so is the default, for the service as it really runs.
        Assert.True(new PhotoStorageOptionsValidator().Validate(null, new PhotoStorageOptions()).Succeeded);
    }

    [Theory]
    [InlineData(null, FormatPreference.WidelyCompatible)]               // nothing set: the JPEG is kept
    [InlineData("CameraOriginal", FormatPreference.CameraOriginal)]
    [InlineData("WidelyCompatible", FormatPreference.WidelyCompatible)]
    [InlineData("cameraoriginal", FormatPreference.CameraOriginal)]
    public void The_Format_To_Keep_Is_Read_From_The_Settings(string? setting, FormatPreference expected)
    {
        // As the service reads it: an environment variable PhotoStorage__KeepFormat arrives under this key.
        var settings = new Dictionary<string, string?> { ["PhotoStorage:DatabasePath"] = "photosense.db", ["PhotoStorage:KeepFormat"] = setting };
        var options = new ConfigurationBuilder().AddInMemoryCollection(settings).Build().GetSection("PhotoStorage").Get<PhotoStorageOptions>()!;
        Assert.Equal(expected, options.KeepFormat);
        Assert.Equal("photosense.db", options.DatabasePath);
    }
}
