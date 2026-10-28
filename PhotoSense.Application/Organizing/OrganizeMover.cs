using PhotoSense.Application.Changes;
using PhotoSense.Application.Removal;
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
/// Moves or copies files into a folder, takes files out of their folders, and can take any of it back, each
/// as a whole: when one file cannot go, none does. Nothing is ever replaced: a file arriving under a name
/// that is taken gets a number, whatever name was asked for. Nothing is ever erased: a file that is removed
/// goes to the folder that holds removed files.
/// </summary>
public sealed class OrganizeMover
{
    private readonly OrganizeLibrary _library;
    private readonly ICompanionFileFinder _companions;
    private readonly FileChanges _changes;
    private readonly IAuditRepository _audit;
    private readonly IPhotoFileSystem _files;

    public OrganizeMover(OrganizeLibrary library, ICompanionFileFinder companions, FileChanges changes, IAuditRepository audit, IPhotoFileSystem files)
    {
        _library = library;
        _companions = companions;
        _changes = changes;
        _audit = audit;
        _files = files;
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
    /// does that. What a scan recorded of a removed file is dropped, as it is when Clean up removes one, and
    /// an undo brings it back.
    /// </summary>
    /// <param name="root">The folder being organized.</param>
    public Task<OrganizeResult> RemoveAsync(string root, IReadOnlyList<string> ids, CancellationToken ct = default)
    {
        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return CarryOutAsync(new OrganizeBatch { Label = folder, Removed = true }, ids.Select(id => (id, (string?)null)).ToList(), bringCompanions: true,
            (file, _) => Path.GetDirectoryName(RemovedPlace.For(file.Path, folder))!, "OrganizeRemove", Path.Combine(folder, PhotoStorageOptions.RemovedFolderName), ct);
    }

    private async Task<OrganizeResult> CarryOutAsync(OrganizeBatch batch, IReadOnlyList<(string Id, string? Name)> files, bool bringCompanions,
        Func<LibraryFile, int, string> folderOf, string action, string where, CancellationToken ct)
    {
        var problems = new List<string>();
        // Each file asked for that is to go, by its id, and the place it is going to.
        var asked = new List<(string Id, OrganizeBatchItem Item)>();
        // Files going along with the picture they belong to.
        var carried = new Dictionary<string, OrganizeBatchItem>(PhotoPath.Comparer);
        var going = new HashSet<string>(PhotoPath.Comparer);
        var taken = new HashSet<string>(PhotoPath.Comparer);
        var siblings = new SiblingIndex(_files);
        var skipped = 0;

        for (var i = 0; i < files.Count; i++)
        {
            var (id, wanted) = files[i];
            // Asked for as well, a file that goes along with its picture goes once: whoever asked is told where to.
            if (_library.PathOf(id) is { } was && carried.TryGetValue(was, out var along))
            {
                asked.Add((id, along));
                continue;
            }
            if (await _library.FindAsync(id, ct) is not { } file)
            {
                skipped++;
                problems.Add("A file is no longer where it was. Choose the folder again to see what is there now.");
                continue;
            }
            var folder = folderOf(file, i);
            if (PhotoPath.Comparer.Equals(file.Folder, folder)) continue;

            var item = Plan(batch, file.Path, folder, NameRules.Usable(wanted, file.Name) ? wanted! : file.Name, companion: false, taken, going);
            asked.Add((id, item));
            var others = bringCompanions ? siblings.SiblingsOf(file.Path, stillThere: true).Where(s => !going.Contains(s)).ToList() : [];
            foreach (var companion in others.Count > 0 ? _companions.FindAmong(file.Path, others) : [])
            {
                if (!_files.FileExists(companion))
                {
                    problems.Add($"{Path.GetFileName(companion)} is no longer where it was, so it stays behind.");
                    continue;
                }
                // It keeps the picture's name where it shared it, so that the two still belong together.
                var sharesName = string.Equals(Path.GetFileNameWithoutExtension(companion), Path.GetFileNameWithoutExtension(file.Name), StringComparison.OrdinalIgnoreCase);
                var name = sharesName ? Path.GetFileNameWithoutExtension(item.To) + Path.GetExtension(companion) : Path.GetFileName(companion);
                carried[companion] = Plan(batch, companion, folder, name, companion: true, taken, going);
            }
        }

        if (batch.Items.Count == 0) return new OrganizeResult(null, 0, 0, 0, 0, skipped, problems, []);
        var outcome = await _changes.CarryOutAsync(batch, ct: ct);
        if (!outcome.Done) return new OrganizeResult(null, 0, 0, 0, 0, skipped + asked.Count, [outcome.Problem!, .. problems], []);

        foreach (var item in batch.Items) _library.Arrived(item.From, item.To, copied: batch.Copy);
        var companions = batch.Items.Count(i => i.Companion);
        var renamed = asked.Count(a => !a.Item.Companion && !string.Equals(Path.GetFileName(a.Item.To), Path.GetFileName(a.Item.From), StringComparison.Ordinal));
        await _audit.AddAsync(new AuditEntry { Action = action, PhotoId = batch.Id.ToString(), Details = $"{batch.Files} files to {where} with {companions} linked files" }, ct);
        return new OrganizeResult(batch.Id, batch.Files, batch.Bytes, companions, renamed, skipped, problems, asked.Select(a => (a.Id, a.Item.To)).ToList());
    }

    // A file's place in the change: under the name asked for, or that name numbered when it is taken on disk or by another file going there.
    private OrganizeBatchItem Plan(OrganizeBatch batch, string from, string folder, string name, bool companion, HashSet<string> taken, HashSet<string> going)
    {
        _changes.NeedFolder(batch.CreatedFolders, folder);
        var item = new OrganizeBatchItem { From = from, To = _changes.FreePlace(Path.Combine(folder, name), taken), Companion = companion };
        batch.Items.Add(item);
        going.Add(from);
        return item;
    }

    public async Task<OrganizeUndoResult> UndoAsync(Guid batchId, CancellationToken ct = default)
    {
        var undone = await _changes.UndoAsync(batchId, ct);
        if (!undone.Found) return new OrganizeUndoResult(false, 0, 0, []);
        if (!undone.Done) return new OrganizeUndoResult(true, 0, undone.Back.Count(i => !i.Companion), [undone.Problem!]);
        if (!undone.Copies)
            foreach (var item in undone.Back) _library.Arrived(item.To, item.Back!, copied: false);
        var restored = undone.Back.Count(i => !i.Companion);
        await _audit.AddAsync(new AuditEntry { Action = "OrganizeUndo", PhotoId = batchId.ToString(), Details = $"{restored} files put back, {undone.Left.Count} left" }, ct);
        return new OrganizeUndoResult(true, restored, undone.Left.Count, undone.Left);
    }
}
