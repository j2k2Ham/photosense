using Microsoft.Extensions.Options;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Domain.Configuration;

public class PhotoStorageOptionsValidator : IValidateOptions<PhotoStorageOptions>
{
    private readonly string _programFolder;

    public PhotoStorageOptionsValidator() : this(AppContext.BaseDirectory) { }

    /// <param name="programFolder">The folder the service runs from, which must not hold its data.</param>
    public PhotoStorageOptionsValidator(string programFolder) => _programFolder = Path.GetFullPath(programFolder);

    public ValidateOptionsResult Validate(string? name, PhotoStorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DatabasePath))
            return ValidateOptionsResult.Fail("DatabasePath must be provided");
        foreach (var (setting, path) in new[] { ("DatabasePath", options.ResolveDatabasePath()), ("ThumbnailPath", options.ResolveThumbnailPath()) })
        {
            // That folder is replaced whenever the program is built or published again, and the data would go with it.
            if (IsInside(path, _programFolder))
                return ValidateOptionsResult.Fail($"{setting} ({path}) is inside the folder the service runs from ({_programFolder}). Data must be kept somewhere else.");
        }
        return ValidateOptionsResult.Success;
    }

    private static bool IsInside(string path, string folder)
        => (PhotoPath.Key(path) + Path.DirectorySeparatorChar).StartsWith(Path.TrimEndingDirectorySeparator(PhotoPath.Key(folder)) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
}
