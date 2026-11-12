using System.Security.Cryptography;
using System.Text.Json.Nodes;
using PhotoSense.Application.Changes;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Application.Transfer;

/// <summary>Where receiving photos from a phone has got to.</summary>
public enum ReceiveStage
{
    /// <summary>Not receiving.</summary>
    Idle,
    /// <summary>Connecting to the phone.</summary>
    Connecting,
    /// <summary>Waiting for the person to say whether the numbers on the two match.</summary>
    Comparing,
    /// <summary>Files are arriving.</summary>
    Receiving,
    /// <summary>Every file offered has arrived.</summary>
    Done,
    /// <summary>It stopped before every file had arrived; <see cref="ReceiveState.Problem"/> says why.</summary>
    Failed
}

/// <summary>How receiving from a phone stands.</summary>
/// <param name="Stage">Where it has got to.</param>
/// <param name="Code">The number to compare with the phone's, while it is being compared.</param>
/// <param name="Phone">The phone's name, once known.</param>
/// <param name="Done">Files that have arrived, those already there among them.</param>
/// <param name="Total">Files the phone offered.</param>
/// <param name="Bytes">Bytes that have arrived.</param>
/// <param name="TotalBytes">Bytes the phone offered.</param>
/// <param name="AlreadyThere">Files of which the very same file was in the folder already, so that nothing was written.</param>
/// <param name="Folder">The folder the files go to.</param>
/// <param name="Problem">Why it stopped, when it failed.</param>
public sealed record ReceiveState(ReceiveStage Stage, string? Code, string? Phone, int Done, int Total, long Bytes, long TotalBytes, int AlreadyThere, string? Folder, string? Problem);

/// <summary>
/// A computer receiving the photos a phone offers, into a folder the person chose. Each file is written under a name
/// of PhotoSense's own until it is all there and its check is the phone's, and only then given its own name, numbered
/// when the name is taken: nothing is ever replaced, and a file that arrived damaged is not kept. A file the very same
/// as one already there is not written again, and counts as safely there.
/// </summary>
public sealed class PhoneReceiving
{
    /// <summary>What a file still arriving is called, after a dot that hides it and its own name.</summary>
    public const string Arriving = ".photosense-part";

    private readonly IPhoneFinder _finder;
    private readonly IPairingStore _pairs;
    private readonly IPhotoFileSystem _files;
    private readonly FileChanges _changes;
    private readonly string _name;
    private readonly TimeProvider _clock;
    private readonly object _lock = new();
    private ReceiveState _state = new(ReceiveStage.Idle, null, null, 0, 0, 0, 0, 0, null, null);
    private IReadOnlyList<FoundPhone> _found = [];
    private CancellationTokenSource? _stop;
    private TaskCompletionSource<bool>? _answer;

    /// <summary>Receives from the phones <paramref name="finder"/> finds, into folders of <paramref name="files"/>.</summary>
    /// <param name="finder">Finds the phones offering photos.</param>
    /// <param name="pairs">The devices this computer has been paired with.</param>
    /// <param name="files">Where the photos are kept.</param>
    /// <param name="changes">Gives each arriving file a name nothing has yet.</param>
    /// <param name="name">What this computer is called, as the phone shows it.</param>
    /// <param name="clock">Times how long the phone has been silent (<see cref="TimedConnection"/>).</param>
    public PhoneReceiving(IPhoneFinder finder, IPairingStore pairs, IPhotoFileSystem files, FileChanges changes, string name, TimeProvider clock)
    {
        _finder = finder;
        _pairs = pairs;
        _files = files;
        _changes = changes;
        _name = name;
        _clock = clock;
    }

    /// <summary>How receiving stands now.</summary>
    public ReceiveState State
    {
        get { lock (_lock) return _state; }
    }

    /// <summary>The run going on now, for whoever waits for it to end.</summary>
    public Task Running { get; private set; } = Task.CompletedTask;

    /// <summary>The phones offering photos now.</summary>
    public async Task<IReadOnlyList<FoundPhone>> FindAsync(CancellationToken ct)
    {
        var found = await _finder.FindAsync(ct);
        lock (_lock) _found = found;
        return found;
    }

    /// <summary>Starts receiving from a phone found, into a folder; it goes on by itself.</summary>
    /// <exception cref="InvalidOperationException">Photos are already being received.</exception>
    /// <exception cref="ArgumentException">The phone is no longer among those found.</exception>
    /// <exception cref="DirectoryNotFoundException">The folder is not there.</exception>
    public void Start(string phoneKey, string folder)
    {
        lock (_lock)
        {
            if (_state.Stage is ReceiveStage.Connecting or ReceiveStage.Comparing or ReceiveStage.Receiving) throw new InvalidOperationException("Photos are already being received.");
            var phone = _found.FirstOrDefault(p => p.Key == phoneKey) ?? throw new ArgumentException("That phone is no longer offering photos. Look for it again.");
            if (!_files.FolderExists(folder)) throw new DirectoryNotFoundException($"Folder not found: {folder}");
            _stop = new CancellationTokenSource();
            _state = new ReceiveState(ReceiveStage.Connecting, null, phone.Name, 0, 0, 0, 0, 0, folder, null);
            var stop = _stop.Token;
            Running = Task.Run(() => RunAsync(phone, folder, stop), CancellationToken.None);
        }
    }

    /// <summary>The person's answer to whether the numbers on the computer and the phone are the same.</summary>
    public void Confirm(bool same) => _answer?.TrySetResult(same);

    /// <summary>Stops receiving. What has arrived stays; a file still arriving does not.</summary>
    public void Stop() => _stop?.Cancel();

    private void Set(Func<ReceiveState, ReceiveState> change)
    {
        lock (_lock) _state = change(_state);
    }

    private async Task<bool> AskAsync(string code, Device phone, CancellationToken ct)
    {
        var answer = _answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Set(s => s with { Stage = ReceiveStage.Comparing, Code = code, Phone = phone.Name });
        using var registration = ct.Register(() => answer.TrySetResult(false));
        return await answer.Task;
    }

    private async Task RunAsync(FoundPhone phone, string folder, CancellationToken ct)
    {
        string? arriving = null;
        // What is said unless it ends as it should, or is stopped.
        var problem = "Receiving stopped. Try again.";
        try
        {
            await using var connection = await TimedConnection.WithinAsync(phone.Connect, _clock, ct);
            var stream = new TimedConnection(connection, _clock);
            var linked = await Pairing.ComputerAsync(stream, new Device(await _pairs.SelfIdAsync(ct), _name), _pairs, AskAsync, ct);
            using var channel = linked.Channel;
            await channel.SendAsync(new JsonObject { ["t"] = "offer?" }, ct);
            var offer = await channel.ReceiveAsync(ct, "offer");
            var files = (offer["files"] as JsonArray ?? []).OfType<JsonObject>().Select(f => (Id: Wire.Text(f, "id"), Name: Plain(Wire.Text(f, "name")), Size: Size(f))).ToList();
            Set(s => s with { Stage = ReceiveStage.Receiving, Code = null, Phone = linked.Other.Name, Total = files.Count, TotalBytes = files.Sum(f => f.Size) });

            var taken = new HashSet<string>(PhotoPath.Comparer);
            foreach (var (id, name, _) in files)
            {
                arriving = Path.Combine(folder, $".{name}.{Guid.NewGuid():N}{Arriving}");
                var check = await ReceiveFileAsync(channel, id, arriving, ct);
                var wanted = Path.Combine(folder, name);
                // Reading through a large file already there takes a while, which the phone is told of.
                var already = _files.Describe(wanted) is { } there && there.Length == _files.Describe(arriving)!.Length
                    && await channel.MeanwhileAsync(stop => HashOfAsync(wanted, stop), _clock, ct) == check;
                if (already) _files.DeleteCopy(arriving);
                else _files.Move(arriving, _changes.FreePlace(wanted, taken));
                arriving = null;
                await channel.SendAsync(new JsonObject { ["t"] = "got", ["id"] = id, ["sha256"] = check, ["where"] = folder }, ct);
                Set(s => s with { Done = s.Done + 1, AlreadyThere = s.AlreadyThere + (already ? 1 : 0) });
            }
            await channel.SendAsync(new JsonObject { ["t"] = "done" }, ct);
            await channel.ReceiveAsync(ct, "bye");
            Set(s => s with { Stage = ReceiveStage.Done });
            problem = null;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            problem = ex is OperationCanceledException ? "Receiving was stopped. What had arrived is kept." : ex is TransferException ? ex.Message : $"Receiving stopped: {ex.Message}";
        }
        finally
        {
            // However it ended, receiving is no longer said to be going on, and a file cut short is PhotoSense's own
            // writing and does not stay.
            if (problem is not null) Set(s => s with { Stage = ReceiveStage.Failed, Code = null, Problem = problem });
            if (arriving is not null && _files.FileExists(arriving)) _files.DeleteCopy(arriving);
        }
    }

    // A file, written under a name of its own until it is all there; its check, which has to be the phone's.
    private async Task<string> ReceiveFileAsync(SecureChannel channel, string id, string arriving, CancellationToken ct)
    {
        await channel.SendAsync(new JsonObject { ["t"] = "get", ["id"] = id }, ct);
        var header = await channel.ReceiveAsync(ct, "file");
        var size = Size(header);
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
                Set(s => s with { Bytes = s.Bytes + piece.Length });
            }
        }
        var check = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (Wire.Text(await channel.ReceiveAsync(ct, "end"), "sha256") != check)
            throw new TransferException("A file arrived damaged, so it was not kept. Try again.");
        return check;
    }

    private async Task<string> HashOfAsync(string path, CancellationToken ct)
    {
        await using var read = await _files.OpenReadAsync(path, ct);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(read, ct));
    }

    private static long Size(JsonObject message)
        => message["size"] is JsonValue value && value.TryGetValue(out long size) && size >= 0 ? size : throw new TransferException("The phone sent something PhotoSense does not understand.");

    /// <summary>A name the phone gave, as a file of this folder: never a path elsewhere, nor one the computer cannot hold.</summary>
    public static string Plain(string name)
    {
        var last = name.Replace('\\', '/').Split('/').Last().Trim();
        var plain = string.Concat(last.Select(c => char.IsControl(c) || NotInNames.Contains(c) ? '_' : c)).Trim('.', ' ');
        return plain.Length == 0 ? "photo" : plain;
    }

    // What Windows does not allow in a file's name, kept out on every computer so that the files can go anywhere after.
    private const string NotInNames = "<>:\"|?*";
}
