using PhotoSense.Application.Organizing;
using PhotoSense.Application.Plans;
using PhotoSense.Application.Transfer;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Application.Locations;

/// <summary>What the locations are doing: backing up, checking their copies, or sending what was chosen to one.</summary>
public enum LocationTask
{
    /// <summary>Backing up to every location that is on.</summary>
    Backup,
    /// <summary>Checking every copy the locations that are on hold.</summary>
    Check,
    /// <summary>Sending what was chosen to one location.</summary>
    Send,
}

/// <summary>How far one location has got in what the locations are doing.</summary>
/// <param name="Id">The location.</param>
/// <param name="Done">Files done.</param>
/// <param name="Total">Files to do.</param>
/// <param name="Bytes">Bytes sent or read back.</param>
/// <param name="TotalBytes">Bytes to send.</param>
/// <param name="Problem">Why it stopped part way; null when it did not.</param>
public sealed record LocationProgress(string Id, int Done, int Total, long Bytes, long TotalBytes, string? Problem);

/// <summary>What sending to a location did.</summary>
/// <param name="LocationId">Where they went.</param>
/// <param name="Items">Pictures and videos sent.</param>
/// <param name="Matched">Those every file of which matches the copy there, which can be deleted from this device.</param>
/// <param name="Bytes">What those take on this device.</param>
public sealed record SendResult(string LocationId, int Items, int Matched, long Bytes);

/// <summary>What the locations are doing, or last did.</summary>
/// <param name="Task">What it is.</param>
/// <param name="Stage">How it stands.</param>
/// <param name="Locations">Each location's part.</param>
/// <param name="Sent">What a send did, once it is done.</param>
public sealed record LocationActivity(LocationTask Task, BackupStage Stage, IReadOnlyList<LocationProgress> Locations, SendResult? Sent);

/// <summary>A location, as the page lists it.</summary>
/// <param name="Id">How PhotoSense knows it.</param>
/// <param name="Kind">What sort it is.</param>
/// <param name="Name">What it is called.</param>
/// <param name="On">Backups go to it.</param>
/// <param name="Connected">Signed in to, paired, or added: it can be turned on.</param>
/// <param name="Available">It can be signed in to from this version of PhotoSense.</param>
/// <param name="Account">The account or user, as it shows it.</param>
/// <param name="Settings">How it is reached, less its secrets.</param>
/// <param name="Copies">Files it holds, checked.</param>
/// <param name="LastBackup">When a backup to it last finished.</param>
/// <param name="LastChecked">When its copies were last checked.</param>
/// <param name="Problem">Why the last backup or check of it stopped.</param>
public sealed record LocationView(string Id, LocationKind Kind, string Name, bool On, bool Connected, bool Available, string? Account, IReadOnlyDictionary<string, string> Settings,
    int Copies, DateTimeOffset? LastBackup, DateTimeOffset? LastChecked, string? Problem);

/// <summary>The locations, how many a copy must be checked on to count as backed up, and what they are doing.</summary>
/// <param name="Locations">The computer, the cloud services, then those added, by name.</param>
/// <param name="CountAt">On how many locations a copy must be checked: 1 or 2.</param>
/// <param name="Pro">The plan lets more than one location be on, and Connect to server add more.</param>
/// <param name="Activity">What they are doing, or last did since PhotoSense started.</param>
public sealed record LocationsState(IReadOnlyList<LocationView> Locations, int CountAt, bool Pro, LocationActivity? Activity);

/// <summary>A location as Connect to server adds it, or changes one added.</summary>
/// <param name="Id">The location changed; null to add one.</param>
/// <param name="Kind">What sort it is.</param>
/// <param name="Name">What the person calls it; the kind's name when left empty.</param>
/// <param name="Settings">How it is reached, less its secrets.</param>
/// <param name="Secrets">Its keys and passwords.</param>
public sealed record ServerConnection(string? Id, LocationKind Kind, string Name, IReadOnlyDictionary<string, string> Settings, IReadOnlyDictionary<string, string> Secrets);

/// <summary>A location in full: where its folder is, and how much it holds.</summary>
/// <param name="Location">The location.</param>
/// <param name="Where">Its PhotoSense folder, as the person would recognise it.</param>
/// <param name="Space">How much it holds, when it says.</param>
/// <param name="Problem">Why it could not be reached just now.</param>
public sealed record LocationDetails(LocationView Location, string? Where, RemoteSpace? Space, string? Problem);

/// <summary>The free plan backs up to one location, and another is on.</summary>
/// <param name="current">The location that is on.</param>
/// <param name="wanted">The one the person wanted on.</param>
public sealed class LocationLimitException(string current, string wanted)
    : InvalidOperationException($"The free plan backs up to one location, and {current} is on. Pro adds as many as you like.")
{
    /// <summary>The location that is on.</summary>
    public string Current { get; } = current;

    /// <summary>The id of the one the person wanted on.</summary>
    public string Wanted { get; } = wanted;
}

/// <summary>Something asked for is part of Pro, which the plan is not.</summary>
/// <param name="message">What is part of Pro, as the Pro sheet says it.</param>
public sealed class ProNeededException(string message) : InvalidOperationException(message);

/// <summary>
/// The places a phone backs up to: the computer it was paired with, the cloud services the person signs in to, and those
/// Connect to server adds. Backups go to every one that is on (one on the free plan); each copy counts only once the
/// location's own checksum of it matches PhotoSense's, or, where it keeps none, PhotoSense has read it back. Nothing is
/// ever replaced there, nor deleted there by PhotoSense: a test file only, as Connect to server checks it can write.
/// </summary>
public sealed class BackupLocations
{
    private const string ComputerId = "computer";
    private const string PairFirst = "Pair with a computer first: in Settings, choose Computer.";

    private readonly ILocationStore _store;
    private readonly ILocationSecrets _secrets;
    private readonly IRemoteFolders _remotes;
    private readonly ICloudAccounts _accounts;
    private readonly IWebSignIn _signIn;
    private readonly PhoneBackup _computer;
    private readonly IBackupLedger _ledger;
    private readonly OrganizeLibrary _library;
    private readonly IPhotoFileSystem _files;
    private readonly IPhotoChangesTogether _together;
    private readonly Subscription _plan;
    private readonly DailyRemovals _removals;
    private readonly IDeviceStatus _device;
    private readonly string _root;
    private readonly TimeProvider _clock;
    private readonly object _lock = new();
    private Run? _run;
    private CancellationTokenSource? _stop;

    // What the locations are doing, and how far each has got.
    private sealed class Run(LocationTask task, string[] ids)
    {
        public readonly LocationTask Task = task;
        public BackupStage Stage = BackupStage.Running;
        public readonly Dictionary<string, Part> Parts = ids.ToDictionary(id => id, _ => new Part(), StringComparer.Ordinal);
        public SendResult? Sent;
        public CancellationToken Stopping;
        // What a send matched, every file of each, which can then be deleted from this device.
        public List<(IReadOnlyList<string> Paths, long Bytes)> Matched = [];
    }

    private sealed class Part
    {
        public int Done, Total;
        public long Bytes, TotalBytes;
        public string? Problem;
    }

    /// <summary>The phone's locations.</summary>
    /// <param name="store">The locations, and what each holds.</param>
    /// <param name="secrets">Their keys, passwords and tokens.</param>
    /// <param name="remotes">Reaches each location's folder.</param>
    /// <param name="accounts">Signing in to the cloud services.</param>
    /// <param name="signIn">The system's sheet the person signs in on.</param>
    /// <param name="computer">Backing up to the computer, which is a location too.</param>
    /// <param name="ledger">The files' checksums, and how the phone backs up by itself.</param>
    /// <param name="library">The library's pictures and videos.</param>
    /// <param name="files">Where they are kept.</param>
    /// <param name="together">Deletes what was sent to the phone's Recently Deleted or Trash, all after one question.</param>
    /// <param name="plan">More than one location, and Connect to server, are part of Pro.</param>
    /// <param name="removals">What is deleted after sending counts toward the day's free removals.</param>
    /// <param name="device">Whether the phone is charging, for automatic backup.</param>
    /// <param name="root">The library's folder.</param>
    /// <param name="clock">When things happen.</param>
    public BackupLocations(ILocationStore store, ILocationSecrets secrets, IRemoteFolders remotes, ICloudAccounts accounts, IWebSignIn signIn, PhoneBackup computer,
        IBackupLedger ledger, OrganizeLibrary library, IPhotoFileSystem files, IPhotoChangesTogether together, Subscription plan, DailyRemovals removals, IDeviceStatus device, string root, TimeProvider clock)
    {
        _store = store;
        _secrets = secrets;
        _remotes = remotes;
        _accounts = accounts;
        _signIn = signIn;
        _computer = computer;
        _ledger = ledger;
        _library = library;
        _files = files;
        _together = together;
        _plan = plan;
        _removals = removals;
        _device = device;
        _root = root;
        _clock = clock;
    }

    /// <summary>What the locations are doing now, for whoever waits for it to end.</summary>
    public Task Running { get; private set; } = Task.CompletedTask;

    /// <summary>The locations, how many a copy must be checked on, and what they are doing. On the free plan only the one used last stays on.</summary>
    public async Task<LocationsState> StateAsync(CancellationToken ct)
    {
        var pro = (await _plan.CurrentAsync(ct)).Pro;
        if (!pro) await KeepOneOnAsync(ct);
        var views = new List<LocationView>();
        foreach (var location in await ListAsync(ct)) views.Add(await ViewAsync(location, ct));
        return new LocationsState(views, await _store.CountAtAsync(ct), pro, Activity());
    }

    /// <summary>One location in full: where its folder is and how much it holds, asked of it now.</summary>
    /// <exception cref="KeyNotFoundException">There is no such location.</exception>
    public async Task<LocationDetails> DetailsAsync(string id, CancellationToken ct)
    {
        var location = await FindAsync(id, ct);
        var view = await ViewAsync(location, ct);
        if (location.Kind == LocationKind.Computer || !view.Connected) return new LocationDetails(view, location.Settings.GetValueOrDefault("folder"), null, null);
        var folder = await OpenAsync(location, ct);
        try
        {
            return new LocationDetails(view, folder.Where, await folder.SpaceAsync(ct), null);
        }
        catch (IOException ex)
        {
            return new LocationDetails(view, folder.Where, null, ex.Message);
        }
    }

    /// <summary>
    /// Turns a location on: the computer once paired; a cloud service, signing in first if it has not been; one added.
    /// On the free plan, with another on, nothing changes and <see cref="LocationLimitException"/> says which.
    /// </summary>
    /// <exception cref="InvalidOperationException">The computer has not been paired, or the service cannot be signed in to from this version.</exception>
    /// <exception cref="OperationCanceledException">The person closed the sign-in.</exception>
    /// <exception cref="IOException">The service refused the sign-in.</exception>
    public async Task TurnOnAsync(string id, CancellationToken ct)
    {
        var location = await FindAsync(id, ct);
        if (location.Kind == LocationKind.Computer && await _ledger.ComputerAsync(ct) is null) throw new InvalidOperationException(PairFirst);
        if (LocationKinds.IsCloud(location.Kind) && (await _secrets.ReadAsync(id, ct)).Count == 0) location = await SignInAsync(location, ct);
        await LimitAsync(location, ct);
        await _store.SaveAsync(location with { On = true, LastUsed = _clock.GetUtcNow() }, ct);
    }

    /// <summary>Turns a location off: backups stop going there, and its copies stop counting toward Safe to delete. Nothing there is deleted.</summary>
    public async Task TurnOffAsync(string id, CancellationToken ct)
    {
        var location = await FindAsync(id, ct);
        await _store.SaveAsync(location with { On = false }, ct);
    }

    /// <summary>On the free plan, turns this location on in place of the one that is on.</summary>
    public async Task UseInsteadAsync(string id, CancellationToken ct)
    {
        foreach (var other in (await ListAsync(ct)).Where(l => l.On && l.Id != id)) await _store.SaveAsync(other with { On = false }, ct);
        await TurnOnAsync(id, ct);
    }

    /// <summary>Signs out of a cloud service: its tokens and what PhotoSense knew it held are forgotten. Nothing there is deleted.</summary>
    /// <exception cref="InvalidOperationException">The location is not a cloud service.</exception>
    public async Task DisconnectAsync(string id, CancellationToken ct)
    {
        var location = await FindAsync(id, ct);
        if (!LocationKinds.IsCloud(location.Kind)) throw new InvalidOperationException("Only a cloud service is signed out of.");
        await _secrets.DeleteAsync(id, ct);
        await _store.RemoveAsync(id, ct);
    }

    /// <summary>Removes a location Connect to server added: its keys and what PhotoSense knew it held are forgotten. Nothing there is deleted.</summary>
    /// <exception cref="InvalidOperationException">The location was not added by Connect to server.</exception>
    public async Task RemoveAsync(string id, CancellationToken ct)
    {
        var location = await FindAsync(id, ct);
        if (!LocationKinds.IsServer(location.Kind)) throw new InvalidOperationException("Only a location added by Connect to server is removed.");
        await _secrets.DeleteAsync(id, ct);
        await _store.RemoveAsync(id, ct);
    }

    /// <summary>Names a location Connect to server added as the person wants it.</summary>
    /// <exception cref="InvalidOperationException">The location was not added by Connect to server, or the name is empty.</exception>
    public async Task RenameAsync(string id, string name, CancellationToken ct)
    {
        var location = await FindAsync(id, ct);
        if (!LocationKinds.IsServer(location.Kind)) throw new InvalidOperationException($"{location.Name} keeps its own name.");
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Enter a name.");
        await _store.SaveAsync(location with { Name = name.Trim() }, ct);
    }

    /// <summary>Keeps on how many locations a copy must be checked before a photo counts as backed up: 1 or 2.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Neither 1 nor 2.</exception>
    public Task SetCountAtAsync(int locations, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(locations, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(locations, 2);
        return _store.SaveCountAtAsync(locations, ct);
    }

    /// <summary>
    /// Connect to server (Pro): checks the location can be reached, lists its folder, writes a small test file, reads it
    /// back and compares its checksum, and deletes it; only then is the location kept, and turned on. Changing one added
    /// checks it the same way, and forgets what it held when it now points elsewhere.
    /// </summary>
    /// <returns>The location's id.</returns>
    /// <exception cref="InvalidOperationException">The plan is not Pro, something required is missing, or there is no such location to change.</exception>
    /// <exception cref="IOException">The check failed; the message says at which step, and what to change.</exception>
    public async Task<string> ConnectServerAsync(ServerConnection connection, CancellationToken ct)
    {
        if (!(await _plan.CurrentAsync(ct)).Pro) throw new ProNeededException("Connecting your own server, like Amazon S3, Azure or a home NAS, is part of Pro.");
        if (ServerFields.Missing(connection.Kind, connection.Settings, connection.Secrets) is { } missing) throw new InvalidOperationException(missing);
        var before = connection.Id is null ? null : await FindAsync(connection.Id, ct);
        var name = string.IsNullOrWhiteSpace(connection.Name) ? LocationKinds.NameOf(connection.Kind) : connection.Name.Trim();
        var location = new BackupLocation(connection.Id ?? Guid.NewGuid().ToString("N"), connection.Kind, name, true, connection.Settings.GetValueOrDefault("user"),
            connection.Settings, _clock.GetUtcNow(), before?.LastBackup, before?.LastChecked, null);
        await CheckAsync(_remotes.Open(location, connection.Secrets, (_, _) => Task.CompletedTask), name, ct);
        // What it held is no longer known once it points elsewhere.
        if (before is not null && !Same(before.Settings, connection.Settings)) await _store.RemoveAsync(before.Id, ct);
        await _secrets.SaveAsync(location.Id, connection.Secrets, ct);
        await _store.SaveAsync(location, ct);
        return location.Id;
    }

    /// <summary>
    /// Backs up to every location that is on: the computer as it always has, and each other one the pictures and videos
    /// it does not hold, videos as the settings say, each checked as it arrives. It goes on by itself.
    /// </summary>
    /// <exception cref="InvalidOperationException">No location is on.</exception>
    public async Task BackupAsync(CancellationToken ct)
    {
        var on = (await StateAsync(ct)).Locations.Where(l => l.On).ToList();
        if (on.Count == 0) throw new InvalidOperationException("Turn on a location first: in Settings, choose Locations.");
        if (on.Any(l => l.Kind == LocationKind.Computer)) await _computer.StartAsync(null, ct);
        var others = on.Where(l => l.Kind != LocationKind.Computer).Select(l => l.Id).ToArray();
        if (others.Length > 0) Start(new Run(LocationTask.Backup, others), run => BackupRunAsync(run, null, run.Stopping));
    }

    /// <summary>Starts a backup to the locations other than the computer when one is due, as <see cref="PhoneBackup.AutomaticAsync"/> does for the computer.</summary>
    /// <returns>Whether a backup started.</returns>
    public async Task<bool> AutomaticAsync(CancellationToken ct)
    {
        var settings = await _ledger.SettingsAsync(ct);
        lock (_lock) if (_run is { Stage: BackupStage.Running }) return false;
        if (!settings.Automatic || (settings.OnlyWhileCharging && !_device.Charging) || !(await _plan.CurrentAsync(ct)).Pro) return false;
        var others = (await StateAsync(ct)).Locations.Where(l => l.On && l.Kind != LocationKind.Computer).Select(l => l.Id).ToArray();
        if (others.Length == 0) return false;
        Start(new Run(LocationTask.Backup, others), run => BackupRunAsync(run, null, run.Stopping));
        return true;
    }

    /// <summary>
    /// Check all backups: asks each location that is on, the computer aside, for its checksum of every copy it holds, or
    /// reads back those it keeps none for; a copy missing or changed stops counting, and the next backup sends it again.
    /// </summary>
    public async Task CheckAllAsync(CancellationToken ct)
    {
        var others = (await StateAsync(ct)).Locations.Where(l => l.On && l.Kind != LocationKind.Computer).Select(l => l.Id).ToArray();
        if (others.Length == 0) throw new InvalidOperationException("Turn on a location other than the computer first: the computer checks its own copies at each backup.");
        Start(new Run(LocationTask.Check, others), CheckRunAsync);
    }

    /// <summary>Send to: sends these pictures and videos, every file of each, videos too, to one location, each checked. It goes on by itself.</summary>
    /// <param name="id">The location, which is not the computer: the computer's Send now sends there.</param>
    /// <param name="ids">The pictures and videos, as Library knows them.</param>
    /// <param name="ct">Stops it before it starts.</param>
    /// <exception cref="InvalidOperationException">The location is the computer, or is off.</exception>
    public async Task SendAsync(string id, IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        var location = await FindAsync(id, ct);
        if (location.Kind == LocationKind.Computer || !location.On) throw new InvalidOperationException($"Turn {location.Name} on first: in Settings, choose Locations.");
        Start(new Run(LocationTask.Send, [id]), run => BackupRunAsync(run, ids, run.Stopping));
    }

    /// <summary>
    /// After a send, deletes from this device what was sent and matched, the largest first and no more than
    /// <paramref name="atMost"/>: to its Recently Deleted or Trash, after its own question, each counting toward the day's
    /// free removals.
    /// </summary>
    /// <exception cref="InvalidOperationException">Nothing was sent, or more than the free plan has left today.</exception>
    /// <exception cref="IOException">The person said no, or the phone could not delete them.</exception>
    public async Task<SafeRemoval> DeleteSentAsync(int? atMost, CancellationToken ct)
    {
        List<(IReadOnlyList<string> Paths, long Bytes)> matched;
        lock (_lock) matched = _run is { Task: LocationTask.Send, Stage: BackupStage.Ready } run ? [.. run.Matched.OrderByDescending(m => m.Bytes)] : [];
        if (matched.Count == 0) throw new InvalidOperationException("Nothing sent is waiting to be deleted.");
        var going = matched.Take(atMost ?? matched.Count).ToList();
        var before = _device.Storage;
        var allowance = await _removals.TodayAsync(ct);
        if (!allowance.Allows(going.Count)) throw new InvalidOperationException(DailyRemovals.Refusal(allowance, going.Count));
        _together.EraseAll([.. going.SelectMany(m => m.Paths)]);
        // Deleted: counted, and forgotten, whatever the caller does now.
        await _removals.RecordAsync(going.Count, going.Sum(m => m.Bytes), allowance, CancellationToken.None);
        lock (_lock) foreach (var gone in going) _run!.Matched.Remove(gone);
        return new SafeRemoval(going.Count, going.Sum(m => m.Bytes), matched.Count - going.Count, before);
    }

    /// <summary>Pauses what the locations are doing; what is there stays there, and the next backup carries on from it.</summary>
    public void Pause()
    {
        lock (_lock)
        {
            if (_run is not { Stage: BackupStage.Running } run) return;
            run.Stage = BackupStage.Paused;
            _stop!.Cancel();
        }
    }

    // The computer, the cloud services and those added, as kept, or as they stand before they are.
    private async Task<List<BackupLocation>> ListAsync(CancellationToken ct)
    {
        var kept = await _store.ListAsync(ct);
        var computer = await _ledger.ComputerAsync(ct);
        BackupLocation Blank(LocationKind kind, bool on) => new(LocationKinds.IdOf(kind), kind, LocationKinds.NameOf(kind), on, null, new Dictionary<string, string>(), null, null, null, null);
        var list = new List<BackupLocation>();
        var mine = kept.FirstOrDefault(l => l.Id == ComputerId) ?? Blank(LocationKind.Computer, true);
        // Named as the computer is; on only once paired.
        list.Add(computer is null
            ? mine with { On = false }
            : mine with { Name = computer.Name, LastUsed = mine.LastUsed ?? computer.LastBackup, LastBackup = computer.LastBackup, LastChecked = computer.LastChecked,
                Settings = new Dictionary<string, string> { ["folder"] = computer.Folder } });
        foreach (var kind in LocationKinds.Cloud) list.Add(kept.FirstOrDefault(l => l.Kind == kind) ?? Blank(kind, false));
        list.AddRange(kept.Where(l => LocationKinds.IsServer(l.Kind)).OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase));
        return list;
    }

    private async Task<BackupLocation> FindAsync(string id, CancellationToken ct)
        => (await ListAsync(ct)).FirstOrDefault(l => l.Id == id) ?? throw new KeyNotFoundException("There is no such location. It may have been removed.");

    private async Task<LocationView> ViewAsync(BackupLocation location, CancellationToken ct)
    {
        var connected = location.Kind switch
        {
            LocationKind.Computer => await _ledger.ComputerAsync(ct) is not null,
            _ when LocationKinds.IsCloud(location.Kind) => (await _secrets.ReadAsync(location.Id, ct)).Count > 0,
            _ => true,
        };
        var copies = location.Kind == LocationKind.Computer ? await _computer.HeldAsync(ct) : (await _store.CopiesAsync(location.Id, ct)).Count;
        var available = !LocationKinds.IsCloud(location.Kind) || _accounts.Available(location.Kind);
        return new LocationView(location.Id, location.Kind, location.Name, location.On, connected, available, location.Account, location.Settings, copies,
            location.LastBackup, location.LastChecked, location.Problem);
    }

    // On the free plan, at most one location stays on: the one used last.
    private async Task KeepOneOnAsync(CancellationToken ct)
    {
        var on = (await ListAsync(ct)).Where(l => l.On).OrderByDescending(l => l.LastUsed ?? DateTimeOffset.MinValue).ToList();
        foreach (var off in on.Skip(1)) await _store.SaveAsync(off with { On = false }, ct);
    }

    private async Task LimitAsync(BackupLocation wanted, CancellationToken ct)
    {
        if ((await _plan.CurrentAsync(ct)).Pro) return;
        if ((await ListAsync(ct)).FirstOrDefault(l => l.On && l.Id != wanted.Id) is { } other) throw new LocationLimitException(other.Name, wanted.Id);
    }

    // Signs in to a cloud service in the system's sheet; its tokens are kept as its secrets, and it is kept, off until turned on.
    private async Task<BackupLocation> SignInAsync(BackupLocation location, CancellationToken ct)
    {
        if (!_accounts.Available(location.Kind)) throw new InvalidOperationException($"{location.Name} can be connected once PhotoSense is registered with it. This version cannot sign in to it yet.");
        var begun = _accounts.Begin(location.Kind);
        var back = await _signIn.SignInAsync(begun.Start, begun.CallbackScheme, ct);
        var account = await _accounts.FinishAsync(location.Kind, begun, back, ct);
        await _secrets.SaveAsync(location.Id, account.Secrets, ct);
        var signedIn = location with { Account = account.Name };
        await _store.SaveAsync(signedIn, ct);
        return signedIn;
    }

    private async Task<IRemoteFolder> OpenAsync(BackupLocation location, CancellationToken ct)
        => _remotes.Open(location, await _secrets.ReadAsync(location.Id, ct), (renewed, stop) => _secrets.SaveAsync(location.Id, renewed, stop));

    private static bool Same(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
        => a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var v) && v == p.Value);

    // Connect to server's check: list, write a test file, read it back, compare, delete; each step said if it fails.
    private static async Task CheckAsync(IRemoteFolder folder, string name, CancellationToken ct)
    {
        var test = $".photosense-check-{Guid.NewGuid():N}.txt";
        var bytes = "PhotoSense checks it can write here. This file is deleted at once."u8.ToArray();
        await Step(() => folder.ListAsync(ct), $"PhotoSense could not reach {name}.");
        await Step(() => folder.PutAsync(test, new MemoryStream(bytes), bytes.Length, ct), $"{name} let PhotoSense list its folder but not write to it.");
        var read = await Step(async () =>
        {
            await using var back = await folder.OpenReadAsync(test, ct);
            return await HashingStream.OfAsync(back, ct);
        }, $"{name} took the test file but would not give it back.");
        if (read.Sha256 != Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)))
            throw new IOException($"{name} gave back something other than the test file PhotoSense wrote. Check the folder and try again.");
        await Step(async () => { await folder.DeleteAsync(test, ct); return true; }, $"{name} let PhotoSense write but not delete its test file, {test}.");
    }

    private static async Task<T> Step<T>(Func<Task<T>> step, string failed)
    {
        try
        {
            return await step();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"{failed} {ex.Message}", ex);
        }
    }

    private void Start(Run run, Func<Run, Task> work)
    {
        lock (_lock)
        {
            if (_run is { Stage: BackupStage.Running }) throw new InvalidOperationException("The locations are busy. Try again once they are done.");
            _run = run;
            _stop = new CancellationTokenSource();
            run.Stopping = _stop.Token;
            Running = Task.Run(() => work(run), CancellationToken.None);
        }
    }

    private LocationActivity? Activity()
    {
        lock (_lock)
        {
            if (_run is not { } run) return null;
            return new LocationActivity(run.Task, run.Stage,
                [.. run.Parts.Select(p => new LocationProgress(p.Key, p.Value.Done, p.Value.Total, p.Value.Bytes, p.Value.TotalBytes, p.Value.Problem))], run.Sent);
        }
    }

    // Backs up to each location in the run, or sends what was chosen; a location that fails stops only itself.
    private async Task BackupRunAsync(Run run, IReadOnlyCollection<string>? only, CancellationToken stop)
    {
        try
        {
            var settings = await _ledger.SettingsAsync(stop);
            var items = PhotoItem.Of(await _library.ListAsync(_root, stop))
                .Where(i => only is null ? !i.Main.IsVideo || settings.IncludeVideos : only.Contains(i.Main.Id)).ToList();
            var sums = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in items.SelectMany(i => i.Files)) sums[file.Id] = await ChecksumAsync(file, stop);
            foreach (var id in run.Parts.Keys)
            {
                var location = await FindAsync(id, stop);
                var part = run.Parts[id];
                try
                {
                    var folder = await OpenAsync(location, stop);
                    var copies = await _store.CopiesAsync(id, stop);
                    var queue = items.SelectMany(i => i.Files).Where(f => !copies.ContainsKey(sums[f.Id])).ToList();
                    lock (_lock) { part.Total = queue.Count; part.TotalBytes = queue.Sum(f => f.SizeBytes); }
                    var taken = (await folder.ListAsync(stop)).Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var held = new HashSet<string>(copies.Keys, StringComparer.Ordinal);
                    foreach (var file in queue)
                    {
                        // A file in two places of the library goes once.
                        if (held.Contains(sums[file.Id])) { lock (_lock) part.Done++; continue; }
                        var name = taken.Contains(file.Name) ? NameRules.NextFree(file.Name, taken.Contains) : file.Name;
                        taken.Add(name);
                        if (await PutAsync(folder, file, name, sums[file.Id], part, stop) is { } copy)
                        {
                            await _store.KeepCopyAsync(id, sums[file.Id], copy, stop);
                            held.Add(sums[file.Id]);
                        }
                        lock (_lock) part.Done++;
                    }
                    if (only is not null)
                    {
                        var matched = items.Where(i => i.Files.All(f => held.Contains(sums[f.Id]))).ToList();
                        lock (_lock)
                        {
                            run.Matched = [.. matched.Select(i => ((IReadOnlyList<string>)[.. i.Files.Select(f => f.Path)], i.Bytes))];
                            run.Sent = new SendResult(id, items.Count, matched.Count, matched.Sum(i => i.Bytes));
                        }
                    }
                    await _store.SaveAsync(location with { LastBackup = _clock.GetUtcNow(), LastUsed = _clock.GetUtcNow(), Problem = part.Problem }, stop);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    lock (_lock) part.Problem = ex.Message;
                    await _store.SaveAsync(location with { Problem = ex.Message }, stop);
                }
            }
            lock (_lock) run.Stage = BackupStage.Ready;
        }
        catch (OperationCanceledException)
        {
            // Paused: Pause said so.
        }
    }

    // Puts a file there and checks what arrived: by the location's own checksum, or read back. Null when it did not match.
    private async Task<LocationCopy?> PutAsync(IRemoteFolder folder, LibraryFile file, string name, string sha256, Part part, CancellationToken stop)
    {
        RemoteChecksum? remote;
        bool matches;
        await using (var hashing = new HashingStream(await _files.OpenReadAsync(file.Path, stop)))
        {
            remote = await folder.PutAsync(name, hashing, file.SizeBytes, stop);
            lock (_lock) part.Bytes += file.SizeBytes;
            matches = hashing.Sha256 == sha256 && (remote is null ? await ReadsBackAsync(folder, name, sha256, stop) : hashing.Matches(remote));
        }
        if (matches) return new LocationCopy(name, remote);
        lock (_lock) part.Problem = $"The copy of {file.Name} there did not match this device's. It is sent again at the next backup.";
        return null;
    }

    private static async Task<bool> ReadsBackAsync(IRemoteFolder folder, string name, string sha256, CancellationToken ct)
    {
        await using var back = await folder.OpenReadAsync(name, ct);
        using var read = await HashingStream.OfAsync(back, ct);
        return read.Sha256 == sha256;
    }

    // Checks every copy each location in the run holds.
    private async Task CheckRunAsync(Run run)
    {
        var stop = run.Stopping;
        try
        {
            foreach (var id in run.Parts.Keys)
            {
                var location = await FindAsync(id, stop);
                var part = run.Parts[id];
                try
                {
                    var folder = await OpenAsync(location, stop);
                    var copies = await _store.CopiesAsync(id, stop);
                    lock (_lock) part.Total = copies.Count;
                    foreach (var (sha, copy) in copies)
                    {
                        if (!await StillThereAsync(folder, sha, copy, stop)) await _store.ForgetCopyAsync(id, sha, stop);
                        lock (_lock) part.Done++;
                    }
                    await _store.SaveAsync(location with { LastChecked = _clock.GetUtcNow(), Problem = null }, stop);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    lock (_lock) part.Problem = ex.Message;
                    await _store.SaveAsync(location with { Problem = ex.Message }, stop);
                }
            }
            lock (_lock) run.Stage = BackupStage.Ready;
        }
        catch (OperationCanceledException)
        {
            // Paused: Pause said so.
        }
    }

    private static async Task<bool> StillThereAsync(IRemoteFolder folder, string sha256, LocationCopy copy, CancellationToken ct)
    {
        try
        {
            return copy.Checksum is not null && await folder.ChecksumAsync(copy.Name, ct) is { } now
                ? now == copy.Checksum
                : await ReadsBackAsync(folder, copy.Name, sha256, ct);
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    // A file's SHA-256: the scan's, or the one worked out before for its size and time, or worked out now and kept.
    private async Task<string> ChecksumAsync(LibraryFile file, CancellationToken ct)
    {
        if (file.ContentHash is { } scanned) return scanned.ToLowerInvariant();
        if (await _ledger.ChecksumAsync(file.Path, file.SizeBytes, file.ModifiedUtc, ct) is { } known) return known;
        await using var read = await _files.OpenReadAsync(file.Path, ct);
        using var hashing = await HashingStream.OfAsync(read, ct);
        await _ledger.KeepChecksumAsync(file.Path, file.SizeBytes, file.ModifiedUtc, hashing.Sha256, ct);
        return hashing.Sha256;
    }
}
