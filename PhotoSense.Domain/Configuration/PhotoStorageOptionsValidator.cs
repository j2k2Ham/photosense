using Microsoft.Extensions.Options;

namespace PhotoSense.Domain.Configuration;

public class PhotoStorageOptionsValidator : IValidateOptions<PhotoStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, PhotoStorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DatabasePath))
            return ValidateOptionsResult.Fail("DatabasePath must be provided");
        return ValidateOptionsResult.Success;
    }
}
