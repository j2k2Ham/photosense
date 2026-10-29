using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Application.Organizing;

/// <summary>What a move, copy or removal did.</summary>
/// <param name="BatchId">What to undo it by; absent when nothing was moved.</param>
/// <param name="Done">Files asked for that went.</param>
/// <param name="Companions">Live Photo videos and edit files that went with them.</param>
/// <param name="Renamed">Files that arrived under another name so that nothing was replaced.</param>
/// <param name="Items">Where each file that went now is (for a copy, where the copy is), by the id it was asked for by: a file that was asked for and went along with its picture is among them.</param>
public sealed record OrganizeResult(Guid? BatchId, int Done, long Bytes, int Companions, int Renamed, int Skipped,
    IReadOnlyList<string> Problems, IReadOnlyList<(string Id, string Path)> Items);

/// <param name="Found">There was such a move, not yet undone.</param>
/// <param name="Restored">Files asked for that were put back, or copies of them that were removed.</param>
public sealed record OrganizeUndoResult(bool Found, int Restored, int Skipped, IReadOnlyList<string> Problems);

/// <summary>
/// Moves or copies files into a folder, takes files out of their folders, and can take any of it back.
/// Nothing is ever replaced: a file arriving under a name that is taken gets a number, whatever name was
/// asked for. Nothing is ever erased: a file that is removed goes to the folder that holds removed files.
/// </summary>
public sealed class OrganizeMover
{
    private readonly OrganizeLibrary _library;
    private readonly ICompanionFileFinder _companions;
    private readonly IPhotoRepository _repo;
    private readonly IOrganizeBatchStore _batches;
    private readonly IAuditRepository _audit;

    public OrganizeMover(OrganizeLibrary library, ICompanionFileFinder companions, IPhotoRepository repo, IOrganizeBatchStore batches, IAuditRepository audit)
    {
        _library = library;
        _companions = companions;
        _repo = repo;
        _batches = batches;
        _audit = audit;
    }

    /// <param name="bringCompanions">Bring the Live Photo videos and edit files that belong to the files and to nothing else.</param>
    /// <param name="files">The files by id, each with the name it should arrive under (its own when none is given).</param>
    /// <param name="subfolders">A subfolder of the destination for each file, in the order of the files; none for a file leaves it to the destination.</param>
    public Task<OrganizeResult> ApplyAsync(OrganizeDestination destination, bool copy, bool bringCompanions, string label,
        IReadOnlyList<(string Id, string? Name)> files, IReadOnlyList<string?>? subfolders = null, CancellationToken ct = default)
        => CarryOutAsync(new OrganizeBatch { Label = label, Copy = copy }, files, bringCompanions,
            (file, i) => destination.FolderFor(file, subfolders?.ElementAtOrDefault(i)), copy ? "OrganizeCopy" : "OrganizeMove", destination.Folder, ct);

    /// <summary>
    /// Takes files out of their folders, with the Live Photo videos and edit files that belong to them and to
    /// nothing else. They go to the folder that holds removed files inside <paramref name="root"/>, each
    /// keeping its place relative to the root, so that putting one back is a matter of moving it up; an undo
    /// does that. What a scan recorded of a removed file is dropped, as it is when Clean up removes one.
    /// </summary>
    /// <param name="root">The folder being organized.</param>
    public Task<OrganizeResult> RemoveAsync(string root, IReadOnlyList<string> ids, CancellationToken ct = default)
    {
        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var held = Path.Combine(folder, PhotoStorageOptions.RemovedFolderName);
        string HeldFor(LibraryFile file, int _)
        {
            var target = Path.GetFullPath(Path.Combine(held, Path.GetRelativePath(folder, file.Folder)));
            // A file from outside the root is held beside itself.
            var inside = (PhotoPath.Key(target) + Path.DirectorySeparatorChar).StartsWith(PhotoPath.Key(held) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            return inside ? target : Path.Combine(file.Folder, PhotoStorageOptions.RemovedFolderName);
        }
        return CarryOutAsync(new OrganizeBatch { Label = folder, Removed = true }, ids.Select(id => (id, (string?)null)).ToList(), bringCompanions: true, HeldFor, "OrganizeRemove", held, ct);
    }

    private async Task<OrganizeResult> CarryOutAsync(OrganizeBatch batch, IReadOnlyList<(string Id, string? Name)> files, bool bringCompanions,
        Func<LibraryFile, int, string> folderOf, string action, string where, CancellationToken ct)
    {
        var problems = new List<string>();
        var items = new List<(string, string)>();
        // Files that have already gone along with the picture they belong to, and where each now is.
        var carried = new Dictionary<string, string>(PhotoPath.Comparer);
        var siblings = new SiblingIndex();
        int renamed = 0, skipped = 0, companionCount = 0;

        for (var i = 0; i < files.Count; i++)
        {
            var (id, wanted) = files[i];
            if (await _library.FindAsync(id, ct) is not { } file)
            {
                // Asked for as well, a file that went along with its picture has gone too: whoever asked is told where it is.
                if (_library.PathOf(id) is { } was && carried.TryGetValue(was, out var now))
                {
                    items.Add((id, now));
                    continue;
                }
                skipped++;
                problems.Add("A file is no longer where it was. Choose the folder again to see what is there now.");
                continue;
            }
            var folder = folderOf(file, i);
            if (PhotoPath.Comparer.Equals(file.Folder, folder)) continue;

            // Worked out while the file is still in place.
            var others = bringCompanions ? siblings.SiblingsOf(file.Path, stillThere: true) : [];
            var companions = others.Count > 0 ? _companions.FindAmong(file.Path, others) : [];
            string arrived;
            try
            {
                MakeFolder(folder, batch.CreatedFolders);
                arrived = await TransferAsync(file.Path, folder, NameRules.Usable(wanted, file.Name) ? wanted! : file.Name, batch, companion: false, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++;
                problems.Add($"{file.Name}: {ex.Message}");
                continue;
            }
            batch.Files++;
            batch.Bytes += file.SizeBytes;
            items.Add((id, arrived));
            if (!string.Equals(Path.GetFileName(arrived), file.Name, StringComparison.Ordinal)) renamed++;

            foreach (var companion in companions)
            {
                // It keeps the picture's name where it shared it, so that the two still belong together.
                var sharesName = string.Equals(Path.GetFileNameWithoutExtension(companion), Path.GetFileNameWithoutExtension(file.Name), StringComparison.OrdinalIgnoreCase);
                var name = sharesName ? Path.GetFileNameWithoutExtension(arrived) + Path.GetExtension(companion) : Path.GetFileName(companion);
                try
                {
                    carried[companion] = await TransferAsync(companion, folder, name, batch, companion: true, ct);
                    companionCount++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    problems.Add($"{Path.GetFileName(companion)}: {ex.Message}");
                }
            }
        }

        if (batch.Items.Count == 0) return new OrganizeResult(null, 0, 0, 0, 0, skipped, problems, items);
        await _batches.AddAsync(batch, ct);
        await _audit.AddAsync(new AuditEntry { Action = action, PhotoId = batch.Id.ToString(), Details = $"{batch.Files} files to {where} with {companionCount} linked files" }, ct);
        return new OrganizeResult(batch.Id, batch.Files, batch.Bytes, companionCount, renamed, skipped, problems, items);
    }

    public async Task<OrganizeUndoResult> UndoAsync(Guid batchId, CancellationToken ct = default)
    {
        if (await _batches.GetAsync(batchId, ct) is not { Undone: false } batch) return new OrganizeUndoResult(false, 0, 0, []);
        var problems = new List<string>();
        int restored = 0, skipped = 0;

        foreach (var item in Enumerable.Reverse(batch.Items))
        {
            var name = Path.GetFileName(item.To);
            try
            {
                var there = new FileInfo(item.To);
                string? problem = !there.Exists ? $"{name} is no longer where it was put."
                    : batch.Copy ? RemoveCopy(there, item)
                    : File.Exists(item.From) ? $"{name} was left where it is: another file now has its old place."
                    : null;
                if (problem is null && !batch.Copy)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(item.From)!);
                    File.Move(item.To, item.From);
                    await RecordMoveAsync(item.To, item.From, ct);
                }
                if (problem is not null) { skipped++; problems.Add(problem); }
                else if (!item.Companion) restored++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++;
                problems.Add($"{name}: {ex.Message}");
            }
        }

        // The folders that were made for the files go again, where the undo has left them empty.
        foreach (var folder in batch.CreatedFolders.OrderByDescending(f => f.Length))
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);

        await _batches.MarkUndoneAsync(batchId, ct);
        await _audit.AddAsync(new AuditEntry { Action = "OrganizeUndo", PhotoId = batchId.ToString(), Details = $"{restored} files put back, {skipped} left" }, ct);
        return new OrganizeUndoResult(true, restored, skipped, problems);
    }

    // A copy goes only while it is still the file that was written and what it was copied from is still there.
    private static string? RemoveCopy(FileInfo copy, OrganizeBatchItem item)
    {
        if (copy.Length != item.SizeBytes || copy.LastWriteTimeUtc.Ticks != item.ModifiedUtcTicks) return $"{copy.Name} has changed since it was copied, so it was left.";
        if (!File.Exists(item.From)) return $"{copy.Name} was left: the file it was copied from is no longer there.";
        copy.Delete();
        return null;
    }

    private async Task<string> TransferAsync(string from, string folder, string name, OrganizeBatch batch, bool companion, CancellationToken ct)
    {
        if (File.Exists(Path.Combine(folder, name))) name = NameRules.NextFree(name, n => File.Exists(Path.Combine(folder, n)));
        var to = Path.Combine(folder, name);
        // Neither replaces a file: should one appear under the name at this very moment, the transfer fails.
        if (batch.Copy) File.Copy(from, to, overwrite: false);
        else File.Move(from, to, overwrite: false);

        var arrived = new FileInfo(to);
        batch.Items.Add(new OrganizeBatchItem { From = from, To = to, SizeBytes = arrived.Length, ModifiedUtcTicks = arrived.LastWriteTimeUtc.Ticks, Companion = companion });
        if (batch.Copy) _library.Arrived(from, to, copied: true);
        else if (batch.Removed) await ForgetAsync(from, to, ct);
        else await RecordMoveAsync(from, to, ct);
        return to;
    }

    // A scan's record of the file follows it, so that the Clean up screens still find it.
    private async Task RecordMoveAsync(string from, string to, CancellationToken ct)
    {
        _library.Arrived(from, to, copied: false);
        if (await _repo.GetByPathAsync(from, ct) is { } photo) await _repo.AddOrUpdateAsync(photo.MovedTo(to), ct);
    }

    // A removed file is no longer one of the scanned pictures: the Clean up screens must not go on listing it.
    private async Task ForgetAsync(string from, string to, CancellationToken ct)
    {
        _library.Arrived(from, to, copied: false);
        if (await _repo.GetByPathAsync(from, ct) is { } photo) await _repo.DeleteAsync(photo.Id, ct);
    }

    private static void MakeFolder(string folder, List<string> created)
    {
        var missing = new List<string>();
        // The folder is inside one that is there, so going up from it always arrives at a folder that exists.
        for (var at = folder; !Directory.Exists(at); at = Path.GetDirectoryName(at)!) missing.Add(at);
        Directory.CreateDirectory(folder);
        created.AddRange(missing.Where(m => !created.Contains(m, PhotoPath.Comparer)));
    }
}
