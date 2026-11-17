namespace PhotoSense.Application.Organizing;

/// <summary>A picture or video as Library shows it: a Live Photo's video goes with its picture, as one.</summary>
/// <param name="Main">The picture, or the video standing alone.</param>
/// <param name="Files">Every file of it, the main one first.</param>
internal sealed record PhotoItem(LibraryFile Main, IReadOnlyList<LibraryFile> Files)
{
    /// <summary>What all its files take.</summary>
    public long Bytes => Files.Sum(f => f.SizeBytes);

    /// <summary>The library's files as pictures and videos, each with its files, the latest first.</summary>
    public static List<PhotoItem> Of(IEnumerable<LibraryFile> files)
    {
        var all = files.ToList();
        var pictures = all.Where(f => !f.IsVideo && f.Details.LivePhotoId is not null).GroupBy(f => f.Details.LivePhotoId!).ToDictionary(g => g.Key, g => g.First());
        var items = new Dictionary<string, (LibraryFile Main, List<LibraryFile> Files)>(StringComparer.Ordinal);
        foreach (var file in all.OrderBy(f => f.IsVideo))
        {
            var main = file.IsVideo && file.Details.LivePhotoId is { } live && pictures.TryGetValue(live, out var picture) ? picture : file;
            if (!items.TryGetValue(main.Id, out var item)) items[main.Id] = item = (main, []);
            item.Files.Add(file);
        }
        return [.. items.Values.OrderByDescending(i => i.Main.Date).Select(i => new PhotoItem(i.Main, i.Files))];
    }
}
