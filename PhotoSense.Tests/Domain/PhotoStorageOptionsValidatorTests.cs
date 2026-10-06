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
