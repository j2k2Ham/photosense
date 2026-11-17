using System.Security.Cryptography;
using System.Text.Json.Nodes;
using PhotoSense.Application.Organizing;
using PhotoSense.Application.Plans;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Application.Transfer;

/// <summary>How backing up stands on the phone.</summary>
public enum BackupStage
{
    /// <summary>The phone has been paired with no computer.</summary>
    Unpaired,
    /// <summary>Not backing up now.</summary>
    Ready,
    /// <summary>Backing up now.</summary>
    Running,
    /// <summary>The person paused a backup part way.</summary>
    Paused,
    /// <summary>A backup stopped part way; the problem says why.</summary>
    Failed,
}

/// <summary>A picture or video in the strip of those backing up, by its id in the library, and whether it is on the computer yet.</summary>
/// <param name="Id">Its id, as Organize and Library know it.</param>
/// <param name="Done">It is on the computer, checked.</param>
public sealed record BackupTile(string Id, bool Done);

/// <summary>How backing up stands on the phone.</summary>
/// <param name="Stage">How it stands.</param>
/// <param name="Computer">The computer backups go to.</param>
/// <param name="Folder">Where the computer puts them, as it last said.</param>
/// <param name="Waiting">Pictures and videos not on the computer yet that a backup would send.</param>
/// <param name="WaitingBytes">How much those take.</param>
/// <param name="OnComputer">Pictures and videos the computer holds, as it last said.</param>
/// <param name="Done">Files of the backup going on, or paused, that are on the computer.</param>
/// <param name="Total">Files that backup sends.</param>
/// <param name="Bytes">Bytes of it sent.</param>
/// <param name="TotalBytes">Bytes it sends.</param>
/// <param name="LastChecked">When the computer last checked what it holds.</param>
/// <param name="LastBackup">When a backup last finished.</param>
/// <param name="Strip">A few of the pictures and videos being backed up, or waiting.</param>
/// <param name="Problem">Why the last backup stopped.</param>
/// <param name="Settings">How the phone backs up by itself.</param>
public sealed record BackupState(BackupStage Stage, string? Computer, string? Folder, int Waiting, long WaitingBytes, int OnComputer, int Done, int Total, long Bytes, long TotalBytes,
    DateTimeOffset? LastChecked, DateTimeOffset? LastBackup, IReadOnlyList<BackupTile> Strip, string? Problem, BackupSettings Settings);

/// <summary>Which of the library's pictures and videos are on the computer, and which wait, by their ids.</summary>
/// <param name="Computer">The computer.</param>
/// <param name="OnComputer">The ids of those on the computer.</param>
/// <param name="Waiting">The ids of those a backup would send.</param>
/// <param name="LastChecked">When the computer last checked.</param>
public sealed record LibraryBackup(string? Computer, IReadOnlyList<string> OnComputer, IReadOnlyList<string> Waiting, DateTimeOffset? LastChecked);

/// <summary>A number of pictures and videos, and how much they take.</summary>
/// <param name="Count">How many.</param>
/// <param name="Bytes">How much they take.</param>
public sealed record ItemCount(int Count, long Bytes);

/// <summary>A picture or video Safe to delete would delete: every file of it, and how much they take.</summary>
/// <param name="Paths">Its files, as the phone's file system shows them.</param>
/// <param name="Bytes">How much they take.</param>
public sealed record RemovableItem(IReadOnlyList<string> Paths, long Bytes);

/// <summary>What Safe to delete deleted.</summary>
/// <param name="Removed">Pictures and videos sent to the phone's Recently Deleted or Trash.</param>
/// <param name="Bytes">How much they took.</param>
/// <param name="Left">Those it could have deleted that are still on the phone.</param>
/// <param name="Before">The phone's storage before.</param>
public sealed record SafeRemoval(int Removed, long Bytes, int Left, DeviceStorage Before);

/// <summary>What could be deleted from the phone, as its copies are on the computer, and what the keep rules keep.</summary>
/// <param name="Computer">The computer.</param>
/// <param name="Folder">Where the copies are on it.</param>
/// <param name="LastChecked">When the computer last checked its copies.</param>
/// <param name="Checked">Every picture and video whose copy the computer holds.</param>
/// <param name="Favorites">Those among them that are favorites.</param>
/// <param name="Recent">Those among them taken in the last 30 days.</param>
/// <param name="Removable">Those the keep rules leave to be deleted.</param>
/// <param name="Storage">The phone's storage.</param>
/// <param name="CloudPhotos">The phone's photos are kept in iCloud too.</param>
/// <param name="Settings">The keep rules, among the rest.</param>
public sealed record SafeReview(string? Computer, string? Folder, DateTimeOffset? LastChecked, ItemCount Checked, ItemCount Favorites, ItemCount Recent, ItemCount Removable,
    DeviceStorage Storage, bool CloudPhotos, BackupSettings Settings);

/// <summary>
/// Backing up from a phone to the computer it was paired with. A backup finds the computer on the Wi-Fi, proves the
/// pairing, and hears from it the SHA-256 checksums of every file it holds for this phone, checked just then; it then
/// sends the files whose checksum is not among them, each checked by the computer as it arrives. A picture or video
/// counts as on the computer only once the computer has confirmed every file of it by its checksum: a Live Photo's
/// video goes with its picture.
/// </summary>
public sealed class PhoneBackup
{
    /// <summary>How many tiles the strip shows.</summary>
    public const int StripSize = 6;

    /// <summary>How long what was taken counts as recent, for Safe to delete's keep rule.</summary>
    public static readonly TimeSpan Recently = TimeSpan.FromDays(30);

    private readonly IBackupLedger _ledger;
    private readonly IComputerFinder _finder;
    private readonly IPairingStore _pairs;
    private readonly OrganizeLibrary _library;
    private readonly IPhotoFileSystem _files;
    private readonly Subscription _plan;
    private readonly DailyRemovals _removals;
    private readonly IDeviceStatus _device;
    private readonly IFavoritePhotos? _favorites;
    private readonly string _name;
    private readonly string _root;
    private readonly TimeProvider _clock;
    private readonly object _lock = new();
    private Run? _run;
    private CancellationTokenSource? _stop;

    // A backup going on, or the last one: what it sends, and how far it has got.
    private sealed class Run
    {
        public BackupStage Stage = BackupStage.Running;
        public List<LibraryFile> Queue = [];
        public readonly HashSet<string> Done = new(StringComparer.Ordinal);
        public long Bytes;
        public int Next;
        public string? Problem;
    }

    // A picture or video as Library shows it: a Live Photo's video goes with its picture.
    private sealed record Item(LibraryFile Main, IReadOnlyList<LibraryFile> Files)
    {
        public long Bytes => Files.Sum(f => f.SizeBytes);
    }

    /// <summary>Backs up the library at <paramref name="root"/> to the computer the phone was paired with.</summary>
    /// <param name="ledger">The computer, the settings, the files' checksums and which of them the computer holds.</param>
    /// <param name="finder">Finds the computer on the Wi-Fi.</param>
    /// <param name="pairs">The pairing with the computer, proved on each connection.</param>
    /// <param name="library">The library's pictures and videos, with their ids.</param>
    /// <param name="files">Where they are kept.</param>
    /// <param name="plan">Automatic backup is part of Pro; the computer is told of the plan.</param>
    /// <param name="removals">What Safe to delete deletes counts toward the day's free removals.</param>
    /// <param name="device">What sort the phone is, whether it is charging, its storage, and whether iCloud keeps its photos.</param>
    /// <param name="favorites">The person's favorites, which Safe to delete can keep; null where there are none.</param>
    /// <param name="name">What this phone is called, as the computer shows it.</param>
    /// <param name="root">The library's folder.</param>
    /// <param name="clock">How long the computer may stay silent, and when things happen.</param>
    public PhoneBackup(IBackupLedger ledger, IComputerFinder finder, IPairingStore pairs, OrganizeLibrary library, IPhotoFileSystem files, Subscription plan, DailyRemovals removals,
        IDeviceStatus device, IFavoritePhotos? favorites, string name, string root, TimeProvider clock)
    {
        _ledger = ledger;
        _finder = finder;
        _pairs = pairs;
        _library = library;
        _files = files;
        _plan = plan;
        _removals = removals;
        _device = device;
        _favorites = favorites;
        _name = name;
        _root = root;
        _clock = clock;
    }

    /// <summary>The backup going on now, for whoever waits for it to end.</summary>
    public Task Running { get; private set; } = Task.CompletedTask;

    /// <summary>How backing up stands now, with what waits counted afresh.</summary>
    public async Task<BackupState> StateAsync(CancellationToken ct)
    {
        var computer = await _ledger.ComputerAsync(ct);
        var settings = await _ledger.SettingsAsync(ct);
        var (items, on) = await ItemsAsync(ct);
        var waiting = items.Where(i => Waits(i, on, settings)).ToList();
        var onComputer = items.Count(i => On(i, on));
        lock (_lock)
        {
            if (computer is null)
                return new BackupState(BackupStage.Unpaired, null, null, waiting.Count, waiting.Sum(i => i.Bytes), 0, 0, 0, 0, 0, null, null, [], null, settings);
            var run = _run;
            var strip = run is null
                ? [.. waiting.Take(StripSize).Select(i => new BackupTile(i.Main.Id, false))]
                : Strip(run);
            return new BackupState(run?.Stage ?? BackupStage.Ready, computer.Name, computer.Folder, waiting.Count, waiting.Sum(i => i.Bytes), onComputer,
                run?.Done.Count ?? 0, run?.Queue.Count ?? 0, run?.Bytes ?? 0, run?.Queue.Sum(f => f.SizeBytes) ?? 0, computer.LastChecked, computer.LastBackup, strip, run?.Problem, settings);
        }
    }

    /// <summary>Which of the library's pictures and videos are on the computer, and which wait.</summary>
    public async Task<LibraryBackup> LibraryAsync(CancellationToken ct)
    {
        var computer = await _ledger.ComputerAsync(ct);
        var settings = await _ledger.SettingsAsync(ct);
        var (items, on) = await ItemsAsync(ct);
        return new LibraryBackup(computer?.Name, [.. items.Where(i => On(i, on)).Select(i => i.Main.Id)], [.. items.Where(i => Waits(i, on, settings)).Select(i => i.Main.Id)], computer?.LastChecked);
    }

    /// <summary>What could be deleted from the phone, as its copies are on the computer, and what the keep rules keep.</summary>
    public async Task<SafeReview> SafeAsync(CancellationToken ct)
    {
        var computer = await _ledger.ComputerAsync(ct);
        var settings = await _ledger.SettingsAsync(ct);
        var (items, on) = await ItemsAsync(ct);
        var safe = items.Where(i => On(i, on)).ToList();
        var favorites = _favorites is null ? new HashSet<string>() : await _favorites.FavoritesAsync(ct);
        var since = _clock.GetLocalNow().DateTime - Recently;
        bool Favorite(Item i) => i.Files.Any(f => favorites.Contains(f.Path));
        bool Recent(Item i) => i.Main.Date >= since;
        var removable = safe.Where(i => !(settings.KeepFavorites && Favorite(i)) && !(settings.KeepRecent && Recent(i))).ToList();
        return new SafeReview(computer?.Name, computer?.Folder, computer?.LastChecked, Count(safe), Count(safe.Where(Favorite)), Count(safe.Where(Recent)), Count(removable),
            _device.Storage, _device.CloudPhotos, settings);
    }

    /// <summary>
    /// The pictures and videos Safe to delete would delete, largest first, each with every file of it: those the
    /// computer holds, checked, less those the keep rules keep.
    /// </summary>
    public async Task<IReadOnlyList<RemovableItem>> RemovableAsync(CancellationToken ct)
    {
        var settings = await _ledger.SettingsAsync(ct);
        var (items, on) = await ItemsAsync(ct);
        var favorites = _favorites is null ? new HashSet<string>() : await _favorites.FavoritesAsync(ct);
        var since = _clock.GetLocalNow().DateTime - Recently;
        return [.. items.Where(i => On(i, on))
            .Where(i => !(settings.KeepFavorites && i.Files.Any(f => favorites.Contains(f.Path))) && !(settings.KeepRecent && i.Main.Date >= since))
            .OrderByDescending(i => i.Bytes)
            .Select(i => new RemovableItem([.. i.Files.Select(f => f.Path)], i.Bytes))];
    }

    /// <summary>
    /// Safe to delete: sends to the phone's Recently Deleted or Trash the pictures and videos the computer holds,
    /// checked, less those the keep rules keep, largest first and no more than <paramref name="atMost"/>. The phone asks
    /// the person first. Each counts toward the day's free removals.
    /// </summary>
    /// <param name="atMost">The most to delete, the largest first; null for all of them.</param>
    /// <param name="ct">Stops it before anything is asked.</param>
    /// <exception cref="InvalidOperationException">More than the free plan has left today, or this is not a phone.</exception>
    /// <exception cref="IOException">The person said no, or the phone could not delete them: nothing went.</exception>
    public async Task<SafeRemoval> RemoveAsync(int? atMost, CancellationToken ct)
    {
        if (_files is not IPhotoChangesTogether phone) throw new InvalidOperationException("Only a phone deletes what is on its computer.");
        var removable = await RemovableAsync(ct);
        var going = removable.Take(atMost ?? removable.Count).ToList();
        var before = _device.Storage;
        if (going.Count == 0) return new SafeRemoval(0, 0, removable.Count, before);
        var allowance = await _removals.TodayAsync(ct);
        if (!allowance.Allows(going.Count)) throw new InvalidOperationException(DailyRemovals.Refusal(allowance, going.Count));
        phone.EraseAll([.. going.SelectMany(i => i.Paths)]);
        // Deleted: counted whatever the caller does now.
        await _removals.RecordAsync(going.Count, allowance, CancellationToken.None);
        return new SafeRemoval(going.Count, going.Sum(i => i.Bytes), removable.Count - going.Count, before);
    }

    /// <summary>Keeps how the phone backs up by itself, and what Safe to delete keeps.</summary>
    /// <exception cref="InvalidOperationException">Automatic backup was turned on without Pro.</exception>
    public async Task<BackupSettings> SaveSettingsAsync(BackupSettings settings, CancellationToken ct)
    {
        if (settings.Automatic && !(await _ledger.SettingsAsync(ct)).Automatic && !(await _plan.CurrentAsync(ct)).Pro)
            throw new InvalidOperationException("Automatic backup is part of Pro. Sending photos yourself stays free.");
        await _ledger.SaveSettingsAsync(settings, ct);
        return settings;
    }

    /// <summary>
    /// Starts a backup of everything that waits, or carries on one paused; or, with <paramref name="only"/>, sends only
    /// those pictures and videos, videos too whatever the settings say (Send now). It goes on by itself.
    /// </summary>
    /// <param name="only">The ids of the pictures and videos to send, as Library knows them; null for everything that waits.</param>
    /// <param name="ct">Stops it before it starts.</param>
    /// <exception cref="InvalidOperationException">The phone has been paired with no computer.</exception>
    public async Task StartAsync(IReadOnlyCollection<string>? only, CancellationToken ct)
    {
        var computer = await _ledger.ComputerAsync(ct) ?? throw new InvalidOperationException("Pair with a computer first: in Settings, choose Computer.");
        lock (_lock)
        {
            if (_run is { Stage: BackupStage.Running }) return;
            _run = new Run();
            _stop = new CancellationTokenSource();
            var stop = _stop.Token;
            var run = _run;
            Running = Task.Run(() => RunAsync(run, computer, only, stop), CancellationToken.None);
        }
    }

    /// <summary>Pauses the backup going on; what is on the computer stays there, and the next backup carries on from it.</summary>
    public void Pause()
    {
        lock (_lock)
        {
            if (_run is not { Stage: BackupStage.Running } run) return;
            run.Stage = BackupStage.Paused;
            _stop!.Cancel();
        }
    }

    /// <summary>
    /// Starts a backup by itself when one is due: automatic backup is on and part of the plan, the phone is charging if
    /// it should be, it has been paired, nothing is backing up, and something waits.
    /// </summary>
    /// <returns>Whether a backup started.</returns>
    public async Task<bool> AutomaticAsync(CancellationToken ct)
    {
        var settings = await _ledger.SettingsAsync(ct);
        lock (_lock) if (_run is { Stage: BackupStage.Running }) return false;
        if (!settings.Automatic || (settings.OnlyWhileCharging && !_device.Charging) || await _ledger.ComputerAsync(ct) is null) return false;
        if (!(await _plan.CurrentAsync(ct)).Pro) return false;
        var (items, on) = await ItemsAsync(ct);
        if (!items.Any(i => Waits(i, on, settings))) return false;
        await StartAsync(null, ct);
        return true;
    }

    private async Task RunAsync(Run run, BackupComputer computer, IReadOnlyCollection<string>? only, CancellationToken stop)
    {
        try
        {
            var self = await _pairs.SelfIdAsync(stop);
            if (await _pairs.GetAsync(computer.ComputerId, stop) is null)
                throw new TransferException($"This phone no longer knows {computer.Name}. Pair them again: in Settings, choose Computer.");
            var found = (await _finder.FindAsync(stop)).FirstOrDefault(c => c.Id == computer.ComputerId)
                ?? throw new TransferException($"{computer.Name} was not found on this Wi-Fi. Check that PhotoSense is open on it, and that both are on the same Wi-Fi.");
            await using var connection = await TimedConnection.WithinAsync(found.Connect, _clock, stop);
            var stream = new TimedConnection(connection, _clock);
            // Both know each other: the pairing is proved, and no number is ever compared here.
            var linked = await Pairing.PhoneAsync(stream, new Device(self, _name), _pairs, (_, _, _) => Task.FromResult(false), ticket: null, stop);
            using var channel = linked.Channel;
            await channel.SendAsync(new JsonObject { ["t"] = "backup", ["kind"] = _device.Kind, ["plan"] = PlanMessage.Write(await _plan.CurrentAsync(stop)) }, stop);
            var first = await channel.ReceiveAsync(stop, "held", "paused");
            if (Wire.Text(first, "t") == "paused") throw new TransferException(Wire.Text(first, "why"));
            var folder = computer.Folder;
            var held = (await Lists.ReceiveAsync(channel, "held", "sha256", first, last => folder = Wire.Text(last, "folder"), stop))
                .Select(n => n?.GetValue<string>()).OfType<string>().ToHashSet(StringComparer.Ordinal);
            await _ledger.KeepOnComputerAsync(held, all: true, stop);
            computer = computer with { Folder = folder, LastChecked = _clock.GetUtcNow() };
            await _ledger.SaveComputerAsync(computer, stop);

            var settings = await _ledger.SettingsAsync(stop);
            var (items, _) = await ItemsAsync(stop);
            var queue = new List<LibraryFile>();
            var said = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var item in items.Where(i => only is null ? !i.Main.IsVideo || settings.IncludeVideos : only.Contains(i.Main.Id)))
                foreach (var file in item.Files)
                {
                    var sha = await ChecksumAsync(file, stop);
                    if (sha is not null && held.Contains(sha)) continue;
                    queue.Add(file);
                    said[file.Id] = sha;
                }
            lock (_lock) run.Queue = queue;
            await Lists.SendAsync(channel, "offer", "files", [.. queue.Select(f => (JsonNode)new JsonObject
            {
                ["id"] = f.Id, ["name"] = f.Name, ["size"] = f.SizeBytes, ["modified"] = f.ModifiedUtc.ToString("O"), ["sha256"] = said[f.Id],
            })], null, stop);
            var sent = new Dictionary<string, string>(StringComparer.Ordinal);
            while (true)
            {
                var asked = await channel.ReceiveAsync(stop, "get", "got", "done");
                var id = Wire.Text(asked, "t") == "done" ? null : Wire.Text(asked, "id");
                if (id is null) break;
                var file = queue.FirstOrDefault(f => f.Id == id) ?? throw new TransferException("The computer asked for a file that was not offered.");
                if (Wire.Text(asked, "t") == "get")
                {
                    sent[id] = await SendFileAsync(channel, run, file, stop);
                    continue;
                }
                // On the computer only with the very bytes that were sent, or that the phone said it had.
                var check = Wire.Text(asked, "sha256");
                if ((sent.TryGetValue(id, out var mine) ? mine : said[id]) != check)
                    throw new TransferException("The computer's check of a file did not match this phone's. Back up again.");
                await _ledger.KeepOnComputerAsync([check], all: false, stop);
                lock (_lock) run.Done.Add(id);
            }
            var safe = await SafeAsync(stop);
            await channel.SendAsync(new JsonObject { ["t"] = "bye", ["safe"] = safe.Removable.Count, ["safeBytes"] = safe.Removable.Bytes }, stop);
            await _ledger.SaveComputerAsync(computer with { LastBackup = _clock.GetUtcNow() }, CancellationToken.None);
            lock (_lock) run.Stage = BackupStage.Ready;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            lock (_lock)
            {
                // Paused: Pause said so, and what is on the computer stays known.
                if (run.Stage == BackupStage.Paused) return;
                run.Stage = BackupStage.Failed;
                run.Problem = ex is TransferException ? ex.Message : $"The backup stopped: {ex.Message}";
            }
        }
    }

    private async Task<string> SendFileAsync(SecureChannel channel, Run run, LibraryFile file, CancellationToken ct)
    {
        lock (_lock) run.Next = Math.Max(run.Next, run.Queue.IndexOf(file));
        // A picture kept only in iCloud is fetched first, which can take a while; the computer is told of it.
        await using var read = await channel.MeanwhileAsync(stop => _files.OpenReadAsync(file.Path, stop), _clock, ct);
        await channel.SendAsync(new JsonObject { ["t"] = "file", ["id"] = file.Id, ["size"] = read.Length }, ct);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var piece = new byte[SecureChannel.PieceSize];
        for (int n; (n = await read.ReadAsync(piece, ct)) > 0;)
        {
            hash.AppendData(piece, 0, n);
            await channel.SendDataAsync(piece.AsMemory(0, n), ct);
            lock (_lock) run.Bytes += n;
        }
        var check = Convert.ToHexStringLower(hash.GetHashAndReset());
        await _ledger.KeepChecksumAsync(file.Path, file.SizeBytes, file.ModifiedUtc, check, ct);
        await channel.SendAsync(new JsonObject { ["t"] = "end", ["id"] = file.Id, ["sha256"] = check }, ct);
        return check;
    }

    // A few of those sent last and those coming next.
    private static List<BackupTile> Strip(Run run)
    {
        var from = Math.Max(0, Math.Min(run.Next - StripSize / 2, run.Queue.Count - StripSize));
        return [.. run.Queue.Skip(from).Take(StripSize).Select(f => new BackupTile(f.Id, run.Done.Contains(f.Id)))];
    }

    // Every picture and video in the library, each with its files, and the checksums of those the computer holds.
    private async Task<(List<Item> Items, HashSet<string> OnComputer)> ItemsAsync(CancellationToken ct)
    {
        var files = await _library.ListAsync(_root, ct);
        var on = new HashSet<string>(StringComparer.Ordinal);
        var held = await _ledger.OnComputerAsync(ct);
        foreach (var file in files)
            if (await ChecksumAsync(file, ct) is { } sha && held.Contains(sha)) on.Add(file.Id);
        var pictures = files.Where(f => !f.IsVideo && f.Details.LivePhotoId is not null).GroupBy(f => f.Details.LivePhotoId!).ToDictionary(g => g.Key, g => g.First());
        var items = new Dictionary<string, (LibraryFile Main, List<LibraryFile> Files)>(StringComparer.Ordinal);
        foreach (var file in files.OrderBy(f => f.IsVideo))
        {
            var main = file.IsVideo && file.Details.LivePhotoId is { } live && pictures.TryGetValue(live, out var picture) ? picture : file;
            if (!items.TryGetValue(main.Id, out var item)) items[main.Id] = item = (main, []);
            item.Files.Add(file);
        }
        return ([.. items.Values.OrderByDescending(i => i.Main.Date).Select(i => new Item(i.Main, i.Files))], on);
    }

    private static bool On(Item item, HashSet<string> on) => item.Files.All(f => on.Contains(f.Id));

    private static bool Waits(Item item, HashSet<string> on, BackupSettings settings) => !On(item, on) && (settings.IncludeVideos || !item.Main.IsVideo);

    // A scan's checksum is written in upper case; the computer's, as those sent, in lower.
    private async Task<string?> ChecksumAsync(LibraryFile file, CancellationToken ct)
        => file.ContentHash?.ToLowerInvariant() ?? await _ledger.ChecksumAsync(file.Path, file.SizeBytes, file.ModifiedUtc, ct);

    private static ItemCount Count(IEnumerable<Item> items)
    {
        var list = items.ToList();
        return new ItemCount(list.Count, list.Sum(i => i.Bytes));
    }
}
