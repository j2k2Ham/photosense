namespace PhotoSense.Tests;

/// <summary>The repository the tests were built from, for a test that checks a file kept in it.</summary>
internal static class Repository
{
    public static string Root { get; } = Find(AppContext.BaseDirectory);

    private static string Find(string from)
    {
        for (var dir = new DirectoryInfo(from); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "PhotoSense.sln"))) return dir.FullName;
        throw new DirectoryNotFoundException($"No PhotoSense.sln above {from}.");
    }
}
