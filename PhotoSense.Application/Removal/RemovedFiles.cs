using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
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
    private readonly IPhotoRepository _repo;
    private readonly IOrganizeBatchStore _batches;
    private readonly IAuditRepository _audit;
    private readonly IPhotoFileSystem _files;

    public RemovedFiles(IPhotoRepository repo, IOrganizeBatchStore batches, IAuditRepository audit, IPhotoFileSystem files)
    {
        _repo = repo;
        _batches = batches;
        _audit = audit;
        _files = files;
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
                if (Outermost(Path.GetDirectoryName(item.To)) is { } folder && _files.FolderExists(folder)) Add(folder);

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
            // Gone since it was listed, or a link: neither is touched.
            if (!_files.FolderExists(path) || _files.IsLink(path)) continue;
            emptied++;
            var inside = Throughout(path);
            foreach (var (file, size) in FilesIn(inside))
            {
                try
                {
                    _files.Erase(file);
                    erased++;
                    bytes += size;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped++;
                    problems.Add($"{Path.GetFileName(file)}: {ex.Message}");
                }
            }
            // The folders go too, deepest first. One that still holds something stays.
            foreach (var folder in inside.OrderByDescending(d => d.Length))
            {
                try { _files.RemoveEmptyFolder(folder); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* stays */ }
            }
        }

        // A removal whose files are all gone can no longer be undone, and is no longer offered to be.
        foreach (var batch in await RemovalsAsync(ct))
            if (!batch.Items.Exists(i => _files.FileExists(i.To))) await _batches.MarkUndoneAsync(batch.Id, ct);
        await _audit.AddAsync(new AuditEntry { Action = "EraseRemoved", Details = $"{erased} files erased from {emptied} folders, {skipped} left" }, ct);
        return new EraseResult(erased, bytes, skipped, problems);
    }

    private async Task<IEnumerable<OrganizeBatch>> RemovalsAsync(CancellationToken ct) => (await _batches.RecentAsync(int.MaxValue, ct)).Where(b => b.Removed);

    private bool IsFolder(string? path) => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && _files.FolderExists(path);

    private static bool IsHeld(string folder) => Path.GetFileName(folder).Equals(PhotoStorageOptions.RemovedFolderName, StringComparison.OrdinalIgnoreCase);

    // The folder of removed files a folder is, or is in: the outermost, should one ever be inside another.
    private static string? Outermost(string? folder)
    {
        string? found = null;
        for (var at = folder; at is not null; at = Path.GetDirectoryName(at))
            if (IsHeld(at)) found = at;
        return found;
    }

    // Looked for at any depth, as a scan leaves them out at any depth; what is inside one, or inside a link, is not looked through.
    private IEnumerable<string> HeldUnder(string root)
    {
        var pending = new Stack<string>([root]);
        while (pending.TryPop(out var folder))
        {
            foreach (var inside in _files.FoldersIn(folder))
            {
                if (_files.IsLink(inside)) continue;
                if (IsHeld(inside)) yield return inside;
                else pending.Push(inside);
            }
        }
    }

    // A folder and every folder inside it. A link is not among them: it is neither followed nor taken away.
    private List<string> Throughout(string folder)
    {
        var all = new List<string>();
        var pending = new Stack<string>([folder]);
        while (pending.TryPop(out var at))
        {
            all.Add(at);
            foreach (var inside in _files.FoldersIn(at))
                if (!_files.IsLink(inside)) pending.Push(inside);
        }
        return all;
    }

    // Each file in the folders, hidden ones too, with its size. A link is left as it is, and so is the folder it
    // is in; a file gone since its folder was read has nothing of it to count or erase.
    private IEnumerable<(string Path, long Bytes)> FilesIn(IEnumerable<string> folders)
    {
        foreach (var folder in folders)
            foreach (var file in _files.FilesIn(folder))
                if (!_files.IsLink(file) && _files.Describe(file) is { } stored) yield return (file, stored.Length);
    }

    private HeldFolder Measure(string folder)
    {
        var files = FilesIn(Throughout(folder)).ToList();
        return new HeldFolder(folder, files.Count, files.Sum(f => f.Bytes));
    }
}
