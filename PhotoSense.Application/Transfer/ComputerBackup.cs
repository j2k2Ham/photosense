using System.Security.Cryptography;
using System.Text.Json.Nodes;
using PhotoSense.Application.Changes;
using PhotoSense.Application.Plans;
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

/// <summary>
/// A computer keeping the backups of the phones paired with it. A phone that connects asks to back up: the computer
/// first checks every file it holds for that phone (one no longer there, or changed and no longer matching its
/// checksum, is forgotten, so that the phone sends it again), tells the phone the checksums of those it holds, and
/// then takes the files the phone sends, each written under a name of PhotoSense's own until it is whole and its
/// SHA-256 checksum is the phone's. Nothing is ever replaced: a file arriving under a taken name gets a number.
/// </summary>
public sealed class ComputerBackup
{
    /// <summary>How many of the files that arrived last are shown.</summary>
    public const int Shown = 10;

    /// <summary>How many items of a long list go in one message.</summary>
    public const int ListPart = 2000;

    private readonly IBackupStore _store;
    private readonly IPhotoFileSystem _files;
    private readonly FileChanges _changes;
    private readonly Subscription _plan;
    private readonly string _root;
    private readonly string _home;
    private readonly TimeProvider _clock;
    private readonly object _lock = new();
    private readonly Dictionary<string, ArrivingBackup> _arriving = [];

    /// <summary>Keeps phones' backups in folders of <paramref name="root"/>, one for each phone.</summary>
    /// <param name="store">The phones, the files held for each, and what happened.</param>
    /// <param name="files">Where the backups are kept.</param>
    /// <param name="changes">Gives each arriving file a name nothing has yet.</param>
    /// <param name="plan">Keeps the plan a phone says it has, which covers this computer too.</param>
    /// <param name="root">The folder each phone's folder is made in: Pictures/PhotoSense.</param>
    /// <param name="home">The person's own folder, from which folders are named to them ("Pictures/PhotoSense").</param>
    /// <param name="clock">When things happen, and how long a phone may stay silent.</param>
    public ComputerBackup(IBackupStore store, IPhotoFileSystem files, FileChanges changes, Subscription plan, string root, string home, TimeProvider clock)
    {
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
        var phone = await PhoneAsync(phoneId, ct) ?? throw new ArgumentException("That phone is not paired with this computer.");
        if (!_files.FolderExists(folder)) throw new DirectoryNotFoundException($"Folder not found: {folder}");
        var moved = phone with { Folder = folder };
        await _store.SavePhoneAsync(moved, ct);
        await NoteAsync(phoneId, $"Backups now go to {Named(folder)}", ct);
        return moved;
    }

    /// <summary>Pauses a phone's backups on this computer, or lets them run again; a backup arriving now is let finish.</summary>
    /// <exception cref="ArgumentException">No such phone.</exception>
    public async Task<BackupPhone> PauseAsync(string phoneId, bool paused, CancellationToken ct)
    {
        var phone = await PhoneAsync(phoneId, ct) ?? throw new ArgumentException("That phone is not paired with this computer.");
        var changed = phone with { Paused = paused };
        await _store.SavePhoneAsync(changed, ct);
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
        record = record with { LastChecked = _clock.GetUtcNow() };
        await _store.SavePhoneAsync(record, ct);
        await Lists.SendAsync(channel, "held", "sha256", [.. held.Keys], new JsonObject { ["folder"] = Named(record.Folder) }, ct);

        var offered = await Lists.ReceiveAsync(channel, "offer", "files", null, null, ct);
        var files = offered.OfType<JsonObject>().Select(f => (Id: Wire.Text(f, "id"), Name: PhoneReceiving.Plain(Wire.Text(f, "name")), Size: Size(f), Sha256: Sha(f))).ToList();
        lock (_lock) _arriving[phone.Id] = new ArrivingBackup(0, files.Count, 0, files.Sum(f => f.Size), _clock.GetUtcNow(), []);
        if (files.Count > 0) await NoteAsync(phone.Id, $"Receiving {Items(files.Count)}", ct);
        string? arriving = null;
        var came = 0;
        try
        {
            var taken = new HashSet<string>(PhotoPath.Comparer);
            foreach (var (id, name, _, said) in files)
            {
                // A file whose checksum the phone knows and this computer holds is not sent again.
                if (said is not null && held.ContainsKey(said))
                {
                    await channel.SendAsync(new JsonObject { ["t"] = "got", ["id"] = id, ["sha256"] = said }, ct);
                    Arrived(phone.Id, held[said].Path, 0);
                    continue;
                }
                arriving = Path.Combine(record.Folder, $".{name}.{Guid.NewGuid():N}{PhoneReceiving.Arriving}");
                var check = await ReceiveFileAsync(channel, phone.Id, id, arriving, ct);
                string path;
                if (held.TryGetValue(check, out var there)) { _files.DeleteCopy(arriving); path = there.Path; }
                else
                {
                    path = _changes.FreePlace(Path.Combine(record.Folder, name), taken);
                    _files.Move(arriving, path);
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
            var bye = await channel.ReceiveAsync(ct, "bye");
            await _store.SavePhoneAsync(record with
            {
                LastBackup = _clock.GetUtcNow(),
                Safe = bye["safe"] is JsonValue s && s.TryGetValue(out int safe) ? safe : record.Safe,
                SafeBytes = bye["safeBytes"] is JsonValue b && b.TryGetValue(out long bytes) ? bytes : record.SafeBytes,
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

    /// <summary>Notes that a phone's backup stopped part way, and why.</summary>
    public Task StoppedAsync(Device phone, Exception why)
        // Noted whatever stopped it, the program's own stopping too.
        => NoteAsync(phone.Id, why is TransferException ? $"Backup stopped: {why.Message}" : "Backup stopped part way. The phone carries on next time.", CancellationToken.None);

    /// <summary>"1 item", "214 items".</summary>
    public static string Items(int count) => count == 1 ? "1 item" : $"{count:N0} items";

    // Every file held for the phone that is still there as it was; one changed is checked again by its checksum.
    private async Task<Dictionary<string, HeldFile>> CheckAsync(SecureChannel channel, BackupPhone phone, CancellationToken ct)
    {
        var held = new Dictionary<string, HeldFile>(StringComparer.Ordinal);
        var gone = new List<string>();
        foreach (var file in await _store.HeldAsync(phone.Id, ct))
        {
            ct.ThrowIfCancellationRequested();
            var now = _files.Describe(file.Path);
            if (now is not null && now.Length == file.Size && now.ModifiedUtc == file.ModifiedUtc) held[file.Sha256] = file;
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
