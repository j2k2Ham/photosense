using PhotoSense.Application.Changes;
using PhotoSense.Application.Removal;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Events;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Application.Scanning.Services;

/// <summary>
/// Removes copies and look-alikes found by a scan. Each request is one change: every file it takes moves
/// into the folder that holds removed files, with the sidecars and Live Photo videos that belong to it alone,
/// or none does; and the whole of it can be undone.
/// </summary>
public class DuplicateRemovalService : IDuplicateRemovalService
{
    private readonly IDuplicateAnalysisService _analysis;
    private readonly IPhotoRepository _repo;
    private readonly ICompanionFileFinder _companions;
    private readonly FileChanges _changes;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly TimeProvider _time;

    public DuplicateRemovalService(IDuplicateAnalysisService analysis, IPhotoRepository repo, ICompanionFileFinder companions, FileChanges changes,
        IIntegrationEventPublisher publisher, TimeProvider? time = null)
    {
        _analysis = analysis;
        _repo = repo;
        _companions = companions;
        _changes = changes;
        _publisher = publisher;
        _time = time ?? TimeProvider.System;
    }

    public Task<BulkRemovalResult> RemoveDuplicatesAsync(string? groupKey = null, bool similar = false, CancellationToken ct = default)
        => RemoveFromAsync(groupKey is null ? null : [groupKey], similar, ct);

    public Task<BulkRemovalResult> RemoveGroupsAsync(IReadOnlyCollection<string> groupKeys, bool similar = false, CancellationToken ct = default)
        => RemoveFromAsync(groupKeys, similar, ct);

    // With no groups named: every duplicate group, and no group of similar shots at all.
    private async Task<BulkRemovalResult> RemoveFromAsync(IReadOnlyCollection<string>? groupKeys, bool similar, CancellationToken ct)
    {
        var analysis = await _analysis.GetAsync(ct);
        // A photo can head a group of each kind under the one key, so the kind is asked for, never guessed.
        var groups = similar ? analysis.Similar : analysis.Duplicates;
        var named = groupKeys ?? (similar ? [] : null);
        if (named is not null) groups = groups.Where(g => named.Contains(g.Key)).ToList();
        // Pictures first: a picture takes its Live Photo video with it, and a video going that way must be
        // seen to be going before any copy of it is judged redundant.
        groups = groups.OrderBy(g => g.Keeper.IsVideo).ToList();

        var removal = new Removal(this);
        var skipped = 0;
        var problems = new List<string>();
        foreach (var group in groups)
        {
            var removable = group.Removable.ToList();
            // Copies are only redundant while the photo they are copies of is still there, unchanged, and stays.
            if (!removal.Stays(group.Keeper))
            {
                skipped += removable.Count;
                problems.Add($"Kept photo is missing or has changed, so its copies were left alone: {group.Keeper.SourcePath}");
                continue;
            }
            foreach (var photo in removable)
            {
                ct.ThrowIfCancellationRequested();
                // The photo kept was seen to stay, and what goes along with a copy (its Live Photo video, its sidecars)
                // is never a file of the copy's own group, so a copy here is never the last one: having changed since
                // the scan is the one reason to leave it.
                if (removal.Add(photo, [group.Keeper]) != RemovalOutcome.Changed) continue;
                skipped++;
                problems.Add($"Changed since the scan, left alone: {photo.SourcePath}");
            }
        }

        var done = await removal.CarryOutAsync(ct);
        if (!done.Done) return new BulkRemovalResult(0, 0, skipped + removal.Asked, [done.Problem!, .. problems]);
        foreach (var photo in removal.LetGo)
        {
            skipped++;
            problems.Add($"The same file as the photo kept, under another path, so it was left alone: {photo.SourcePath}");
        }
        var removed = removal.Removed.Concat(removal.Forgotten).ToList();
        return new BulkRemovalResult(removed.Count, removed.Sum(p => p.FileSizeBytes), skipped, problems, removal.Companions.Count, removal.BatchId);
    }

    public async Task<RemovalResult> RemoveAsync(PhotoId id, bool deleteFile, CancellationToken ct = default)
    {
        if (await _repo.GetAsync(id, ct) is not { } photo) return new RemovalResult(RemovalOutcome.NotFound);
        // A record whose file is already gone is only forgotten: there is nothing left to be the last copy of.
        if (!deleteFile || !File.Exists(photo.SourcePath))
        {
            await ForgetAsync(photo, ct);
            return new RemovalResult(RemovalOutcome.Removed);
        }

        var group = (await _analysis.GetAsync(ct)).Duplicates.FirstOrDefault(g => g.Keeper.Id == id || g.Members.Any(m => m.Photo.Id == id));
        // A copy is a copy of the photo kept. The photo kept may go too, while one of its copies stays in its
        // place. A photo in no duplicate group is nobody's copy, and removing it is the user's own decision.
        IReadOnlyList<Photo>? copyOf = group is null ? null
            : group.Keeper.Id == id ? group.Members.Select(m => m.Photo).ToList() : [group.Keeper];
        var removal = new Removal(this);
        if (removal.Add(photo, copyOf) is var refused and not RemovalOutcome.Removed) return new RemovalResult(refused);

        var done = await removal.CarryOutAsync(ct);
        if (!done.Done) return new RemovalResult(RemovalOutcome.Failed, Error: done.Problem);
        if (removal.LetGo.Count > 0) return new RemovalResult(RemovalOutcome.LastCopy);
        return new RemovalResult(RemovalOutcome.Removed, removal.HeldAt) { Companions = removal.Companions.Select(c => c.From).ToList(), BatchId = removal.BatchId };
    }

    private async Task ForgetAsync(Photo photo, CancellationToken ct)
    {
        await _repo.DeleteAsync(photo.Id, ct);
        await _publisher.PublishAsync(new PhotoDeletedEvent(Guid.NewGuid(), _time.GetUtcNow().UtcDateTime, photo.Id, photo.SourcePath), ct);
    }

    /// <summary>The files one request takes, worked out in full before any of them moves.</summary>
    private sealed class Removal(DuplicateRemovalService service)
    {
        private readonly OrganizeBatch _batch = new() { Removed = true };
        private readonly Dictionary<OrganizeBatchItem, (Photo Photo, Photo? Stays)> _of = [];
        private readonly HashSet<string> _going = new(PhotoPath.Comparer);
        private readonly HashSet<string> _taken = new(PhotoPath.Comparer);
        private readonly List<Photo> _missing = [];
        private List<OrganizeBatchItem> _left = [];

        /// <summary>Photos whose files are no longer there: only what was recorded of them goes.</summary>
        public IReadOnlyList<Photo> Forgotten => _missing;
        /// <summary>Photos whose files moved.</summary>
        public IEnumerable<Photo> Removed => _batch.Items.Where(i => !i.Companion).Select(i => _of[i].Photo);
        /// <summary>Photos that turned out, once moved, to have taken the file kept with them, and were put straight back.</summary>
        public IReadOnlyList<Photo> LetGo => _left.Where(i => !i.Companion).Select(i => _of[i].Photo).ToList();
        public IReadOnlyList<OrganizeBatchItem> Companions => _batch.Items.Where(i => i.Companion).ToList();
        /// <summary>Photos asked for that were to go.</summary>
        public int Asked => _batch.Items.Count(i => !i.Companion) + _missing.Count;
        public Guid? BatchId => _batch.Items.Count > 0 ? _batch.Id : null;
        /// <summary>Where the photo of a request for one is held now.</summary>
        public string HeldAt => _batch.Items.First(i => !i.Companion).To;

        /// <summary>A photo stays while its file is there as it was scanned and this request is not taking it.</summary>
        public bool Stays(Photo photo) => ScannedFile.IsIntact(photo) && !_going.Contains(photo.SourcePath);

        /// <param name="copyOf">The files it is a copy of, when it is being removed as one: it goes only while one of them stays.</param>
        /// <returns>Removed when it is to go, or why it is not.</returns>
        public RemovalOutcome Add(Photo photo, IReadOnlyList<Photo>? copyOf)
        {
            // Already going, for instance as the Live Photo video of a picture this request takes.
            if (_going.Contains(photo.SourcePath)) return RemovalOutcome.NotFound;
            var file = new FileInfo(photo.SourcePath);
            if (!file.Exists)
            {
                _missing.Add(photo);
                return RemovalOutcome.Removed;
            }
            // The decision to remove this file was made from the scan; if the file has changed since, it no longer holds.
            if (file.Length != photo.FileSizeBytes || (photo.FileModifiedUtc is { } scanned && file.LastWriteTimeUtc != scanned)) return RemovalOutcome.Changed;
            var stays = copyOf?.FirstOrDefault(Stays);
            if (copyOf is not null && stays is null) return RemovalOutcome.LastCopy;

            // Worked out while the file is still in place: its sidecars and Live Photo video, if nothing else uses them.
            var companions = service._companions.FindFor(photo.SourcePath).Where(c => !_going.Contains(c)).ToList();
            _of[Plan(photo.SourcePath, photo.ScanRoot, companion: false)] = (photo, stays);
            foreach (var companion in companions) _of[Plan(companion, photo.ScanRoot, companion: true)] = (photo, null);
            return RemovalOutcome.Removed;
        }

        private OrganizeBatchItem Plan(string path, string? root, bool companion)
        {
            var item = new OrganizeBatchItem { From = path, To = service._changes.FreePlace(RemovedPlace.For(path, root), _taken), Companion = companion };
            service._changes.NeedFolder(_batch.CreatedFolders, Path.GetDirectoryName(item.To)!);
            _batch.Items.Add(item);
            _going.Add(path);
            return item;
        }

        public async Task<ChangeResult> CarryOutAsync(CancellationToken ct)
        {
            if (_batch.Items.Count > 0)
            {
                var first = _of[_batch.Items[0]].Photo;
                _batch.Label = first.ScanRoot is { Length: > 0 } root ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) : Path.GetDirectoryName(first.SourcePath)!;
                // Two records can be one file reached by two paths: through a linked folder, or a drive letter
                // standing for the same folder. Moving the "copy" then takes the file that was to stay with it,
                // so it goes straight back.
                var done = await service._changes.CarryOutAsync(_batch, item => _of[item].Stays is not { } stays || File.Exists(stays.SourcePath), ct);
                if (!done.Done) return done;
                _left = done.Left.ToList();
            }
            foreach (var photo in _missing) await service._repo.DeleteAsync(photo.Id, ct);
            foreach (var photo in Removed.Concat(_missing))
                await service._publisher.PublishAsync(new PhotoDeletedEvent(Guid.NewGuid(), service._time.GetUtcNow().UtcDateTime, photo.Id, photo.SourcePath), ct);
            return new ChangeResult(true, null, _left);
        }
    }
}
