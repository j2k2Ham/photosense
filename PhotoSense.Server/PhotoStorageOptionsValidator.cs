using Microsoft.Extensions.Options;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Server;

/// <summary>Refuses settings that would lose the service's data: none named, or kept inside the program's own folder.</summary>
internal sealed class PhotoStorageOptionsValidator : IValidateOptions<PhotoStorageOptions>
{
    // The folder the service runs from, which must not hold its data.
    private readonly string _programFolder = Path.GetFullPath(AppContext.BaseDirectory);

    /// <summary>Fails when no database is named, or when the database or the previews would be kept inside the program's folder.</summary>
    /// <param name="name">The name of the options instance; unused, as there is only one.</param>
    /// <param name="options">The settings to check.</param>
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
