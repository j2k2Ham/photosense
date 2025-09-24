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
}
