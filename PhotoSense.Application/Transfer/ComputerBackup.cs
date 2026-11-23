using System.Security.Cryptography;
using System.Text.Json.Nodes;
using PhotoSense.Application.Changes;
using PhotoSense.Application.Plans;
using PhotoSense.Application.Vault;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Application.Transfer;

/// <summary>A backup arriving from a phone now, as the computer shows it.</summary>
/// <param name="Done">Files that have arrived and been checked.</param>
/// <param name="Total">Files the phone is sending.</param>
/// <param name="Bytes">Bytes that have arrived.</param>
/// <param name="TotalBytes">Bytes the phone is sending.</param>
/// <param name="Started">When it started.</param>
/// <param name="Arrived">The paths on this computer of the files that arrived last, the latest last.</param>
public sealed record ArrivingBackup(int Done, int Total, long Bytes, long TotalBytes, DateTimeOffset Started, IReadOnlyList<string> Arrived);

/// <summary>A copy's name now, and what renaming by date makes it.</summary>
/// <param name="Now">Its name now: "IMG_4821.HEIC".</param>
/// <param name="After">Its name after: "2026-03-02 14.31.07.HEIC".</param>
public sealed record RenamedFile(string Now, string After);

/// <summary>
/// A computer keeping the backups of the phones paired with it. A phone that connects asks to back up: the computer
/// first checks every file it holds for that phone (one no longer there, or changed and no longer matching its
/// checksum, is forgotten, so that the phone sends it again), tells the phone the checksums of those it holds, and
/// then takes the files the phone sends, each written under a name of PhotoSense's own until it is whole and its
/// SHA-256 checksum is the phone's. Nothing is ever replaced: a file arriving under a taken name gets a number. A phone
/// can then ask for a trip's copies to be moved into a folder of their own beside the phones' folders, which is a change
/// like any other here, with Undo.
/// </summary>
public sealed class ComputerBackup
{
    /// <summary>How many of the files that arrived last are shown.</summary>
    public const int Shown = 10;

    /// <summary>How many items of a long list go in one message.</summary>
    public const int ListPart = 2000;

    /// <summary>
    /// The folder, inside a phone's, that its hidden vault's files are kept in, sealed: only a phone with the vault's
    /// recovery key can open them.
    /// </summary>
    public const string HiddenFolder = "Hidden";

    private readonly IBackupStore _store;
    private readonly IPhotoFileSystem _files;
    private readonly FileChanges _changes;
    private readonly Subscription _plan;
    private readonly string _root;
    private readonly string _home;
    private readonly TimeProvider _clock;
    private readonly IMediaDetailsReader _reader;
    private readonly IPlaceNameResolver _places;
    private readonly TermsAcceptance _terms;
    private readonly object _lock = new();
    private readonly Dictionary<string, ArrivingBackup> _arriving = [];
    private readonly SemaphoreSlim _changing = new(1, 1);

    /// <summary>Keeps phones' backups in folders of <paramref name="root"/>, one for each phone.</summary>
    /// <param name="store">The phones, the files held for each, and what happened.</param>
    /// <param name="files">Where the backups are kept.</param>
    /// <param name="changes">Gives each arriving file a name nothing has yet.</param>
    /// <param name="plan">Keeps the plan a phone says it has, which covers this computer too.</param>
    /// <param name="root">The folder each phone's folder is made in: Pictures/PhotoSense.</param>
    /// <param name="home">The person's own folder, from which folders are named to them ("Pictures/PhotoSense").</param>
    /// <param name="clock">When things happen, and how long a phone may stay silent.</param>
    /// <param name="reader">When and where each copy was taken, as its file says, for naming it by date.</param>
    /// <param name="places">The towns and landmarks places are at, for naming a copy by where it was taken.</param>
    /// <param name="terms">Whether the person accepted the note asking for a backup: a phone and this computer tell each other.</param>
    public ComputerBackup(IBackupStore store, IPhotoFileSystem files, FileChanges changes, Subscription plan, string root, string home, TimeProvider clock,
        IMediaDetailsReader reader, IPlaceNameResolver places, TermsAcceptance terms)
    {
        _terms = terms;
        _reader = reader;
        _places = places;
        _store = store;
        _files = files;
        _changes = changes;
        _plan = plan;
        _root = root;
        _home = home;
        _clock = clock;
    }

    /// <summary>A folder as the person knows it: from their own folder, with forward slashes, when it is inside it ("Pictures/PhotoSense"); otherwise whole.</summary>
    public string Named(string folder)
    {
        var inside = Path.GetRelativePath(_home, folder);
        return inside == "." || inside.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(inside) ? folder : inside.Replace('\\', '/');
    }

    /// <summary>The backup arriving from a phone now, or null when none is.</summary>
    public ArrivingBackup? Arriving(string phoneId)
    {
        lock (_lock) return _arriving.GetValueOrDefault(phoneId);
    }

    /// <summary>Every phone that backs up to this computer.</summary>
    public Task<IReadOnlyList<BackupPhone>> PhonesAsync(CancellationToken ct) => _store.PhonesAsync(ct);

    /// <summary>What happened to a phone's backups, the latest first.</summary>
    public Task<IReadOnlyList<BackupEvent>> EventsAsync(string phoneId, CancellationToken ct) => _store.EventsAsync(phoneId, 20, ct);

    /// <summary>How many files are held for a phone, and how much they take.</summary>
    public async Task<(int Count, long Bytes)> HeldAsync(string phoneId, CancellationToken ct)
    {
        var held = await _store.HeldAsync(phoneId, ct);
        return (held.Count, held.Sum(f => f.Size));
    }

    /// <summary>Notes a phone paired just now, giving it a folder of its own the first time; the folder, as the person knows it.</summary>
    public async Task<string> PairedAsync(Device phone, CancellationToken ct)
    {
        var known = await PhoneAsync(phone.Id, ct);
        var kept = known is null
            ? new BackupPhone(phone.Id, phone.Name, null, Path.Combine(_root, PhoneReceiving.Plain(phone.Name)), false, _clock.GetUtcNow(), null, null)
            : known with { Name = phone.Name };
        await _store.SavePhoneAsync(kept, ct);
        await NoteAsync(phone.Id, "Paired with this computer", ct);
        return Named(kept.Folder);
    }

    /// <summary>Sends a phone's backups to another folder from now on; what is there already stays where it is.</summary>
    /// <exception cref="ArgumentException">No such phone.</exception>
    /// <exception cref="DirectoryNotFoundException">The folder is not there.</exception>
    public async Task<BackupPhone> MoveToAsync(string phoneId, string folder, CancellationToken ct)
    {
        _ = await PhoneAsync(phoneId, ct) ?? throw new ArgumentException("That phone is not paired with this computer.");
        if (!_files.FolderExists(folder)) throw new DirectoryNotFoundException($"Folder not found: {folder}");
        var moved = await ChangePhoneAsync(phoneId, p => p with { Folder = folder }, ct);
        await NoteAsync(phoneId, $"Backups now go to {Named(folder)}", ct);
        return moved;
    }

    /// <summary>Makes a phone's trip folders in another folder from now on; those made already stay where they are.</summary>
    /// <exception cref="ArgumentException">No such phone.</exception>
    /// <exception cref="DirectoryNotFoundException">The folder is not there.</exception>
    public async Task<BackupPhone> TripsGoToAsync(string phoneId, string folder, CancellationToken ct)
    {
        _ = await PhoneAsync(phoneId, ct) ?? throw new ArgumentException("That phone is not paired with this computer.");
        if (!_files.FolderExists(folder)) throw new DirectoryNotFoundException($"Folder not found: {folder}");
        var moved = await ChangePhoneAsync(phoneId, p => p with { TripsFolder = folder }, ct);
        await NoteAsync(phoneId, $"Trip folders now go in {Named(folder)}", ct);
        return moved;
    }

    /// <summary>
    /// What renaming a phone's copies would make of their names, the first <paramref name="atMost"/>, and how many would
    /// change; a copy whose file does not say when it was taken keeps its name.
    /// </summary>
    /// <exception cref="ArgumentException">No such phone.</exception>
    public async Task<(int Count, IReadOnlyList<RenamedFile> First)> RenamingAsync(string phoneId, FileNaming naming, int atMost, CancellationToken ct)
    {
        var phone = await PhoneAsync(phoneId, ct) ?? throw new ArgumentException("That phone is not paired with this computer.");
        var plan = await RenamePlanAsync(phone, naming, ct);
        return (plan.Count, [.. plan.Take(atMost).Select(p => new RenamedFile(Path.GetFileName(p.File.Path), Path.GetFileName(p.To)))]);
    }

    /// <summary>
    /// Names a phone's copies on this computer by date, or by date and place, as one change with Undo in Recently
    /// moved, and those that arrive from then on the same way; or, with <see cref="FileNaming.AsOnPhone"/>, lets those
    /// that arrive keep the phone's names. Renaming is part of Pro.
    /// </summary>
    /// <returns>How many were renamed.</returns>
    /// <exception cref="ArgumentException">No such phone.</exception>
    /// <exception cref="InvalidOperationException">The plan is not Pro.</exception>
    /// <exception cref="IOException">A copy could not be renamed: none was.</exception>
    public async Task<int> RenameAsync(string phoneId, FileNaming naming, CancellationToken ct)
    {
        var phone = await PhoneAsync(phoneId, ct) ?? throw new ArgumentException("That phone is not paired with this computer.");
        if (naming != FileNaming.AsOnPhone && !(await _plan.CurrentAsync(ct)).Pro)
            throw new InvalidOperationException("Renaming copies by date is part of Pro. Pro bought on the phone covers this computer too.");
        var plan = naming == FileNaming.AsOnPhone ? [] : await RenamePlanAsync(phone, naming, ct);
        if (plan.Count > 0)
        {
            var batch = new OrganizeBatch { UtcTicks = _clock.GetUtcNow().UtcTicks, Label = $"{phone.Name}, named by date" };
            batch.Items.AddRange(plan.Select(p => new OrganizeBatchItem { From = p.File.Path, To = p.To, SizeBytes = p.File.Size, ModifiedUtcTicks = p.File.ModifiedUtc.Ticks }));
            var result = await _changes.CarryOutAsync(batch, null, ct);
            if (!result.Done) throw new IOException($"The copies could not be renamed. {result.Problem}");
            // The files have moved: where they are now is kept whatever happens.
            foreach (var (file, to) in plan) await _store.HoldAsync(file with { Path = to }, CancellationToken.None);
        }
        await ChangePhoneAsync(phoneId, p => p with { Names = naming }, CancellationToken.None);
        await NoteAsync(phoneId, naming == FileNaming.AsOnPhone ? "New backups keep the phone's names" : $"Renamed {Items(plan.Count)} by date", CancellationToken.None);
        return plan.Count;
    }

    // Each copy whose name by date differs from its own, with where it goes; pictures first, so that each Live Photo's
    // video takes its picture's new name.
    private async Task<List<(HeldFile File, string To)>> RenamePlanAsync(BackupPhone phone, FileNaming naming, CancellationToken ct)
    {
        var held = (await _store.HeldAsync(phone.Id, ct)).Where(f => _files.FileExists(f.Path))
            .OrderBy(f => MediaFiles.IsVideo(f.Path)).ThenBy(f => f.Path, StringComparer.Ordinal).ToList();
        var stems = new Dictionary<string, string>(PhotoPath.Comparer);
        var taken = new HashSet<string>(PhotoPath.Comparer);
        var plan = new List<(HeldFile, string)>();
        foreach (var file in held)
            if (await DatedAsync(file.Path, naming, stems, taken, ct) is { } to) plan.Add((file, to));
        return plan;
    }

    // Where a copy goes named by date, or null when its name stays: it already has the name, or its file does not say
    // when it was taken. A picture's new stem is noted for its Live Photo video, which takes it.
    private async Task<string?> DatedAsync(string path, FileNaming naming, Dictionary<string, string> stems, ISet<string> taken, CancellationToken ct)
    {
        var folder = Path.GetDirectoryName(path)!;
        var stem = Path.Combine(folder, Path.GetFileNameWithoutExtension(path));
        string name;
        if (MediaFiles.IsVideo(path) && stems.TryGetValue(stem, out var picture)) name = Path.GetFileName(picture) + Path.GetExtension(path);
        else
        {
            var details = await _reader.ReadAsync(path, ct);
            if (details.TakenOn is not { } when) return null;
            var place = naming == FileNaming.DatePlace && details.Latitude is { } lat && details.Longitude is { } lon && _places.Locate(lat, lon) is { } at ? at.Landmark ?? at.Town : null;
            name = DetailRules.Numbered(DetailRules.NameByDate(when, place, Path.GetExtension(path)),
                n => !PhotoPath.Comparer.Equals(Path.Combine(folder, n), path) && (taken.Contains(Path.Combine(folder, n)) || _files.FileExists(Path.Combine(folder, n))));
        }
        var to = Path.Combine(folder, name);
        taken.Add(to);
        if (!MediaFiles.IsVideo(path)) stems[stem] = Path.Combine(folder, Path.GetFileNameWithoutExtension(name));
        return PhotoPath.Comparer.Equals(to, path) ? null : to;
    }

    /// <summary>The day the person accepted the note asking for a backup, as this computer knows it, which pairing tells a phone.</summary>
    public Task<DateOnly?> AcceptedAsync(CancellationToken ct) => _terms.AcceptedAsync(ct);

    /// <summary>Where a phone's trip folders are made: where the person chose, or beside the phones' folders.</summary>
    public string TripsIn(BackupPhone phone) => phone.TripsFolder ?? _root;

    /// <summary>Pauses a phone's backups on this computer, or lets them run again; a backup arriving now is let finish.</summary>
    /// <exception cref="ArgumentException">No such phone.</exception>
    public async Task<BackupPhone> PauseAsync(string phoneId, bool paused, CancellationToken ct)
    {
        var changed = await ChangePhoneAsync(phoneId, p => p with { Paused = paused }, ct);
        await NoteAsync(phoneId, paused ? "Backups paused on this computer" : "Backups resumed", ct);
        return changed;
    }

    /// <summary>
    /// Answers a phone linked just now, until it says goodbye: a backup it asks for is checked, received and noted.
    /// A phone whose backups are paused here is told so.
    /// </summary>
    public async Task ServeAsync(SecureChannel channel, Device phone, CancellationToken ct)
    {
        var asked = await channel.ReceiveAsync(ct, "backup", "bye");
        // Until Family, every phone paired with this computer is its owner's: accepted there, it is accepted here.
        await _terms.HeardAsync(TermsAcceptance.Read(asked["accepted"]), ct);
        if (Wire.Text(asked, "t") == "bye") return;
        var known = await PhoneAsync(phone.Id, ct);
        // A phone paired before backups came keeps the folder it would have had.
        var record = (known ?? new BackupPhone(phone.Id, phone.Name, null, Path.Combine(_root, PhoneReceiving.Plain(phone.Name)), false, _clock.GetUtcNow(), null, null))
            with { Name = phone.Name, Kind = asked["kind"] is JsonValue k && k.TryGetValue(out string? kind) ? kind : known?.Kind };
        await _store.SavePhoneAsync(record, ct);
        if (record.Paused)
        {
            await channel.SendAsync(new JsonObject { ["t"] = "paused", ["why"] = $"Backups from {phone.Name} are paused on this computer. Resume them there, in Phones." }, ct);
            return;
        }
        if (PlanMessage.Read(asked["plan"]) is { } plan) await _plan.ShareFromAsync(phone.Name, plan, ct);

        var held = await CheckAsync(channel, record, ct);
        if (!_files.FolderExists(record.Folder)) _files.MakeFolder(record.Folder);
        record = await ChangePhoneAsync(phone.Id, p => p with { LastChecked = _clock.GetUtcNow() }, ct);
        await Lists.SendAsync(channel, "held", "sha256", [.. held.Keys], new JsonObject { ["folder"] = Named(record.Folder), ["trips"] = Named(TripsIn(record)), ["names"] = record.Names.ToString(), ["accepted"] = TermsAcceptance.Write(await _terms.AcceptedAsync(ct)) }, ct);

        var offered = await Lists.ReceiveAsync(channel, "offer", "files", null, null, ct);
        var files = offered.OfType<JsonObject>()
            .Select(f => (Id: Wire.Text(f, "id"), Name: PhoneReceiving.Plain(Wire.Text(f, "name")), Size: Size(f), Sha256: Sha(f), Hidden: f["hidden"] is JsonValue h && h.TryGetValue(out bool hid) && hid))
            .ToList();
        lock (_lock) _arriving[phone.Id] = new ArrivingBackup(0, files.Count, 0, files.Sum(f => f.Size), _clock.GetUtcNow(), []);
        if (files.Count > 0) await NoteAsync(phone.Id, $"Receiving {Items(files.Count)}", ct);
        string? arriving = null;
        var came = 0;
        try
        {
            var taken = new HashSet<string>(PhotoPath.Comparer);
            // The new stems of the pictures named by date, which their Live Photo videos take too.
            var stems = new Dictionary<string, string>(PhotoPath.Comparer);
            foreach (var (id, name, _, said, sealedFile) in files)
            {
                // A file whose checksum the phone knows and this computer holds is not sent again.
                if (said is not null && held.ContainsKey(said))
                {
                    await channel.SendAsync(new JsonObject { ["t"] = "got", ["id"] = id, ["sha256"] = said }, ct);
                    Arrived(phone.Id, held[said].Path, 0);
                    continue;
                }
                // The vault's files are kept apart, sealed, as they came.
                var into = sealedFile ? Path.Combine(record.Folder, HiddenFolder) : record.Folder;
                if (sealedFile && !_files.FolderExists(into)) _files.MakeFolder(into);
                arriving = Path.Combine(into, $".{name}.{Guid.NewGuid():N}{PhoneReceiving.Arriving}");
                var check = await ReceiveFileAsync(channel, phone.Id, id, arriving, ct);
                string path;
                if (held.TryGetValue(check, out var there)) { _files.DeleteCopy(arriving); path = there.Path; }
                else
                {
                    path = _changes.FreePlace(Path.Combine(into, name), taken);
                    _files.Move(arriving, path);
                    // Named by date, as the person chose, once it is whole and its file can say when it was taken.
                    if (!sealedFile && record.Names != FileNaming.AsOnPhone && await DatedAsync(path, record.Names, stems, taken, ct) is { } dated)
                    {
                        _files.Move(path, dated);
                        path = dated;
                    }
                    var stored = _files.Describe(path)!;
                    held[check] = new HeldFile(phone.Id, check, path, stored.Length, stored.ModifiedUtc, _clock.GetUtcNow());
                    await _store.HoldAsync(held[check], ct);
                    came++;
                }
                arriving = null;
                await channel.SendAsync(new JsonObject { ["t"] = "got", ["id"] = id, ["sha256"] = check }, ct);
                Arrived(phone.Id, path, 0);
            }
            await channel.SendAsync(new JsonObject { ["t"] = "done" }, ct);
            var bye = await channel.ReceiveAsync(ct, "bye", "trip", "names", "vault");
            // What the phone asks for after its backup, until it says goodbye: a trip's folder, its copies named by date, or its hidden vault back.
            while (Wire.Text(bye, "t") != "bye")
            {
                if (Wire.Text(bye, "t") == "vault") await GiveBackHiddenAsync(channel, phone, Wire.Text(bye, "key"), ct);
                else if (Wire.Text(bye, "t") == "trip")
                {
                    var folder = string.Empty;
                    var checksums = (await Lists.ReceiveAsync(channel, "trip", "sha256", bye, last => folder = Wire.Text(last, "folder"), ct)).Select(n => n?.GetValue<string>()).OfType<string>().ToList();
                    (record, var made) = await TripFolderAsync(record, folder, checksums, ct);
                    await channel.SendAsync(made, ct);
                }
                else
                {
                    var naming = Enum.TryParse<FileNaming>(Wire.Text(bye, "style"), out var style) ? style : throw new TransferException("The phone sent something PhotoSense does not understand.");
                    try
                    {
                        var renamed = await RenameAsync(phone.Id, naming, ct);
                        await channel.SendAsync(new JsonObject { ["t"] = "named", ["renamed"] = renamed }, ct);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or IOException)
                    {
                        await channel.SendAsync(new JsonObject { ["t"] = "named", ["problem"] = ex.Message }, ct);
                    }
                }
                bye = await channel.ReceiveAsync(ct, "bye", "trip", "names", "vault");
            }
            await ChangePhoneAsync(phone.Id, p => p with
            {
                LastBackup = _clock.GetUtcNow(),
                Safe = bye["safe"] is JsonValue s && s.TryGetValue(out int safe) ? safe : p.Safe,
                SafeBytes = bye["safeBytes"] is JsonValue b && b.TryGetValue(out long bytes) ? bytes : p.SafeBytes,
            }, ct);
        }
        finally
        {
            lock (_lock) _arriving.Remove(phone.Id);
            // A file cut short is PhotoSense's own writing and does not stay; what came is noted however it ended.
            if (arriving is not null && _files.FileExists(arriving)) _files.DeleteCopy(arriving);
            if (came > 0) await NoteAsync(phone.Id, $"{Items(came)} backed up and checked", CancellationToken.None);
        }
    }

    // Lists the hidden vault's files sealed with this key, from whichever phone sent them, and sends those the phone asks for.
    private async Task GiveBackHiddenAsync(SecureChannel channel, Device phone, string key, CancellationToken ct)
    {
        var sealedFiles = new Dictionary<string, HeldFile>(StringComparer.Ordinal);
        foreach (var other in await _store.PhonesAsync(ct))
            foreach (var held in await _store.HeldAsync(other.Id, ct))
            {
                if (!held.Path.EndsWith(VaultBox.Extension, StringComparison.OrdinalIgnoreCase) || sealedFiles.ContainsKey(held.Sha256) || !_files.FileExists(held.Path)) continue;
                await using var read = await _files.OpenReadAsync(held.Path, ct);
                if (await VaultBox.KeyIdOfAsync(read, ct) == key) sealedFiles[held.Sha256] = held;
            }
        await Lists.SendAsync(channel, "vault", "files", [.. sealedFiles.Values.Select(h => (JsonNode)new JsonObject { ["sha256"] = h.Sha256, ["size"] = h.Size })], null, ct);
        var given = 0;
        for (var asked = await channel.ReceiveAsync(ct, "fetch", "fetched"); Wire.Text(asked, "t") == "fetch"; asked = await channel.ReceiveAsync(ct, "fetch", "fetched"))
        {
            var held = sealedFiles.GetValueOrDefault(Wire.Text(asked, "sha256")) ?? throw new TransferException("The phone asked for a file that was not offered.");
            await using var read = await _files.OpenReadAsync(held.Path, ct);
            await channel.SendAsync(new JsonObject { ["t"] = "file", ["size"] = read.Length }, ct);
            var piece = new byte[SecureChannel.PieceSize];
            for (int n; (n = await read.ReadAsync(piece, ct)) > 0;) await channel.SendDataAsync(piece.AsMemory(0, n), ct);
            await channel.SendAsync(new JsonObject { ["t"] = "end", ["sha256"] = held.Sha256 }, ct);
            given++;
        }
        if (given > 0) await NoteAsync(phone.Id, $"{Items(given)} of the hidden vault given back", ct);
    }

    /// <summary>Notes that a phone's backup stopped part way, and why.</summary>
    public Task StoppedAsync(Device phone, Exception why)
        // Noted whatever stopped it, the program's own stopping too.
        => NoteAsync(phone.Id, why is TransferException ? $"Backup stopped: {why.Message}" : "Backup stopped part way. The phone carries on next time.", CancellationToken.None);

    /// <summary>"1 item", "214 items".</summary>
    public static string Items(int count) => count == 1 ? "1 item" : $"{count:N0} items";

    // Moves the copies held with these checksums into the trip's folder, as one change; the answer for the phone.
    private async Task<(BackupPhone Record, JsonObject Answer)> TripFolderAsync(BackupPhone record, string asked, IReadOnlyCollection<string> checksums, CancellationToken ct)
    {
        var name = PhoneReceiving.Plain(asked);
        var folder = Path.Combine(TripsIn(record), name);
        var wanted = checksums.ToHashSet(StringComparer.Ordinal);
        var going = (await _store.HeldAsync(record.Id, ct)).Where(h => wanted.Contains(h.Sha256) && !PhotoPath.Comparer.Equals(Path.GetDirectoryName(h.Path), folder)).ToList();
        if (going.Count == 0 && !_files.FolderExists(folder)) _files.MakeFolder(folder);
        if (going.Count > 0)
        {
            var taken = new HashSet<string>(PhotoPath.Comparer);
            var batch = new OrganizeBatch { UtcTicks = _clock.GetUtcNow().UtcTicks, Label = name };
            _changes.NeedFolder(batch.CreatedFolders, folder);
            batch.Items.AddRange(going.Select(h => new OrganizeBatchItem
            {
                From = h.Path, To = _changes.FreePlace(Path.Combine(folder, Path.GetFileName(h.Path)), taken), SizeBytes = h.Size, ModifiedUtcTicks = h.ModifiedUtc.Ticks,
            }));
            var result = await _changes.CarryOutAsync(batch, null, ct);
            if (!result.Done) return (record, new JsonObject { ["t"] = "trip-made", ["problem"] = $"{Named(folder)} could not be made. {result.Problem}" });
            // The files have moved: where they are now is kept whatever happens.
            foreach (var (file, item) in going.Zip(batch.Items)) await _store.HoldAsync(file with { Path = item.To }, CancellationToken.None);
        }
        record = await ChangePhoneAsync(record.Id, p => p with { TripFolders = (p.TripFolders ?? []).Where(f => !PhotoPath.Comparer.Equals(f, folder)).Append(folder).ToList() }, CancellationToken.None);
        await NoteAsync(record.Id, $"Made the trip folder {Named(folder)} with {Items(going.Count)}", CancellationToken.None);
        return (record, new JsonObject { ["t"] = "trip-made", ["folder"] = Named(folder), ["moved"] = going.Count });
    }

    // Every file held for the phone that is still there as it was; one changed is checked again by its checksum, and one
    // moved among the phone's folders (a trip folder's move undone, say) is found again there.
    private async Task<Dictionary<string, HeldFile>> CheckAsync(SecureChannel channel, BackupPhone phone, CancellationToken ct)
    {
        var all = await _store.HeldAsync(phone.Id, ct);
        var held = new Dictionary<string, HeldFile>(StringComparer.Ordinal);
        var gone = new List<string>();
        Dictionary<(long, long), List<string>>? loose = null;
        foreach (var file in all)
        {
            ct.ThrowIfCancellationRequested();
            var now = _files.Describe(file.Path);
            if (now is null && await FoundAgainAsync(channel, file, loose ??= Loose(phone, all), ct) is { } moved)
            {
                held[file.Sha256] = file with { Path = moved };
                await _store.HoldAsync(held[file.Sha256], ct);
            }
            else if (now is not null && now.Length == file.Size && now.ModifiedUtc == file.ModifiedUtc) held[file.Sha256] = file;
            // Changed since: it is held only if its bytes are still the same. Reading a large file takes a while, which the phone is told of.
            else if (now is not null && await channel.MeanwhileAsync(stop => HashOfAsync(file.Path, stop), _clock, ct) == file.Sha256)
            {
                held[file.Sha256] = file with { Size = now.Length, ModifiedUtc = now.ModifiedUtc };
                await _store.HoldAsync(held[file.Sha256], ct);
            }
            else gone.Add(file.Sha256);
        }
        if (gone.Count > 0)
        {
            await _store.ForgetAsync(phone.Id, gone, ct);
            await NoteAsync(phone.Id, $"{Items(gone.Count)} no longer in the folder as they were, so the phone sends them again", ct);
        }
        return held;
    }

    // The files in the phone's folders that nothing held is at, by their size and time.
    private Dictionary<(long, long), List<string>> Loose(BackupPhone phone, IReadOnlyList<HeldFile> held)
    {
        var known = held.Select(h => h.Path).ToHashSet(PhotoPath.Comparer);
        var loose = new Dictionary<(long, long), List<string>>();
        foreach (var folder in (phone.TripFolders ?? []).Prepend(phone.Folder).Where(_files.FolderExists))
            foreach (var path in _files.FilesIn(folder).Where(p => !known.Contains(p)))
            {
                var stored = _files.Describe(path)!;
                var key = (stored.Length, stored.ModifiedUtc.Ticks);
                if (!loose.TryGetValue(key, out var same)) loose[key] = same = [];
                same.Add(path);
            }
        return loose;
    }

    // Where a file held but no longer where it was is now, among those of its size and time, by its checksum.
    private async Task<string?> FoundAgainAsync(SecureChannel channel, HeldFile file, Dictionary<(long, long), List<string>> loose, CancellationToken ct)
    {
        if (!loose.TryGetValue((file.Size, file.ModifiedUtc.Ticks), out var same)) return null;
        foreach (var path in same)
            // Reading a large file takes a while, which the phone is told of.
            if (await channel.MeanwhileAsync(stop => HashOfAsync(path, stop), _clock, ct) == file.Sha256)
            {
                same.Remove(path);
                return path;
            }
        return null;
    }

    private async Task<string> ReceiveFileAsync(SecureChannel channel, string phoneId, string id, string arriving, CancellationToken ct)
    {
        await channel.SendAsync(new JsonObject { ["t"] = "get", ["id"] = id }, ct);
        var size = Size(await channel.ReceiveAsync(ct, "file"));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var output = _files.CreateNew(arriving))
        {
            for (long written = 0; written < size;)
            {
                var piece = await channel.ReceiveDataAsync(ct);
                if (written + piece.Length > size) throw new TransferException("The phone sent more of a file than it said it would.");
                hash.AppendData(piece);
                await output.WriteAsync(piece, ct);
                written += piece.Length;
                Arrived(phoneId, null, piece.Length);
            }
        }
        var check = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (Wire.Text(await channel.ReceiveAsync(ct, "end"), "sha256") != check)
            throw new TransferException("A file arrived damaged, so it was not kept. Back up again.");
        return check;
    }

    // Counts bytes as they come, and a file once it is whole and checked.
    private void Arrived(string phoneId, string? path, long bytes)
    {
        lock (_lock)
        {
            var now = _arriving[phoneId];
            _arriving[phoneId] = path is null
                ? now with { Bytes = now.Bytes + bytes }
                : now with { Done = now.Done + 1, Arrived = [.. now.Arrived.TakeLast(Shown - 1), path] };
        }
    }

    private async Task<BackupPhone?> PhoneAsync(string id, CancellationToken ct) => (await _store.PhonesAsync(ct)).FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// Changes a phone's record as it is now, one change at a time: a backup takes minutes, and a pause, a folder or a
    /// way of naming chosen in Phones meanwhile must not be lost when it saves what it did.
    /// </summary>
    /// <exception cref="ArgumentException">No such phone.</exception>
    private async Task<BackupPhone> ChangePhoneAsync(string id, Func<BackupPhone, BackupPhone> change, CancellationToken ct)
    {
        await _changing.WaitAsync(ct);
        try
        {
            var changed = change(await PhoneAsync(id, ct) ?? throw new ArgumentException("That phone is not paired with this computer."));
            await _store.SavePhoneAsync(changed, ct);
            return changed;
        }
        finally
        {
            _changing.Release();
        }
    }

    private Task NoteAsync(string phoneId, string text, CancellationToken ct) => _store.NoteAsync(new BackupEvent(phoneId, _clock.GetUtcNow(), text), ct);

    private async Task<string> HashOfAsync(string path, CancellationToken ct)
    {
        await using var read = await _files.OpenReadAsync(path, ct);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(read, ct));
    }

    private static long Size(JsonObject message)
        => message["size"] is JsonValue value && value.TryGetValue(out long size) && size >= 0 ? size : throw new TransferException("The phone sent something PhotoSense does not understand.");

    private static string? Sha(JsonObject file) => file["sha256"] is JsonValue value && value.TryGetValue(out string? sha) ? sha : null;
}

/// <summary>Long lists sent in parts, each small enough for one message.</summary>
internal static class Lists
{
    /// <summary>Sends <paramref name="items"/> under <paramref name="key"/> in messages of type <paramref name="type"/>, the last with <paramref name="last"/>'s fields too.</summary>
    public static async Task SendAsync(SecureChannel channel, string type, string key, IReadOnlyList<JsonNode> items, JsonObject? last, CancellationToken ct)
    {
        for (var from = 0; ; from += ComputerBackup.ListPart)
        {
            var more = from + ComputerBackup.ListPart < items.Count;
            var message = new JsonObject { ["t"] = type, [key] = new JsonArray([.. items.Skip(from).Take(ComputerBackup.ListPart).Select(i => i.DeepClone())]), ["more"] = more };
            if (!more && last is not null) foreach (var (name, value) in last) message[name] = value!.DeepClone();
            await channel.SendAsync(message, ct);
            if (!more) return;
        }
    }

    /// <summary>Strings sent with <see cref="SendAsync(SecureChannel, string, string, IReadOnlyList{JsonNode}, JsonObject?, CancellationToken)"/>.</summary>
    public static Task SendAsync(SecureChannel channel, string type, string key, IReadOnlyList<string> items, JsonObject? last, CancellationToken ct)
        => SendAsync(channel, type, key, [.. items.Select(i => (JsonNode)JsonValue.Create(i))], last, ct);

    /// <summary>Every item of a list sent in parts, starting from a part already read, if any; the last part, which carries whatever else was sent with it, is handed to <paramref name="last"/>.</summary>
    public static async Task<List<JsonNode?>> ReceiveAsync(SecureChannel channel, string type, string key, JsonObject? first, Action<JsonObject>? last, CancellationToken ct)
    {
        var items = new List<JsonNode?>();
        while (true)
        {
            var part = first ?? await channel.ReceiveAsync(ct, type);
            first = null;
            items.AddRange((part[key] as JsonArray ?? []).Select(n => n?.DeepClone()));
            if (part["more"] is JsonValue more && more.TryGetValue(out bool going) && going) continue;
            last?.Invoke(part);
            return items;
        }
    }
}
