using PhotoSense.Domain.Services;

namespace PhotoSense.Domain.Repositories;

/// <summary>What was read from a file, with the size and time that say whether the file is still the one that was read.</summary>
public sealed record StoredMediaDetails(string Path, long SizeBytes, DateTime ModifiedUtc, MediaDetails Details);

/// <summary>
/// Keeps what Organize read from files no scan has been through, so that a folder is slow to list once
/// and not every time the service is started.
/// </summary>
public interface IMediaDetailsStore
{
    /// <summary>What is kept about the file at a path, if anything.</summary>
    StoredMediaDetails? At(string path);

    /// <summary>What is kept about every file in a folder and the folders inside it.</summary>
    IReadOnlyList<StoredMediaDetails> Under(string folder);

    /// <summary>Keeps these, in place of whatever was kept for the same paths.</summary>
    void Save(IEnumerable<StoredMediaDetails> details);
}
