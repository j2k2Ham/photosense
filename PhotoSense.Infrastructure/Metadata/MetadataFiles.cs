using MetadataExtractor;
using PhotoSense.Domain.Services;
using Directory = MetadataExtractor.Directory;

namespace PhotoSense.Infrastructure.Metadata;

/// <summary>What a picture or video says about itself, read by MetadataExtractor, for every reader of it to share.</summary>
internal static class MetadataFiles
{
    /// <summary>Opens a file and reads what it says about itself.</summary>
    /// <param name="files">Where the photos are kept.</param>
    /// <param name="path">The file.</param>
    /// <param name="use">Takes what was read; it runs only when the file could be read.</param>
    /// <param name="ct">Stops the opening.</param>
    /// <returns>Whether the file could be opened and read.</returns>
    public static async Task<bool> TryReadAsync(IPhotoFileSystem files, string path, Action<IReadOnlyList<Directory>> use, CancellationToken ct)
    {
        Stream stream;
        try { stream = await files.OpenReadAsync(path, ct); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        await using (stream) return TryRead(stream, use);
    }

    /// <summary>Reads what the file at the start of a stream says about itself.</summary>
    /// <param name="stream">The file, from its start.</param>
    /// <param name="use">Takes what was read; it runs only when the file could be read.</param>
    /// <returns>Whether the file could be read.</returns>
    public static bool TryRead(Stream stream, Action<IReadOnlyList<Directory>> use)
    {
        try
        {
            use(ImageMetadataReader.ReadMetadata(stream));
            return true;
        }
        // The one catch of every error PhotoSense keeps (CLAUDE.md): on a damaged file MetadataExtractor throws kinds
        // of error it does not document, and so can what is made of its answer, and one damaged file must not stop
        // a scan or a listing of thousands. A stop is not an error of the file's, and goes on up.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }
}
