using PhotoSense.Application.Organizing;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Application.Changes;

/// <param name="Done">The change was carried out in full. When it was not, none of it was.</param>
/// <param name="Problem">Why it was not, naming the file.</param>
/// <param name="Left">Files it let go of after all, as it was told to, and put straight back.</param>
public sealed record ChangeResult(bool Done, string? Problem, IReadOnlyList<OrganizeBatchItem> Left);

/// <param name="Found">There was such a change, carried out and not undone.</param>
/// <param name="Done">The undo was carried out in full. When it was not, none of it was.</param>
/// <param name="Copies">The change was a copy, so undoing it deleted copies rather than moving files back.</param>
/// <param name="Back">
/// The files put back, each with where it went in <see cref="OrganizeBatchItem.Back"/>; for a copy, the copies
/// deleted. When the undo was not carried out, the files it was to put back, all of which are where they were.
/// </param>
/// <param name="Left">Why each file that could not be put back was left.</param>
public sealed record UndoResult(bool Found, bool Done, bool Copies, IReadOnlyList<OrganizeBatchItem> Back, IReadOnlyList<string> Left, string? Problem);

/// <summary>
/// Carries out moves, copies and removals of files as a whole. Each is written down before any file is
/// touched, and when one of its files cannot go, those that went are put back: it happens in full or not at
/// all, and so does undoing it. A change that a stop of the service cut short is put back as it was before
/// it began, the next time the service starts.
/// </summary>
public sealed class FileChanges
{
    private readonly IOrganizeBatchStore _batches;
    private readonly IPhotoRepository _repo;
    private readonly IAuditRepository _audit;
    private readonly IPhotoFileSystem _files;
    // One change at a time: two at once could each find a name free and both take it.
    private readonly SemaphoreSlim _one = new(1, 1);

    public FileChanges(IOrganizeBatchStore batches, IPhotoRepository repo, IAuditRepository audit, IPhotoFileSystem files)
    {
        _batches = batches;
        _repo = repo;
        _audit = audit;
        _files = files;
    }

    /// <summary>A place a file can go to without replacing anything: the one asked for, or that name with a number.</summary>
    /// <param name="taken">Places already given out to other files of the same change; this one is added.</param>
    public string FreePlace(string path, ISet<string> taken)
    {
        var folder = Path.GetDirectoryName(path)!;
        bool Taken(string p) => taken.Contains(p) || _files.FileExists(p);
        if (Taken(path)) path = Path.Combine(folder, NameRules.NextFree(Path.GetFileName(path), n => Taken(Path.Combine(folder, n))));
        taken.Add(path);
        return path;
    }

    /// <summary>Adds to <paramref name="folders"/> the folders that have to be made for a file to go into <paramref name="folder"/>, deepest first.</summary>
    public void NeedFolder(List<string> folders, string folder)
    {
        // The folder is inside one that is there, so going up from it always arrives at a folder that exists.
        for (var at = folder; !_files.FolderExists(at) && !folders.Contains(at, PhotoPath.Comparer); at = Path.GetDirectoryName(at)!) folders.Add(at);
    }

    /// <summary>
    /// Carries out a change whose every item knows where it is going, with the folders that have to be made
    /// for them listed. What scans recorded follows the files: a moved file's record moves with it, and a
    /// removed file's record goes, kept for an undo to bring back.
    /// </summary>
    /// <param name="keep">
    /// Asked about each file other than a companion once it has gone. When it says no, the file is put
    /// straight back, its companions stay where they are, and the change goes on without them.
    /// </param>
    /// <exception cref="OperationCanceledException">Cancelled; every file that had gone is back.</exception>
    public async Task<ChangeResult> CarryOutAsync(OrganizeBatch batch, Func<OrganizeBatchItem, bool>? keep = null, CancellationToken ct = default)
    {
        await _one.WaitAsync(ct);
        try
        {
            var records = batch.Removed ? await RecordsAtAsync(batch.Items.Select(i => i.From)) : [];
            if (records.Count > 0) await _batches.KeepRecordsAsync(batch.Id, records);
            batch.State = ChangeState.Started;
            await _batches.SaveAsync(batch);

            var made = new List<string>();
            var done = new List<OrganizeBatchItem>();
            var left = new List<OrganizeBatchItem>();
            OrganizeBatchItem? at = null;
            string? folder = null;
            try
            {
                foreach (var f in batch.CreatedFolders.OrderBy(f => f.Length))
                {
                    folder = f;
                    _files.MakeFolder(f);
                    made.Add(f);
                }
                folder = null;
                var lettingGo = false;
                foreach (var item in batch.Items)
                {
                    ct.ThrowIfCancellationRequested();
                    // The companions of a file let go of stay where they are with it.
                    if (item.Companion && lettingGo) { left.Add(item); continue; }
                    at = item;
                    if (batch.Copy) _files.Copy(item.From, item.To); else _files.Move(item.From, item.To);
                    done.Add(item);
                    var arrived = _files.Describe(item.To)!;
                    item.SizeBytes = arrived.Length;
                    item.ModifiedUtcTicks = arrived.ModifiedUtc.Ticks;
                    if (item.Companion) continue;
                    lettingGo = keep?.Invoke(item) == false;
                    if (!lettingGo) continue;
                    Reverse(item, batch.Copy);
                    done.Remove(item);
                    left.Add(item);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                var stuck = await PutBackAsync(batch, done, made);
                if (ex is OperationCanceledException) throw;
                return new ChangeResult(false, stuck ?? Problem(batch, at, folder, ex), []);
            }

            batch.Items.RemoveAll(left.Contains);
            if (batch.Items.Count > 0) await RecordsFollowAsync(batch, records);
            else TidyAway(made);
            batch.Files = batch.Items.Count(i => !i.Companion);
            batch.Bytes = batch.Items.Where(i => !i.Companion).Sum(i => i.SizeBytes);
            batch.State = ChangeState.Done;
            // A change that let go of every file changed nothing, and there is nothing to undo.
            batch.Undone = batch.Items.Count == 0;
            await _batches.SaveAsync(batch);
            return new ChangeResult(true, null, left);
        }
        finally
        {
            _one.Release();
        }
    }

    /// <summary>
    /// Puts back what a change did. A moved or removed file goes back to its old place, numbered when
    /// another file has taken the name since; a copy is deleted while it is still as it was written and what
    /// it was copied from is still there. Files no longer where the change put them are left, and said so.
    /// </summary>
    public async Task<UndoResult> UndoAsync(Guid id, CancellationToken ct = default)
    {
        await _one.WaitAsync(ct);
        try
        {
            if (await _batches.GetAsync(id) is not { Undone: false, State: ChangeState.Done } batch) return new UndoResult(false, false, false, [], [], null);

            var left = new List<string>();
            var going = new List<OrganizeBatchItem>();
            var taken = new HashSet<string>(PhotoPath.Comparer);
            foreach (var item in Enumerable.Reverse(batch.Items))
            {
                var name = Path.GetFileName(item.To);
                if (_files.Describe(item.To) is not { } now) { left.Add($"{name} is no longer where it was put."); continue; }
                if (batch.Copy)
                {
                    if (now.Length != item.SizeBytes || now.ModifiedUtc.Ticks != item.ModifiedUtcTicks) { left.Add($"{name} has changed since it was copied, so it was left."); continue; }
                    if (!_files.FileExists(item.From)) { left.Add($"{name} was left: the file it was copied from is no longer there."); continue; }
                    // Set aside under a hidden name first, so that it can still come back should another copy not go.
                    item.Back = Path.Combine(Path.GetDirectoryName(item.To)!, $".{name}.{batch.Id:N}.undo");
                }
                else
                {
                    item.Back = FreePlace(item.From, taken);
                    NeedFolder(batch.UndoFolders, Path.GetDirectoryName(item.Back)!);
                }
                going.Add(item);
            }

            if (going.Count > 0)
            {
                batch.State = ChangeState.Undoing;
                await _batches.SaveAsync(batch);
                var made = new List<string>();
                var moved = new List<OrganizeBatchItem>();
                OrganizeBatchItem? at = null;
                string? folder = null;
                try
                {
                    foreach (var f in batch.UndoFolders.OrderBy(f => f.Length))
                    {
                        folder = f;
                        _files.MakeFolder(f);
                        made.Add(f);
                    }
                    folder = null;
                    foreach (var item in going)
                    {
                        at = item;
                        _files.Move(item.To, item.Back!);
                        moved.Add(item);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    var stuck = await PutBackUndoAsync(batch, moved, made);
                    return new UndoResult(true, false, batch.Copy, going, [], stuck ?? UndoProblem(batch, at, folder, ex));
                }
                await RecordsGoBackAsync(batch, going);
            }

            batch.State = ChangeState.Done;
            batch.Undone = true;
            await _batches.SaveAsync(batch);
            if (batch.Copy)
                foreach (var item in going.ToList())
                    if (DeleteSetAside(item) is { } why) { left.Add(why); going.Remove(item); }
            // The folders that were made for the files go again, where the undo has left them empty.
            TidyAway(batch.CreatedFolders);
            return new UndoResult(true, true, batch.Copy, going, left, null);
        }
        finally
        {
            _one.Release();
        }
    }

    /// <summary>Puts back, as they were before they began, the changes and undos that a stop of the service cut short.</summary>
    /// <returns>A sentence for each, saying what became of it.</returns>
    public async Task<IReadOnlyList<string>> RecoverAsync(CancellationToken ct = default)
    {
        await _one.WaitAsync(ct);
        try
        {
            var said = new List<string>();
            foreach (var batch in await _batches.UnfinishedAsync())
            {
                var what = batch.Removed ? $"Deleting files from {batch.Label}" : batch.Copy ? $"Copying files to {batch.Label}" : $"Moving files to {batch.Label}";
                var undoing = batch.State == ChangeState.Undoing;
                string sentence;
                try
                {
                    if (undoing) await RecoverUndoAsync(batch); else await RecoverChangeAsync(batch);
                    sentence = undoing ? $"Undoing \"{what}\" was cut short when PhotoSense stopped. Every file is where it was before the undo."
                        : batch.Copy ? $"{what} was cut short when PhotoSense stopped. The copies it had made so far were deleted."
                        : $"{what} was cut short when PhotoSense stopped. Every file is back where it was.";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    sentence = $"{what} was cut short when PhotoSense stopped, and could not all be put back yet: {Sentence(ex.Message)} PhotoSense tries again each time it starts.";
                }
                said.Add(sentence);
                await _audit.AddAsync(new AuditEntry { Action = "ChangeRecovered", PhotoId = batch.Id.ToString(), Details = sentence });
            }
            return said;
        }
        finally
        {
            _one.Release();
        }
    }

    // A file that is moved goes back; a copy is deleted.
    private void Reverse(OrganizeBatchItem item, bool copy)
    {
        if (copy) _files.DeleteCopy(item.To);
        else _files.Move(item.To, item.From);
    }

    // Puts back what a change that failed part of the way had done. Returns why it could not, if it could not.
    private async Task<string?> PutBackAsync(OrganizeBatch batch, List<OrganizeBatchItem> done, List<string> made)
    {
        string? stuck = null;
        foreach (var item in Enumerable.Reverse(done))
        {
            try { Reverse(item, batch.Copy); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stuck ??= $"{Path.GetFileName(item.To)} could not be put back where it was: {Sentence(ex.Message)} PhotoSense puts it back the next time it starts.";
            }
        }
        // Left as begun, the change is put back once more at the next start.
        if (stuck is not null) return stuck;
        TidyAway(made);
        batch.State = ChangeState.Done;
        batch.Undone = true;
        await _batches.SaveAsync(batch);
        return null;
    }

    private async Task<string?> PutBackUndoAsync(OrganizeBatch batch, List<OrganizeBatchItem> moved, List<string> made)
    {
        string? stuck = null;
        foreach (var item in Enumerable.Reverse(moved))
        {
            try { _files.Move(item.Back!, item.To); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stuck ??= $"{Path.GetFileName(item.Back)} could not be returned to where the undo found it: {Sentence(ex.Message)} PhotoSense returns it the next time it starts.";
            }
        }
        if (stuck is not null) return stuck;
        TidyAway(made);
        EndUndo(batch);
        await _batches.SaveAsync(batch);
        return null;
    }

    private async Task RecoverChangeAsync(OrganizeBatch batch)
    {
        foreach (var item in Enumerable.Reverse(batch.Items))
        {
            if (!_files.FileExists(item.To)) continue;
            if (!_files.FileExists(item.From))
            {
                if (!batch.Copy) _files.Move(item.To, item.From);
                continue;
            }
            // A copy, or a move to another disk that had copied the file and not yet taken the original away:
            // what PhotoSense wrote goes, and only while it holds nothing but what the original holds.
            if (_files.IsCopyOf(item.To, item.From)) _files.DeleteCopy(item.To);
        }
        if (batch.Removed)
        {
            foreach (var record in await _batches.RecordsAsync(batch.Id))
                if (await _repo.GetAsync(record.Id) is null) await _repo.AddOrUpdateAsync(record);
        }
        else if (!batch.Copy)
        {
            foreach (var item in batch.Items)
                if (await _repo.GetByPathAsync(item.To) is { } photo) await _repo.AddOrUpdateAsync(photo.MovedTo(item.From));
        }
        TidyAway(batch.CreatedFolders);
        batch.State = ChangeState.Done;
        batch.Undone = true;
        await _batches.SaveAsync(batch);
    }

    private async Task RecoverUndoAsync(OrganizeBatch batch)
    {
        var going = batch.Items.Where(i => i.Back is not null).ToList();
        foreach (var item in going)
        {
            if (!_files.FileExists(item.Back!)) continue;
            if (!_files.FileExists(item.To)) _files.Move(item.Back!, item.To);
            // An undo to another disk that had copied the file back and not yet taken it away.
            else if (!batch.Copy && _files.IsCopyOf(item.Back!, item.To)) _files.DeleteCopy(item.Back!);
        }
        if (batch.Removed)
        {
            var removed = batch.Items.Select(i => PhotoPath.Key(i.From)).ToHashSet();
            foreach (var record in (await _batches.RecordsAsync(batch.Id)).Where(r => removed.Contains(PhotoPath.Key(r.SourcePath))))
                await _repo.DeleteAsync(record.Id);
        }
        else if (!batch.Copy)
        {
            foreach (var item in going)
                if (await _repo.GetByPathAsync(item.Back!) is { } photo) await _repo.AddOrUpdateAsync(photo.MovedTo(item.To));
        }
        TidyAway(batch.UndoFolders);
        EndUndo(batch);
        await _batches.SaveAsync(batch);
    }

    private async Task<List<Photo>> RecordsAtAsync(IEnumerable<string> paths)
    {
        var records = new List<Photo>();
        foreach (var path in paths)
            if (await _repo.GetByPathAsync(path) is { } record) records.Add(record);
        return records;
    }

    private async Task RecordsFollowAsync(OrganizeBatch batch, List<Photo> records)
    {
        if (batch.Copy) return;
        if (batch.Removed)
        {
            // A removed file is no longer one of the scanned pictures: the Clean up screens must not go on listing it.
            var going = batch.Items.Select(i => PhotoPath.Key(i.From)).ToHashSet();
            foreach (var record in records.Where(r => going.Contains(PhotoPath.Key(r.SourcePath)))) await _repo.DeleteAsync(record.Id);
            return;
        }
        // A scan's record of the file follows it, so that the Clean up screens still find it.
        foreach (var item in batch.Items)
            if (await _repo.GetByPathAsync(item.From) is { } photo) await _repo.AddOrUpdateAsync(photo.MovedTo(item.To));
    }

    private async Task RecordsGoBackAsync(OrganizeBatch batch, List<OrganizeBatchItem> going)
    {
        if (batch.Copy) return;
        if (batch.Removed)
        {
            var records = (await _batches.RecordsAsync(batch.Id)).ToDictionary(r => PhotoPath.Key(r.SourcePath));
            foreach (var item in going)
                if (records.GetValueOrDefault(PhotoPath.Key(item.From)) is { } record)
                    await _repo.AddOrUpdateAsync(PhotoPath.Comparer.Equals(item.Back, item.From) ? record : record.MovedTo(item.Back!));
            return;
        }
        foreach (var item in going)
            if (await _repo.GetByPathAsync(item.To) is { } photo) await _repo.AddOrUpdateAsync(photo.MovedTo(item.Back!));
    }

    // A copy set aside by an undo is deleted once the undo is through. Returns why it was left, if it was.
    private string? DeleteSetAside(OrganizeBatchItem item)
    {
        try
        {
            _files.DeleteCopy(item.Back!);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { _files.Move(item.Back!, item.To); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* left under the hidden name */ }
            return $"{Path.GetFileName(item.To)} could not be deleted, so the copy was left: {Sentence(ex.Message)}";
        }
    }

    private static void EndUndo(OrganizeBatch batch)
    {
        foreach (var item in batch.Items) item.Back = null;
        batch.UndoFolders.Clear();
        batch.State = ChangeState.Done;
    }

    // Folders a change made go again where it leaves them empty; one that will not go is no loss.
    private void TidyAway(IEnumerable<string> folders)
    {
        foreach (var folder in folders.OrderByDescending(f => f.Length))
        {
            try { _files.RemoveEmptyFolder(folder); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* stays */ }
        }
    }

    private static string Problem(OrganizeBatch batch, OrganizeBatchItem? item, string? folder, Exception ex)
    {
        var what = folder is not null ? CouldNotMake(folder) : $"{Path.GetFileName(item!.From)} could not be {(batch.Copy ? "copied" : "moved")}";
        var nothing = batch.Removed ? "Nothing was removed: every file is where it was." : batch.Copy ? "Nothing was copied, and no copy was left behind." : "Nothing was moved: every file is where it was.";
        return $"{what}: {Sentence(ex.Message)} {nothing}";
    }

    private static string UndoProblem(OrganizeBatch batch, OrganizeBatchItem? item, string? folder, Exception ex)
    {
        if (batch.Copy) return $"The copy {Path.GetFileName(item!.To)} could not be deleted: {Sentence(ex.Message)} No copy was deleted.";
        var what = folder is not null ? CouldNotMake(folder) : $"{Path.GetFileName(item!.To)} could not be put back";
        return $"{what}: {Sentence(ex.Message)} Nothing was put back: every file is where it is.";
    }

    private static string CouldNotMake(string folder) => $"The folder {Path.GetFileName(folder)} could not be made in {Path.GetDirectoryName(folder)}";

    private static string Sentence(string text)
    {
        text = text.Trim();
        return text.EndsWith('.') ? text : text + ".";
    }
}
