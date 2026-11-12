using PhotoSense.Domain.Services;

namespace PhotoSense.Application.Changes;

/// <summary>Takes away the folders a change made or emptied, once it is through with them.</summary>
internal static class EmptyFolders
{
    /// <summary>
    /// Takes away those of the folders that are empty, deepest first, handed over whole where the photos are kept
    /// so. A folder that will not go stays: nothing depends on its going, and the change is done either way.
    /// </summary>
    public static void TakeAway(IPhotoFileSystem files, IEnumerable<string> folders)
    {
        var deepestFirst = folders.OrderByDescending(f => f.Length).ToList();
        if (files is IPhotoChangesTogether together)
        {
            _ = TryTakeAway(() => together.RemoveEmptyFolders(deepestFirst));
            return;
        }
        foreach (var folder in deepestFirst) _ = TryTakeAway(() => files.RemoveEmptyFolder(folder));
    }

    /// <summary>Whether the folders went; false when the system would not take them away.</summary>
    private static bool TryTakeAway(Action takeAway)
    {
        try
        {
            takeAway();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
