using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Application.Removal;

/// <summary>A folder that holds removed files, with how much is in it.</summary>
public sealed record HeldFolder(string Path, int Files, long Bytes);

/// <summary>What erasing did.</summary>
/// <param name="Skipped">Files that could not be erased and are still there.</param>
public sealed record EraseResult(int Erased, long Bytes, int Skipped, IReadOnlyList<string> Problems);

/// <summary>
/// The files Clean up and Organize have removed. They wait in a folder of their own inside the folder they
/// were removed from, where they can be put back; this is the one place they are erased for good.
/// </summary>
public sealed class RemovedFiles
{
    // Hidden and system files are part of a folder too; a link is neither followed nor taken away.
    private static readonly EnumerationOptions Here = new() { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
    private static readonly EnumerationOptions Throughout = new() { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, RecurseSubdirectories = true };

    private readonly IPhotoRepository _repo;
    private readonly IOrganizeBatchStore _batches;
    private readonly IAuditRepository _audit;
    private readonly Action<string> _erase;

    /// <param name="eraseFile">How a file is erased; the file system's own way when not given.</param>
    public RemovedFiles(IPhotoRepository repo, IOrganizeBatchStore batches, IAuditRepository audit, Action<string>? eraseFile = null)
    {
        _repo = repo;
        _batches = batches;
        _audit = audit;
        _erase = eraseFile ?? File.Delete;
    }

    /// <summary>
    /// Every folder of removed files that holds something: those inside the folders given and inside every
    /// folder a scan on record covered, wherever in them they are, and those Organize put files in.
    /// </summary>
    public async Task<IReadOnlyList<HeldFolder>> FindAsync(IEnumerable<string?> roots, CancellationToken ct = default)
    {
        var held = new SortedDictionary<string, string>(StringComparer.Ordinal);
        void Add(string folder) => held.TryAdd(PhotoPath.Key(folder), folder);

        var scanned = (await _repo.GetAllAsync(ct)).Select(p => p.ScanRoot);
        foreach (var root in roots.Concat(scanned).Where(IsFolder).Select(r => Path.TrimEndingDirectorySeparator(Path.GetFullPath(r!))).Distinct(PhotoPath.Comparer))
            foreach (var folder in HeldUnder(root)) Add(Outermost(folder)!);
        // A file Organize took from outside the folder being organized is held beside itself, which no folder above names.
        foreach (var batch in await RemovalsAsync(ct))
            foreach (var item in batch.Items)
                if (Outermost(Path.GetDirectoryName(item.To)) is { } folder && Directory.Exists(folder)) Add(folder);

        return held.Values.Select(Measure).Where(f => f.Files > 0).ToList();
    }

    /// <summary>
    /// Erases everything in the folders given, and the folders with it. This cannot be taken back. A file
    /// that cannot be erased is left, with the folders it is in.
    /// </summary>
    /// <exception cref="ArgumentException">One of them is not a folder of removed files; nothing is erased then.</exception>
    public async Task<EraseResult> EraseAsync(IEnumerable<string?> folders, CancellationToken ct = default)
    {
        var asked = folders.ToList();
        // Only the folders PhotoSense itself puts removed files in are ever emptied, whatever is asked.
        foreach (var folder in asked)
            if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder) || !IsHeld(Path.TrimEndingDirectorySeparator(folder)))
                throw new ArgumentException($"Not a folder of removed files: {folder}");

        var problems = new List<string>();
        int erased = 0, skipped = 0, emptied = 0;
        long bytes = 0;
        foreach (var path in asked.Select(f => Path.TrimEndingDirectorySeparator(Path.GetFullPath(f!))).Distinct(PhotoPath.Comparer))
        {
            var held = new DirectoryInfo(path);
            // Gone since it was listed, or a link to somewhere else: neither is touched.
            if (!held.Exists || held.LinkTarget is not null) continue;
            emptied++;
            foreach (var file in held.EnumerateFiles("*", Throughout).ToList())
            {
                try
                {
                    var size = file.Length;
                    // A file marked read-only cannot be erased until the mark is taken off.
                    if (file.IsReadOnly) file.IsReadOnly = false;
                    _erase(file.FullName);
                    erased++;
                    bytes += size;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped++;
                    problems.Add($"{file.Name}: {ex.Message}");
                }
            }
            // The folders go too, deepest first. One that still holds something stays.
            foreach (var folder in held.EnumerateDirectories("*", Throughout).Select(d => d.FullName).OrderByDescending(d => d.Length).Append(held.FullName).ToList())
            {
                try { Directory.Delete(folder); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* not empty */ }
            }
        }

        // A removal whose files are all gone can no longer be undone, and is no longer offered to be.
        foreach (var batch in await RemovalsAsync(ct))
            if (!batch.Items.Exists(i => File.Exists(i.To))) await _batches.MarkUndoneAsync(batch.Id, ct);
        await _audit.AddAsync(new AuditEntry { Action = "EraseRemoved", Details = $"{erased} files erased from {emptied} folders, {skipped} left" }, ct);
        return new EraseResult(erased, bytes, skipped, problems);
    }

    private async Task<IEnumerable<OrganizeBatch>> RemovalsAsync(CancellationToken ct) => (await _batches.RecentAsync(int.MaxValue, ct)).Where(b => b.Removed);

    private static bool IsFolder(string? path) => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && Directory.Exists(path);

    private static bool IsHeld(string folder) => Path.GetFileName(folder).Equals(PhotoStorageOptions.RemovedFolderName, StringComparison.OrdinalIgnoreCase);

    // The folder of removed files a folder is, or is in: the outermost, should one ever be inside another.
    private static string? Outermost(string? folder)
    {
        string? found = null;
        for (var at = folder; at is not null; at = Path.GetDirectoryName(at))
            if (IsHeld(at)) found = at;
        return found;
    }

    // Looked for at any depth, as a scan leaves them out at any depth; what is inside one is not looked through.
    private static IEnumerable<string> HeldUnder(string root)
    {
        var pending = new Stack<string>([root]);
        while (pending.TryPop(out var folder))
        {
            foreach (var inside in Directory.EnumerateDirectories(folder, "*", Here))
            {
                if (IsHeld(inside)) yield return inside;
                else pending.Push(inside);
            }
        }
    }

    private static HeldFolder Measure(string folder)
    {
        int files = 0;
        long bytes = 0;
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", Throughout))
        {
            files++;
            bytes += file.Length;
        }
        return new HeldFolder(folder, files, bytes);
    }
}
