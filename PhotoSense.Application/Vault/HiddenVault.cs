using System.Security.Cryptography;
using PhotoSense.Application.Organizing;
using PhotoSense.Application.Plans;
using PhotoSense.Application.Transfer;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;

namespace PhotoSense.Application.Vault;

/// <summary>How the hidden vault stands.</summary>
public enum VaultStage
{
    /// <summary>Not turned on: there is no key yet.</summary>
    Off,

    /// <summary>On, and locked: how many items it holds can be seen, not what they are.</summary>
    Locked,

    /// <summary>Unlocked, until it is locked again or PhotoSense goes to the background.</summary>
    Open,
}

/// <summary>An item of the hidden vault, as its list shows it once unlocked.</summary>
/// <param name="Id">What the page knows it by.</param>
/// <param name="Name">Its name, as Library showed it.</param>
/// <param name="Taken">When it was taken.</param>
/// <param name="IsVideo">It is a video.</param>
/// <param name="Bytes">What its files take.</param>
/// <param name="OnComputer">The computer holds it, sealed, as it last said.</param>
public sealed record VaultItem(string Id, string Name, DateTime? Taken, bool IsVideo, long Bytes, bool OnComputer);

/// <summary>How the hidden vault stands, and, once unlocked, what is in it.</summary>
/// <param name="Stage">Off, locked or open.</param>
/// <param name="Count">How many items it holds.</param>
/// <param name="NotBackedUp">How many of them the computer does not hold yet, which deleting PhotoSense would delete.</param>
/// <param name="Computer">The computer backups go to; null before one is paired.</param>
/// <param name="Items">What it holds, the latest hidden first; empty while it is locked.</param>
/// <param name="Restore">Hidden items being brought back from the computer, or the last time they were.</param>
public sealed record VaultState(VaultStage Stage, int Count, int NotBackedUp, string? Computer, IReadOnlyList<VaultItem> Items, RestoreRun? Restore);

/// <summary>
/// The hidden vault (Pro): pictures and videos taken out of the phone's library and kept inside PhotoSense, each sealed
/// with a key made from the vault's recovery key, behind the phone's own Face ID, fingerprint or passcode. An item
/// leaves the library only once its sealed copy has been read back and found the very bytes it had; it goes to the
/// phone's Recently Deleted or bin through the phone's question, not counted toward the day's free removals. It
/// comes back as a new item with its date and place (Unhide), or comes back and goes to the bin (Delete, counted).
/// Backups carry the sealed files to the computer as they are, and a phone with the recovery key brings them back.
/// </summary>
public sealed class HiddenVault
{
    /// <summary>What the phone's question says it is for.</summary>
    public const string UnlockReason = "Unlock Hidden";

    /// <summary>Why the vault cannot be turned on, nor more hidden, on the free plan.</summary>
    public const string ProOnly = "The hidden vault is part of Pro.";

    private const int ThumbnailEdge = 320;
    private const int ViewEdge = 2048;

    private readonly IVaultKeyStore _keys;
    private readonly IUnlocker _unlocker;
    private readonly IVaultFiles _vault;
    private readonly IPhotoFileSystem _files;
    private readonly IPhotoChangesTogether _together;
    private readonly IPhotoImport _import;
    private readonly OrganizeLibrary _library;
    private readonly IImageAnalyzer _analyzer;
    private readonly IPhotoRepository _repo;
    private readonly IThumbnailStore _thumbnails;
    private readonly Subscription _plan;
    private readonly DailyRemovals _removals;
    private readonly PhoneBackup _backup;
    private readonly string _root;
    private readonly TimeProvider _clock;
    private readonly object _lock = new();
    private readonly Dictionary<string, VaultMeta> _known = new(StringComparer.Ordinal);
    // One change of the vault at a time: two would ask the phone at once.
    private readonly SemaphoreSlim _changing = new(1, 1);
    private RecoveryKey? _open;

    /// <summary>The hidden vault of the library at <paramref name="root"/>.</summary>
    /// <param name="keys">Where the recovery key is kept.</param>
    /// <param name="unlocker">The phone's own question before the vault opens.</param>
    /// <param name="vault">The vault's own files.</param>
    /// <param name="files">The library's files.</param>
    /// <param name="together">The library's changes made together: what is hidden goes to the phone's bin in one, after its question.</param>
    /// <param name="import">Puts an item back in the library.</param>
    /// <param name="library">The library's pictures and videos, with their ids.</param>
    /// <param name="analyzer">Makes the previews kept with each item, as nothing outside the vault may show it.</param>
    /// <param name="repo">The scan's records, which forget what is hidden.</param>
    /// <param name="thumbnails">The previews made before, which go when their picture is hidden.</param>
    /// <param name="plan">Hiding is part of Pro.</param>
    /// <param name="removals">Deleting from the vault counts toward the day's free removals.</param>
    /// <param name="backup">Which of the vault's files the computer holds, and bringing them back.</param>
    /// <param name="root">The library's folder.</param>
    /// <param name="clock">When an item was hidden.</param>
    public HiddenVault(IVaultKeyStore keys, IUnlocker unlocker, IVaultFiles vault, IPhotoFileSystem files, IPhotoChangesTogether together, IPhotoImport import, OrganizeLibrary library, IImageAnalyzer analyzer,
        IPhotoRepository repo, IThumbnailStore thumbnails, Subscription plan, DailyRemovals removals, PhoneBackup backup, string root, TimeProvider clock)
    {
        _keys = keys;
        _unlocker = unlocker;
        _vault = vault;
        _files = files;
        _together = together;
        _import = import;
        _library = library;
        _analyzer = analyzer;
        _repo = repo;
        _thumbnails = thumbnails;
        _plan = plan;
        _removals = removals;
        _backup = backup;
        _root = root;
        _clock = clock;
    }

    /// <summary>How the vault stands; once unlocked, what is in it.</summary>
    public async Task<VaultState> StateAsync(CancellationToken ct)
    {
        var on = await _keys.RecoveryKeyAsync(ct) is not null;
        var files = Sealed();
        var held = await _backup.HiddenOnComputerAsync(ct);
        var computer = (await _backup.ComputerAsync(ct))?.Name;
        var notBackedUp = files.Count(f => !held.GetValueOrDefault(f.Name));
        var key = Opened();
        var stage = !on ? VaultStage.Off : key is null ? VaultStage.Locked : VaultStage.Open;
        var items = new List<(VaultItem Item, DateTime Hidden)>();
        if (key is not null)
            foreach (var file in files)
                if (await MetaAsync(file.Name, key, ct) is { } meta)
                    items.Add((new VaultItem(IdOf(file.Name), meta.Name, meta.Taken, meta.IsVideo, meta.Bytes, held.GetValueOrDefault(file.Name)), meta.HiddenUtc));
        return new VaultState(stage, files.Count, notBackedUp, computer, [.. items.OrderByDescending(i => i.Hidden).Select(i => i.Item)], _backup.RestoreRun());
    }

    /// <summary>Turns the vault on, unlocked, with a new recovery key, which is returned for the person to write down.</summary>
    /// <exception cref="InvalidOperationException">On the free plan, or the vault is on already.</exception>
    public async Task<string> TurnOnAsync(CancellationToken ct)
    {
        if (!(await _plan.CurrentAsync(ct)).Pro) throw new InvalidOperationException(ProOnly);
        if (await _keys.RecoveryKeyAsync(ct) is not null) throw new InvalidOperationException("The hidden vault is on already.");
        var key = RecoveryKey.New();
        await _keys.SaveRecoveryKeyAsync(key.ToString(), ct);
        lock (_lock) _open = key;
        return key.ToString();
    }

    /// <summary>
    /// Takes the recovery key of a vault, written down when it was turned on, and brings its items back from the
    /// computer, after a backup. The vault is left unlocked: knowing the key is proof enough.
    /// </summary>
    /// <exception cref="ArgumentException">What was typed is not a recovery key.</exception>
    /// <exception cref="InvalidOperationException">The vault holds items under another key, no computer is paired, or a backup is running.</exception>
    public async Task RecoverAsync(string typed, CancellationToken ct)
    {
        var key = RecoveryKey.Parse(typed) ?? throw new ArgumentException("That is not a recovery key. It has 32 letters and digits, in 8 groups of 4.");
        var now = await _keys.RecoveryKeyAsync(ct);
        if (now is not null && RecoveryKey.Parse(now)!.ToString() != key.ToString() && Sealed().Count > 0)
            throw new InvalidOperationException("This phone's hidden vault holds items under another recovery key. Unhide them first.");
        await _backup.RestoreHiddenAsync(key.Id, ct);
        await _keys.SaveRecoveryKeyAsync(key.ToString(), ct);
        lock (_lock)
        {
            _open = key;
            _known.Clear();
        }
    }

    /// <summary>Asks the phone's own question, and unlocks the vault when the person passes it.</summary>
    /// <exception cref="InvalidOperationException">The vault is not on.</exception>
    public async Task<bool> UnlockAsync(CancellationToken ct)
    {
        var kept = await _keys.RecoveryKeyAsync(ct) ?? throw new InvalidOperationException("Turn on the hidden vault first.");
        if (!await _unlocker.UnlockAsync(UnlockReason, ct)) return false;
        lock (_lock) _open = RecoveryKey.Parse(kept);
        return true;
    }

    /// <summary>Locks the vault: when the person asks, and whenever PhotoSense goes to the background.</summary>
    public void Lock()
    {
        lock (_lock)
        {
            _open = null;
            _known.Clear();
        }
    }

    /// <summary>The recovery key, to write down again.</summary>
    /// <exception cref="InvalidOperationException">The vault is locked.</exception>
    public string RecoveryKeyShown() => (Opened() ?? throw new InvalidOperationException(Locked)).ToString();

    /// <summary>
    /// Hides these pictures and videos (Pro): each, with every file of it, is sealed into the vault and read back,
    /// then all go together to the phone's Recently Deleted or bin, through the phone's question, not counted toward
    /// the day's free removals. Should anything fail, or the person say no, nothing leaves the library and nothing
    /// stays in the vault. What the scan and the previews knew of them goes too.
    /// </summary>
    /// <param name="ids">The pictures and videos, as Library knows them.</param>
    /// <param name="ct">Stops it before anything leaves the library.</param>
    /// <exception cref="InvalidOperationException">The vault is locked, or the plan is not Pro.</exception>
    /// <exception cref="IOException">A file could not be read or sealed, or the phone did not let them go: nothing changed.</exception>
    public async Task<int> HideAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        var key = Opened() ?? throw new InvalidOperationException(Locked);
        if (!(await _plan.CurrentAsync(ct)).Pro) throw new InvalidOperationException(ProOnly);
        await _changing.WaitAsync(ct);
        try
        {
            var wanted = ids.ToHashSet(StringComparer.Ordinal);
            var items = PhotoItem.Of(await _library.ListAsync(_root, ct)).Where(i => wanted.Contains(i.Main.Id)).ToList();
            if (items.Count == 0) return 0;
            var made = new List<string>();
            var whole = false;
            try
            {
                foreach (var item in items) made.Add(await SealAsync(item, key, ct));
                var paths = items.SelectMany(i => i.Files.Select(f => f.Path)).ToList();
                try { _together.EraseAll(paths); }
                catch (IOException ex) { throw new IOException($"Nothing was hidden: the phone did not let PhotoSense take {Count(items.Count)} out of the library. {ex.Message}", ex); }
                whole = true;
                _library.Forget(paths);
            }
            // A sealed copy whose picture stays in the library is PhotoSense's own, and does not stay.
            finally { if (!whole) foreach (var name in made) _vault.Delete(name); }
            // Out of the library now: what was known of them goes, whatever the caller does.
            await ForgetAsync(items, CancellationToken.None);
            return items.Count;
        }
        finally { _changing.Release(); }
    }

    /// <summary>Puts these items back in the library, each as a new item with its date and place, and out of the vault.</summary>
    /// <exception cref="InvalidOperationException">The vault is locked.</exception>
    /// <exception cref="IOException">The phone could not take one back; those before it are back.</exception>
    public async Task<int> UnhideAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        var key = Opened() ?? throw new InvalidOperationException(Locked);
        await _changing.WaitAsync(ct);
        try { return (await PutBackAsync(ids, key, ct)).Count; }
        finally { _changing.Release(); }
    }

    /// <summary>
    /// Deletes these items: puts them back in the library, then sends them to the phone's Recently Deleted or bin
    /// through its question, where they can still be found for 30 days. Each counts toward the day's free removals.
    /// </summary>
    /// <exception cref="InvalidOperationException">The vault is locked, or more than the free plan has left today.</exception>
    /// <exception cref="IOException">The phone did not let them go: they are back in the library.</exception>
    public async Task<int> DeleteAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        var key = Opened() ?? throw new InvalidOperationException(Locked);
        await _changing.WaitAsync(ct);
        try
        {
            var count = Names(ids).Count;
            var allowance = await _removals.TodayAsync(ct);
            if (!allowance.Allows(count)) throw new InvalidOperationException(DailyRemovals.Refusal(allowance, count));
            var back = await PutBackAsync(ids, key, ct);
            try { _together.EraseAll([.. back.SelectMany(b => b.Paths)]); }
            catch (IOException ex) { throw new IOException($"{Count(back.Count)} back in the library, as the phone did not let PhotoSense delete them. {ex.Message}", ex); }
            // Deleted: counted whatever the caller does now.
            await _removals.RecordAsync(back.Count, back.Sum(b => b.Bytes), allowance, CancellationToken.None);
            return back.Count;
        }
        finally { _changing.Release(); }
    }

    /// <summary>A preview of an item, made when it was hidden: <see cref="VaultBox.ThumbnailPart"/> or <see cref="VaultBox.ViewPart"/>; null for none, or while locked.</summary>
    public async Task<byte[]?> PreviewAsync(string id, string kind, CancellationToken ct)
    {
        if (Opened() is not { } key || Names([id]) is not [var name]) return null;
        try
        {
            using var input = _vault.OpenRead(name);
            using var reader = await VaultBox.Reader.OpenAsync(input, key.VaultKey, key.Id, ct);
            return (await reader.MetaAsync(ct)).Preview(kind) is { } part ? await reader.ReadAsync(part.Offset, (int)part.Length, ct) : null;
        }
        // A damaged item has nothing to show.
        catch (InvalidDataException) { return null; }
    }

    private const string Locked = "Unlock Hidden first.";

    private RecoveryKey? Opened()
    {
        lock (_lock) return _open;
    }

    // The vault's own files; anything else that may be in its folder is not.
    private List<VaultFileInfo> Sealed() => [.. _vault.List().Where(f => f.Name.EndsWith(VaultBox.Extension, StringComparison.Ordinal))];

    private static string IdOf(string name) => name[..^VaultBox.Extension.Length];

    // The vault's files these ids name: an id that names none is passed over, so a request can never name a file of its own.
    private List<string> Names(IEnumerable<string> ids)
    {
        var there = Sealed().ToDictionary(f => IdOf(f.Name), f => f.Name, StringComparer.Ordinal);
        return [.. ids.Distinct(StringComparer.Ordinal).Select(there.GetValueOrDefault).OfType<string>()];
    }

    // What an item is; null for a file that cannot be opened with this key, or is damaged.
    private async Task<VaultMeta?> MetaAsync(string name, RecoveryKey key, CancellationToken ct)
    {
        lock (_lock) if (_known.TryGetValue(name, out var known)) return known;
        try
        {
            using var input = _vault.OpenRead(name);
            using var reader = await VaultBox.Reader.OpenAsync(input, key.VaultKey, key.Id, ct);
            var meta = await reader.MetaAsync(ct);
            lock (_lock) _known[name] = meta;
            return meta;
        }
        catch (InvalidDataException) { return null; }
    }

    // Seals an item into a new file of the vault and reads it back; returns the file's name.
    private async Task<string> SealAsync(PhotoItem item, RecoveryKey key, CancellationToken ct)
    {
        var name = $"{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))}{VaultBox.Extension}";
        var whole = false;
        try
        {
            await SealIntoAsync(name, item, key, ct);
            whole = true;
            return name;
        }
        // A file cut short, or that did not read back as it was, does not stay.
        finally { if (!whole) _vault.Delete(name); }
    }

    private async Task SealIntoAsync(string name, PhotoItem item, RecoveryKey key, CancellationToken ct)
    {
        var parts = new List<VaultPart>();
        await _vault.WriteAsync(name, async output =>
        {
            using var writer = new VaultBox.Writer(output, key.VaultKey, key.Id);
            foreach (var file in item.Files)
            {
                var at = writer.Position;
                string sha;
                await using (var read = await _files.OpenReadAsync(file.Path, ct)) sha = await writer.CopyAsync(read, ct);
                parts.Add(new VaultPart(VaultBox.FilePart, file.Name, at, writer.Position - at, sha));
            }
            foreach (var (kind, edge) in new[] { (VaultBox.ThumbnailPart, ThumbnailEdge), (VaultBox.ViewPart, ViewEdge) })
                if (!item.Main.IsVideo && await RenderAsync(item.Main, edge, ct) is { } jpeg)
                {
                    var at = writer.Position;
                    await writer.WriteAsync(jpeg, ct);
                    parts.Add(new VaultPart(kind, string.Empty, at, jpeg.Length, null));
                }
            var details = item.Main.Details;
            await writer.CompleteAsync(new VaultMeta(item.Main.Name, item.Main.Folder, details.TakenOn, details.Latitude, details.Longitude, item.Main.IsVideo,
                _clock.GetUtcNow().UtcDateTime, parts), ct);
        }, ct);
        // Read back: each file must be the very bytes the library had before anything leaves it.
        try
        {
            using var input = _vault.OpenRead(name);
            using var reader = await VaultBox.Reader.OpenAsync(input, key.VaultKey, key.Id, ct);
            foreach (var part in parts.Where(p => p.Kind == VaultBox.FilePart))
                if (await reader.CopyToAsync(part, Stream.Null, ct) != part.Sha256) throw new InvalidDataException();
        }
        catch (InvalidDataException) { throw new IOException($"PhotoSense could not check the hidden copy of {item.Main.Name}, so nothing was hidden."); }
    }

    private async Task<byte[]?> RenderAsync(LibraryFile picture, int edge, CancellationToken ct)
    {
        try
        {
            using var read = await _files.OpenReadAsync(picture.Path, ct);
            return await _analyzer.RenderJpegAsync(read, edge, ct);
        }
        // A picture that cannot be decoded is hidden all the same, without a preview.
        catch (UnreadableImageException) { return null; }
    }

    private async Task<List<(VaultMeta Meta, IReadOnlyList<string> Paths, long Bytes)>> PutBackAsync(IReadOnlyCollection<string> ids, RecoveryKey key, CancellationToken ct)
    {
        var back = new List<(VaultMeta, IReadOnlyList<string>, long)>();
        foreach (var name in Names(ids))
        {
            VaultMeta meta;
            IReadOnlyList<string> paths;
            try
            {
                await using var input = _vault.OpenRead(name);
                using var reader = await VaultBox.Reader.OpenAsync(input, key.VaultKey, key.Id, ct);
                meta = await reader.MetaAsync(ct);
                var files = meta.Files.Select(f => new ImportedFile(f.Name, reader.OpenPart(f))).ToList();
                try
                {
                    paths = await _import.ImportAsync(meta.Folder, files, meta.Taken,
                        meta.Latitude is { } lat && meta.Longitude is { } lon ? new PlaceChange(lat, lon) : null, ct);
                }
                finally { foreach (var file in files) await file.Content.DisposeAsync(); }
            }
            catch (InvalidDataException) { throw new IOException($"An item of the hidden vault is damaged, so it could not be put back.{Done(back.Count)}"); }
            // In the library again: the sealed copy goes.
            _vault.Delete(name);
            lock (_lock) _known.Remove(name);
            back.Add((meta, paths, meta.Bytes));
        }
        return back;
    }

    private async Task ForgetAsync(List<PhotoItem> items, CancellationToken ct)
    {
        var records = new List<Domain.ValueObjects.PhotoId>();
        foreach (var file in items.SelectMany(i => i.Files))
            if (await _repo.GetByPathAsync(file.Path, ct) is { } photo) records.Add(photo.Id);
        await _repo.DeleteAllAsync(records, ct);
        await _thumbnails.RemoveAsync([.. items.SelectMany(i => i.Files).Select(f => f.PreviewKey)], ct);
    }

    private static string Count(int count) => count == 1 ? "1 item" : $"{count:N0} items";

    // What was put back before something stopped the rest.
    private static string Done(int count) => count == 0 ? string.Empty : $" The {Count(count)} before it {(count == 1 ? "is" : "are")} back in the library.";
}
