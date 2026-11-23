using System.Security.Cryptography;
using System.Text.Json.Nodes;
using PhotoSense.Application.Locations;
using PhotoSense.Application.Organizing;
using PhotoSense.Application.Plans;
using PhotoSense.Application.Vault;
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

/// <summary>A trip folder being made on the computer, or the last one asked for.</summary>
/// <param name="Folder">The folder's name, as the phone asked for it: "2026-07 Glacier National Park".</param>
/// <param name="Stage">How it stands: running while what is missing is sent and the copies moved, ready once made, failed with the problem.</param>
/// <param name="Problem">Why it was not made.</param>
/// <param name="Made">Where the computer made it, as it says: "Pictures/PhotoSense/2026-07 Glacier National Park".</param>
public sealed record TripFolderRun(string Folder, BackupStage Stage, string? Problem, string? Made);

/// <summary>The phone's copies on the computer being named by date, or the last time it was asked.</summary>
/// <param name="Names">How they were to be named.</param>
/// <param name="Stage">How it stands: running, ready once done, or failed with the problem.</param>
/// <param name="Problem">Why they were not renamed.</param>
/// <param name="Renamed">How many the computer renamed.</param>
public sealed record NamesRun(FileNaming Names, BackupStage Stage, string? Problem, int? Renamed);

/// <summary>Hidden items being brought back from the computer to a phone's vault, or the last time it was asked.</summary>
/// <param name="Stage">How it stands.</param>
/// <param name="Problem">Why it stopped.</param>
/// <param name="Restored">How many items the computer gave back, once it is done.</param>
public sealed record RestoreRun(BackupStage Stage, string? Problem, int? Restored);

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
    private readonly IFavoritePhotos _favorites;
    private readonly string _name;
    private readonly string _root;
    private readonly TimeProvider _clock;
    private readonly object _lock = new();
    private readonly IVaultFiles _vault;
    private readonly ILocationStore _locations;
    private readonly TermsAcceptance _terms;
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
        // The trip folder this backup ends by making, and where the computer made it.
        public string? TripFolder;
        public string? Made;
        // How the computer is to name the copies, once this backup is done, and how many it renamed.
        public FileNaming? Names;
        public int? Renamed;
        // The hidden vault's files this backup sends, sealed; and the vault whose files it ends by bringing back, and how many came.
        public List<VaultFileInfo> Hidden = [];
        public string? Restore;
        public int? Restored;
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
    /// <param name="favorites">The person's favorites, which Safe to delete can keep.</param>
    /// <param name="name">What this phone is called, as the computer shows it.</param>
    /// <param name="root">The library's folder.</param>
    /// <param name="clock">How long the computer may stay silent, and when things happen.</param>
    /// <param name="vault">The hidden vault's files, which a backup sends as they are kept, sealed.</param>
    /// <param name="locations">The other places the phone backs up to, whose checked copies count toward Safe to delete too, and whether the computer is on.</param>
    /// <param name="terms">Whether the person accepted the note asking for a backup, which the phone and the computer tell each other.</param>
    public PhoneBackup(IBackupLedger ledger, IComputerFinder finder, IPairingStore pairs, OrganizeLibrary library, IPhotoFileSystem files, Subscription plan, DailyRemovals removals,
        IDeviceStatus device, IFavoritePhotos favorites, string name, string root, TimeProvider clock, IVaultFiles vault, ILocationStore locations, TermsAcceptance terms)
    {
        _terms = terms;
        _vault = vault;
        _locations = locations;
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
                run?.Done.Count ?? 0, run is null ? 0 : run.Queue.Count + run.Hidden.Count, run?.Bytes ?? 0, run is null ? 0 : run.Queue.Sum(f => f.SizeBytes) + run.Hidden.Sum(f => f.Length),
                computer.LastChecked, computer.LastBackup, strip, run?.Problem, settings);
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
        var backedUp = await BackedUpAsync(items, on, ct);
        var safe = items.Where(backedUp).ToList();
        var favorites = await _favorites.FavoritesAsync(ct);
        var since = _clock.GetLocalNow().DateTime - Recently;
        bool Favorite(PhotoItem i) => i.Files.Any(f => favorites.Contains(f.Path));
        bool Recent(PhotoItem i) => i.Main.Date >= since;
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
        var backedUp = await BackedUpAsync(items, on, ct);
        var favorites = await _favorites.FavoritesAsync(ct);
        var since = _clock.GetLocalNow().DateTime - Recently;
        return [.. items.Where(backedUp)
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
        await _removals.RecordAsync(going.Count, going.Sum(i => i.Bytes), allowance, CancellationToken.None);
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
        var computer = await _ledger.ComputerAsync(ct) ?? throw new InvalidOperationException(PairFirst);
        lock (_lock)
        {
            if (_run is { Stage: BackupStage.Running }) return;
            Start(new Run(), computer, only);
        }
    }

    /// <summary>
    /// Makes a trip's folder on the computer: sends first what of the trip is not there yet, videos too, then has the
    /// computer move the trip's copies into a folder of its own, beside the phones' folders. Nothing changes on the
    /// phone. It goes on by itself; <see cref="TripFolder"/> says how it stands.
    /// </summary>
    /// <param name="folder">The folder's name: "2026-07 Glacier National Park".</param>
    /// <param name="paths">The trip's pictures and videos, by their paths; what goes with each goes too.</param>
    /// <param name="ct">Stops it before it starts.</param>
    /// <exception cref="InvalidOperationException">The phone has been paired with no computer, or a backup is running.</exception>
    /// <exception cref="ProNeededException">The plan is not Pro: trip folders are part of Pro (Jamie, 2026-11-20).</exception>
    public async Task MakeFolderAsync(string folder, IReadOnlyCollection<string> paths, CancellationToken ct)
    {
        if (!(await _plan.CurrentAsync(ct)).Pro) throw new ProNeededException(TripsArePro);
        var computer = await _ledger.ComputerAsync(ct) ?? throw new InvalidOperationException(PairFirst);
        var wanted = paths.ToHashSet(PhotoPath.Comparer);
        var ids = PhotoItem.Of(await _library.ListAsync(_root, ct)).Where(i => i.Files.Any(f => wanted.Contains(f.Path))).Select(i => i.Main.Id).ToList();
        lock (_lock)
        {
            if (_run is { Stage: BackupStage.Running }) throw new InvalidOperationException($"A backup to {computer.Name} is running. Make the folder once it ends.");
            Start(new Run { TripFolder = folder }, computer, ids);
        }
    }

    /// <summary>The trip folder being made, or the last one asked for; null when none was asked for since PhotoSense started.</summary>
    public TripFolderRun? TripFolder()
    {
        lock (_lock) return _run is { TripFolder: { } folder } run ? new TripFolderRun(folder, run.Stage, run.Problem, run.Made) : null;
    }

    /// <summary>
    /// Has the computer name this phone's copies by date, or by date and place, and those that arrive from then on
    /// (Pro); or, with <see cref="FileNaming.AsOnPhone"/>, keep the phone's names for those that arrive. It goes on by
    /// itself; <see cref="NamesRun"/> says how it stands.
    /// </summary>
    /// <exception cref="InvalidOperationException">The phone has been paired with no computer, a backup is running, or the plan is not Pro.</exception>
    public async Task RenameOnComputerAsync(FileNaming naming, CancellationToken ct)
    {
        var computer = await _ledger.ComputerAsync(ct) ?? throw new InvalidOperationException(PairFirst);
        if (naming != FileNaming.AsOnPhone && !(await _plan.CurrentAsync(ct)).Pro) throw new InvalidOperationException("Renaming copies by date is part of Pro.");
        lock (_lock)
        {
            if (_run is { Stage: BackupStage.Running }) throw new InvalidOperationException($"A backup to {computer.Name} is running. Rename the files once it ends.");
            Start(new Run { Names = naming }, computer, []);
        }
    }

    /// <summary>The copies being named by date on the computer, or the last time it was asked; null when it was not since PhotoSense started.</summary>
    public NamesRun? NamesRun()
    {
        lock (_lock) return _run is { Names: { } names } run ? new NamesRun(names, run.Stage, run.Problem, run.Renamed) : null;
    }

    /// <summary>
    /// Brings the hidden vault's files sealed with this key back from the computer, after a backup: those this phone
    /// sent, and those another phone with the same recovery key sent. It goes on by itself; <see cref="RestoreRun"/>
    /// says how it stands.
    /// </summary>
    /// <param name="keyId">The id of the vault's key, which the computer finds its files by without the key.</param>
    /// <param name="ct">Stops it before it starts.</param>
    /// <exception cref="InvalidOperationException">The phone has been paired with no computer, or a backup is running.</exception>
    public async Task RestoreHiddenAsync(byte[] keyId, CancellationToken ct)
    {
        var computer = await _ledger.ComputerAsync(ct) ?? throw new InvalidOperationException(PairFirst);
        lock (_lock)
        {
            if (_run is { Stage: BackupStage.Running }) throw new InvalidOperationException($"A backup to {computer.Name} is running. Restore once it ends.");
            Start(new Run { Restore = Convert.ToHexStringLower(keyId) }, computer, []);
        }
    }

    /// <summary>The hidden items being brought back, or the last time it was asked; null when it was not since PhotoSense started.</summary>
    public RestoreRun? RestoreRun()
    {
        lock (_lock) return _run is { Restore: not null } run ? new RestoreRun(run.Stage, run.Problem, run.Restored) : null;
    }

    /// <summary>Each of the hidden vault's files, by name, and whether the computer holds it, as it last said.</summary>
    public async Task<IReadOnlyDictionary<string, bool>> HiddenOnComputerAsync(CancellationToken ct)
    {
        var held = await _ledger.OnComputerAsync(ct);
        var on = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var file in Hidden()) on[file.Name] = held.Contains(await HiddenChecksumAsync(file, ct));
        return on;
    }

    /// <summary>How many of this phone's files the computer holds, as it last said.</summary>
    public async Task<int> HeldAsync(CancellationToken ct) => (await _ledger.OnComputerAsync(ct)).Count;

    /// <summary>The computer backups go to, or null when the phone has been paired with none.</summary>
    public Task<BackupComputer?> ComputerAsync(CancellationToken ct) => _ledger.ComputerAsync(ct);

    /// <summary>The trip folders the computer made when this phone asked, by their names.</summary>
    public Task<IReadOnlySet<string>> FoldersMadeAsync(CancellationToken ct) => _ledger.FoldersMadeAsync(ct);

    private const string PairFirst = "Pair with a computer first: in Settings, choose Computer.";

    /// <summary>What the free plan is told when it asks for a trip folder: the Pro sheet says it too.</summary>
    public const string TripsArePro = "Trip folders on your computer are part of Pro.";

    // Called holding the lock.
    private void Start(Run run, BackupComputer computer, IReadOnlyCollection<string>? only)
    {
        _run = run;
        _stop = new CancellationTokenSource();
        var stop = _stop.Token;
        Running = Task.Run(() => RunAsync(run, computer, only, stop), CancellationToken.None);
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
        if (!settings.Automatic || (settings.OnlyWhileCharging && !_device.Charging) || await _ledger.ComputerAsync(ct) is null || !await ComputerOnAsync(ct)) return false;
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
            var (connection, linked) = await LinkAsync(computer, stop);
            await using var connected = connection;
            using var channel = linked.Channel;
            // The computer may have been named again since.
            computer = computer with { Name = linked.Other.Name };
            await channel.SendAsync(new JsonObject
            {
                ["t"] = "backup", ["kind"] = _device.Kind, ["plan"] = PlanMessage.Write(await _plan.CurrentAsync(stop)), ["accepted"] = TermsAcceptance.Write(await _terms.AcceptedAsync(stop)),
            }, stop);
            var first = await channel.ReceiveAsync(stop, "held", "paused");
            if (Wire.Text(first, "t") == "paused") throw new TransferException(Wire.Text(first, "why"));
            var folder = computer.Folder;
            var trips = computer.Trips;
            var names = computer.Names;
            DateOnly? accepted = null;
            var held = (await Lists.ReceiveAsync(channel, "held", "sha256", first, last =>
                {
                    folder = Wire.Text(last, "folder");
                    // A computer from before trip folders does not say where it makes them, nor how it names copies.
                    trips = last["trips"] is JsonValue said ? said.GetValue<string>() : trips;
                    if (last["names"] is JsonValue naming && Enum.TryParse<FileNaming>(naming.GetValue<string>(), out var known)) names = known;
                    accepted = TermsAcceptance.Read(last["accepted"]);
                }, stop))
                .Select(n => n?.GetValue<string>()).OfType<string>().ToHashSet(StringComparer.Ordinal);
            await _terms.HeardAsync(accepted, stop);
            await _ledger.KeepOnComputerAsync(held, all: true, stop);
            computer = computer with { Folder = folder, Trips = trips, Names = names, LastChecked = _clock.GetUtcNow() };
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
            var hidden = new List<VaultFileInfo>();
            // The vault goes with a whole backup, sealed as it is kept; never with what was chosen to send.
            if (only is null)
                foreach (var file in Hidden())
                {
                    var sha = await HiddenChecksumAsync(file, stop);
                    if (held.Contains(sha)) continue;
                    hidden.Add(file);
                    said[HiddenId(file)] = sha;
                }
            lock (_lock)
            {
                run.Queue = queue;
                run.Hidden = hidden;
            }
            await Lists.SendAsync(channel, "offer", "files", [
                .. queue.Select(f => (JsonNode)new JsonObject
                {
                    ["id"] = f.Id, ["name"] = f.Name, ["size"] = f.SizeBytes, ["modified"] = f.ModifiedUtc.ToString("O"), ["sha256"] = said[f.Id],
                }),
                .. hidden.Select(f => (JsonNode)new JsonObject
                {
                    ["id"] = HiddenId(f), ["name"] = f.Name, ["size"] = f.Length, ["modified"] = f.ModifiedUtc.ToString("O"), ["sha256"] = said[HiddenId(f)], ["hidden"] = true,
                })], null, stop);
            var sent = new Dictionary<string, string>(StringComparer.Ordinal);
            while (true)
            {
                var asked = await channel.ReceiveAsync(stop, "get", "got", "done");
                var id = Wire.Text(asked, "t") == "done" ? null : Wire.Text(asked, "id");
                if (id is null) break;
                var file = queue.FirstOrDefault(f => f.Id == id);
                var sealedFile = hidden.FirstOrDefault(f => HiddenId(f) == id);
                if (file is null && sealedFile is null) throw new TransferException("The computer asked for a file that was not offered.");
                if (Wire.Text(asked, "t") == "get")
                {
                    sent[id] = file is not null ? await SendFileAsync(channel, run, file, stop) : await SendHiddenAsync(channel, run, sealedFile!, stop);
                    continue;
                }
                // On the computer only with the very bytes that were sent, or that the phone said it had.
                var check = Wire.Text(asked, "sha256");
                if ((sent.TryGetValue(id, out var mine) ? mine : said[id]) != check)
                    throw new TransferException("The computer's check of a file did not match this phone's. Back up again.");
                await _ledger.KeepOnComputerAsync([check], all: false, stop);
                lock (_lock) run.Done.Add(id);
            }
            if (run.TripFolder is { } trip) await TripFolderAsync(channel, run, trip, items.Where(i => only!.Contains(i.Main.Id)), stop);
            if (run.Names is { } naming)
            {
                await channel.SendAsync(new JsonObject { ["t"] = "names", ["style"] = naming.ToString() }, stop);
                var named = await channel.ReceiveAsync(stop, "named");
                if (named["problem"] is JsonValue problem) throw new TransferException(problem.GetValue<string>());
                lock (_lock) run.Renamed = named["renamed"]!.GetValue<int>();
                computer = computer with { Names = naming };
            }
            if (run.Restore is { } key) await RestoreAsync(channel, run, key, stop);
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

    /// <summary>The telling started by <see cref="TellAccepted"/>, for whoever waits for it: true when the computer heard.</summary>
    public Task<bool> Telling { get; private set; } = Task.FromResult(false);

    /// <summary>
    /// Tells the computer this phone was paired with that the person accepted the note asking for a backup, so that it
    /// does not ask there too. It goes on by itself; a computer not reached now hears it with the next backup, which
    /// always says it.
    /// </summary>
    public void TellAccepted()
    {
        // The page has its answer already, and nothing it does later stops the telling: it ends by itself, within the minute's limit.
        Telling = Task.Run(() => TellAsync(CancellationToken.None), CancellationToken.None);
    }

    private async Task<bool> TellAsync(CancellationToken ct)
    {
        if (await _ledger.ComputerAsync(ct) is not { } computer) return false;
        try
        {
            var (connection, linked) = await LinkAsync(computer, ct);
            await using var connected = connection;
            using var channel = linked.Channel;
            await channel.SendAsync(new JsonObject { ["t"] = "bye", ["accepted"] = TermsAcceptance.Write(await _terms.AcceptedAsync(ct)) }, ct);
            return true;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Finds the computer on the Wi-Fi and proves the pairing; the connection is the caller's to close once it has it.
    private async Task<(Stream Connection, Linked Linked)> LinkAsync(BackupComputer computer, CancellationToken stop)
    {
        var self = await _pairs.SelfIdAsync(stop);
        if (await _pairs.GetAsync(computer.ComputerId, stop) is null)
            throw new TransferException($"This phone no longer knows {computer.Name}. Pair them again: in Settings, choose Computer.");
        var found = (await _finder.FindAsync(stop)).FirstOrDefault(c => c.Id == computer.ComputerId)
            ?? throw new TransferException($"{computer.Name} was not found on this Wi-Fi. Check that PhotoSense is open on it, and that both are on the same Wi-Fi.");
        var connection = await TimedConnection.WithinAsync(found.Connect, _clock, stop);
        var handed = false;
        try
        {
            // Both know each other: the pairing is proved, and no number is ever compared here.
            var linked = await Pairing.PhoneAsync(new TimedConnection(connection, _clock), new Device(self, _name), _pairs, (_, _, _) => Task.FromResult(false), ticket: null, stop);
            handed = true;
            return (connection, linked);
        }
        finally
        {
            if (!handed) await connection.DisposeAsync();
        }
    }

    // Has the computer move the trip's copies, every file of each, into the trip's folder.
    private async Task TripFolderAsync(SecureChannel channel, Run run, string folder, IEnumerable<PhotoItem> trip, CancellationToken ct)
    {
        var checksums = new List<string>();
        foreach (var file in trip.SelectMany(i => i.Files))
            if (await ChecksumAsync(file, ct) is { } sha) checksums.Add(sha);
        await Lists.SendAsync(channel, "trip", "sha256", checksums, new JsonObject { ["folder"] = folder }, ct);
        var made = await channel.ReceiveAsync(ct, "trip-made");
        if (made["problem"] is JsonValue problem) throw new TransferException(problem.GetValue<string>());
        await _ledger.KeepFolderMadeAsync(folder, ct);
        lock (_lock) run.Made = Wire.Text(made, "folder");
    }

    private async Task<string> SendFileAsync(SecureChannel channel, Run run, LibraryFile file, CancellationToken ct)
    {
        lock (_lock) run.Next = Math.Max(run.Next, run.Queue.IndexOf(file));
        // A picture kept only in iCloud is fetched first, which can take a while; the computer is told of it.
        await using var read = await channel.MeanwhileAsync(stop => _files.OpenReadAsync(file.Path, stop), _clock, ct);
        var check = await SendBytesAsync(channel, run, file.Id, read, ct);
        await _ledger.KeepChecksumAsync(file.Path, file.SizeBytes, file.ModifiedUtc, check, ct);
        await channel.SendAsync(new JsonObject { ["t"] = "end", ["id"] = file.Id, ["sha256"] = check }, ct);
        return check;
    }

    private async Task<string> SendHiddenAsync(SecureChannel channel, Run run, VaultFileInfo file, CancellationToken ct)
    {
        await using var read = _vault.OpenRead(file.Name);
        var check = await SendBytesAsync(channel, run, HiddenId(file), read, ct);
        await _ledger.KeepChecksumAsync(HiddenPath(file.Name), file.Length, file.ModifiedUtc, check, ct);
        await channel.SendAsync(new JsonObject { ["t"] = "end", ["id"] = HiddenId(file), ["sha256"] = check }, ct);
        return check;
    }

    // Sends a file's bytes, after saying how many; returns their checksum.
    private async Task<string> SendBytesAsync(SecureChannel channel, Run run, string id, Stream read, CancellationToken ct)
    {
        await channel.SendAsync(new JsonObject { ["t"] = "file", ["id"] = id, ["size"] = read.Length }, ct);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var piece = new byte[SecureChannel.PieceSize];
        for (int n; (n = await read.ReadAsync(piece, ct)) > 0;)
        {
            hash.AppendData(piece, 0, n);
            await channel.SendDataAsync(piece.AsMemory(0, n), ct);
            lock (_lock) run.Bytes += n;
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    // Brings back the vault's files sealed with this key that the computer holds and this phone has not, each checked.
    private async Task RestoreAsync(SecureChannel channel, Run run, string key, CancellationToken ct)
    {
        await channel.SendAsync(new JsonObject { ["t"] = "vault", ["key"] = key }, ct);
        var offered = await Lists.ReceiveAsync(channel, "vault", "files", null, null, ct);
        var mine = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Hidden()) mine.Add(await HiddenChecksumAsync(file, ct));
        var restored = 0;
        foreach (var file in offered.OfType<JsonObject>())
        {
            var sha = Wire.Text(file, "sha256");
            if (!mine.Add(sha)) continue;
            await channel.SendAsync(new JsonObject { ["t"] = "fetch", ["sha256"] = sha }, ct);
            var size = (await channel.ReceiveAsync(ct, "file"))["size"] is JsonValue said && said.TryGetValue(out long n) && n >= 0
                ? n : throw new TransferException("The computer sent something PhotoSense does not understand.");
            // Checked before it is the vault's: a file cut short or damaged does not stay.
            await _vault.WriteAsync($"{sha[..32]}{VaultBox.Extension}", async output =>
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                for (long written = 0; written < size;)
                {
                    var piece = await channel.ReceiveDataAsync(ct);
                    if (written + piece.Length > size) throw new TransferException("The computer sent more of a file than it said it would.");
                    hash.AppendData(piece);
                    await output.WriteAsync(piece, ct);
                    written += piece.Length;
                }
                if (Convert.ToHexStringLower(hash.GetHashAndReset()) != sha || Wire.Text(await channel.ReceiveAsync(ct, "end"), "sha256") != sha)
                    throw new TransferException("A hidden item arrived damaged, so it was not kept. Restore again.");
            }, ct);
            restored++;
        }
        await channel.SendAsync(new JsonObject { ["t"] = "fetched" }, ct);
        lock (_lock) run.Restored = restored;
    }

    // The vault's own files, as backups send them.
    private IEnumerable<VaultFileInfo> Hidden() => _vault.List().Where(f => f.Name.EndsWith(VaultBox.Extension, StringComparison.Ordinal));

    private static string HiddenId(VaultFileInfo file) => $"hidden:{file.Name}";

    // What the ledger knows a vault file's checksum by: no photo of the library has a path like it.
    private static string HiddenPath(string name) => $"vault:{name}";

    private async Task<string> HiddenChecksumAsync(VaultFileInfo file, CancellationToken ct)
    {
        if (await _ledger.ChecksumAsync(HiddenPath(file.Name), file.Length, file.ModifiedUtc, ct) is { } known) return known;
        string sha;
        await using (var read = _vault.OpenRead(file.Name)) sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(read, ct));
        await _ledger.KeepChecksumAsync(HiddenPath(file.Name), file.Length, file.ModifiedUtc, sha, ct);
        return sha;
    }

    // A few of those sent last and those coming next.
    private static List<BackupTile> Strip(Run run)
    {
        var from = Math.Max(0, Math.Min(run.Next - StripSize / 2, run.Queue.Count - StripSize));
        return [.. run.Queue.Skip(from).Take(StripSize).Select(f => new BackupTile(f.Id, run.Done.Contains(f.Id)))];
    }

    // Every picture and video in the library, each with its files, and the checksums of those the computer holds.
    private async Task<(List<PhotoItem> Items, HashSet<string> OnComputer)> ItemsAsync(CancellationToken ct)
    {
        var files = await _library.ListAsync(_root, ct);
        var on = new HashSet<string>(StringComparer.Ordinal);
        var held = await _ledger.OnComputerAsync(ct);
        foreach (var file in files)
            if (await ChecksumAsync(file, ct) is { } sha && held.Contains(sha)) on.Add(file.Id);
        return (PhotoItem.Of(files), on);
    }

    private static bool On(PhotoItem item, HashSet<string> on) => item.Files.All(f => on.Contains(f.Id));

    // The computer is on unless the person turned it off among the locations.
    private async Task<bool> ComputerOnAsync(CancellationToken ct)
        => (await _locations.ListAsync(ct)).FirstOrDefault(l => l.Kind == LocationKind.Computer)?.On != false;

    // Whether a picture or video counts as backed up: every file of it checked on as many locations that are on as the
    // person chose (any one, or two), the computer among them while it is on.
    private async Task<Func<PhotoItem, bool>> BackedUpAsync(List<PhotoItem> items, HashSet<string> onComputer, CancellationToken ct)
    {
        var computerOn = await ComputerOnAsync(ct);
        var need = await _locations.CountAtAsync(ct);
        var others = new List<IReadOnlyDictionary<string, LocationCopy>>();
        foreach (var location in (await _locations.ListAsync(ct)).Where(l => l.On && l.Kind != LocationKind.Computer)) others.Add(await _locations.CopiesAsync(location.Id, ct));
        var sums = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var file in items.SelectMany(i => i.Files)) sums[file.Id] = await ChecksumAsync(file, ct);
        bool Held(IReadOnlyDictionary<string, LocationCopy> copies, LibraryFile f) => sums.GetValueOrDefault(f.Id) is { } sha && copies.ContainsKey(sha);
        return i => (computerOn && On(i, onComputer) ? 1 : 0) + others.Count(copies => i.Files.All(f => Held(copies, f))) >= need;
    }

    private static bool Waits(PhotoItem item, HashSet<string> on, BackupSettings settings) => !On(item, on) && (settings.IncludeVideos || !item.Main.IsVideo);

    // A scan's checksum is written in upper case; the computer's, as those sent, in lower.
    private async Task<string?> ChecksumAsync(LibraryFile file, CancellationToken ct)
        => file.ContentHash?.ToLowerInvariant() ?? await _ledger.ChecksumAsync(file.Path, file.SizeBytes, file.ModifiedUtc, ct);

    private static ItemCount Count(IEnumerable<PhotoItem> items)
    {
        var list = items.ToList();
        return new ItemCount(list.Count, list.Sum(i => i.Bytes));
    }
}
