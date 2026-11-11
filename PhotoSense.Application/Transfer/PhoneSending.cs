using System.Security.Cryptography;
using System.Text.Json.Nodes;
using PhotoSense.Application.Organizing;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Application.Transfer;

/// <summary>Where sending photos from this phone has got to.</summary>
public enum SendStage
{
    /// <summary>Not offering photos.</summary>
    Idle,
    /// <summary>Offering photos, waiting for a computer to connect.</summary>
    Waiting,
    /// <summary>Waiting for the person to say whether the numbers on the two match.</summary>
    Comparing,
    /// <summary>The computer is taking the files.</summary>
    Sending,
    /// <summary>The computer has taken what it wanted.</summary>
    Sent,
    /// <summary>It stopped before the computer was through; <see cref="SendState.Problem"/> says why.</summary>
    Failed
}

/// <summary>How sending from this phone stands.</summary>
/// <param name="Stage">Where it has got to.</param>
/// <param name="Code">The number to compare with the computer's, while it is being compared.</param>
/// <param name="Computer">The computer's name, once known.</param>
/// <param name="Done">Files the computer has said are safely there.</param>
/// <param name="Total">Files offered.</param>
/// <param name="Bytes">Bytes sent.</param>
/// <param name="TotalBytes">Bytes offered.</param>
/// <param name="Deletable">The names of the pictures and videos every file of which is safely on the computer, and that can be deleted from the phone.</param>
/// <param name="Where">The folder on the computer the files went to, when they all went to one.</param>
/// <param name="Problem">Why it stopped, when it failed.</param>
/// <param name="Current">The name of the file going to the computer now, from the moment the phone starts reading it until its last byte is sent.</param>
public sealed record SendState(SendStage Stage, string? Code, string? Computer, int Done, int Total, long Bytes, long TotalBytes, IReadOnlyList<string> Deletable, string? Where, string? Problem, string? Current);

/// <summary>
/// A phone offering photos to a computer, while the person has it offering them: it waits for a computer to connect,
/// pairs with it the first time, and sends the files the computer asks for, each with a check of its bytes. A file
/// counts as safely on the computer only once the computer has said so and its check is the one the phone sent.
/// Deleting from the phone afterwards takes only pictures every file of which is there, and sends them to the
/// phone's own bin.
/// </summary>
public sealed class PhoneSending
{
    private readonly ITransferListener _listener;
    private readonly ITransferAdvertiser _advertiser;
    private readonly IPairingStore _pairs;
    private readonly IPhotoFileSystem _files;
    private readonly ICompanionFileFinder _companions;
    private readonly OrganizeLibrary _library;
    private readonly string _name;
    private readonly TimeProvider _clock;
    private readonly object _lock = new();
    private SendState _state = new(SendStage.Idle, null, null, 0, 0, 0, 0, [], null, null, null);
    private List<Offered> _offer = [];
    private readonly Dictionary<string, string> _sent = [];
    private readonly Dictionary<string, string> _safe = [];
    private TaskCompletionSource<bool>? _answer;
    private bool _serving, _offering;

    // A file offered, and the picture or video it goes with: a Live Photo's video goes with its picture.
    private sealed record Offered(string Id, string Path, long Size, DateTime ModifiedUtc, int Unit);

    /// <summary>Offers the files Organize knows, through the listener and advertiser given.</summary>
    /// <param name="listener">Waits for a computer to connect.</param>
    /// <param name="advertiser">Tells the network that the phone is offering photos.</param>
    /// <param name="pairs">The devices this phone has been paired with.</param>
    /// <param name="files">Where the photos are kept.</param>
    /// <param name="companions">Finds the Live Photo videos and edit files that go with a picture.</param>
    /// <param name="library">The files Organize knows, by id.</param>
    /// <param name="name">What this phone is called, as the computer shows it.</param>
    /// <param name="clock">Times how long the computer has been silent (<see cref="TimedConnection"/>).</param>
    public PhoneSending(ITransferListener listener, ITransferAdvertiser advertiser, IPairingStore pairs, IPhotoFileSystem files, ICompanionFileFinder companions, OrganizeLibrary library, string name, TimeProvider clock)
    {
        _listener = listener;
        _advertiser = advertiser;
        _pairs = pairs;
        _files = files;
        _companions = companions;
        _library = library;
        _name = name;
        _clock = clock;
    }

    /// <summary>What this phone is called, as the computer shows it.</summary>
    public string Name => _name;

    /// <summary>How sending stands now, with what can be deleted from the phone.</summary>
    public SendState State
    {
        // Each by its picture, or the video that is all there is of it.
        get { lock (_lock) return _state with { Deletable = [.. DeletableUnits().Select(unit => Path.GetFileName(_offer.First(f => f.Unit == unit).Path))] }; }
    }

    /// <summary>Offers the files Organize knows by these ids, with their Live Photo videos and edit files, until stopped.</summary>
    /// <param name="fileIds">The files, by the ids Organize gave them.</param>
    /// <param name="ct">Stops the gathering of the files; once offered, they are offered until stopped.</param>
    /// <exception cref="ArgumentException">None of them is there to send.</exception>
    public async Task<SendState> StartAsync(IReadOnlyList<string> fileIds, CancellationToken ct)
    {
        var offer = new List<Offered>();
        var siblings = new SiblingIndex(_files);
        var listed = new HashSet<string>(PhotoPath.Comparer);
        foreach (var id in fileIds)
        {
            if (await _library.FindAsync(id, ct) is not { } file || !listed.Add(file.Path)) continue;
            var unit = offer.Count == 0 ? 0 : offer[^1].Unit + 1;
            string[] together = [file.Path, .. await _companions.FindAmongAsync(file.Path, siblings.SiblingsOf(file.Path), ct)];
            foreach (var path in together)
                if ((path == file.Path || listed.Add(path)) && _files.Describe(path) is { } stored)
                    offer.Add(new Offered($"f{offer.Count + 1}", path, stored.Length, stored.ModifiedUtc, unit));
        }
        if (offer.Count == 0) throw new ArgumentException("None of those files is there to send any more.");

        Stop();
        lock (_lock)
        {
            _offer = offer;
            _sent.Clear();
            _safe.Clear();
            _state = new SendState(SendStage.Waiting, null, null, 0, offer.Count, 0, offer.Sum(f => f.Size), [], null, null, null);
            _offering = true;
        }
        var port = _listener.Start(ServeAsync);
        _advertiser.Start(await _pairs.SelfIdAsync(ct), _name, port);
        return State;
    }

    /// <summary>The person's answer to whether the numbers on the phone and the computer are the same.</summary>
    public void Confirm(bool same) => _answer?.TrySetResult(same);

    /// <summary>Stops offering. What was sent stays known, to be deleted from the phone.</summary>
    public void Stop()
    {
        _listener.Stop();
        _advertiser.Stop();
        Confirm(false);
        lock (_lock)
        {
            _offering = false;
            if (_state.Stage is not (SendStage.Idle or SendStage.Sent)) _state = _state with { Stage = SendStage.Idle, Code = null, Current = null };
        }
    }

    /// <summary>
    /// Deletes from the phone the pictures and videos every file of which is safely on the computer. The phone
    /// asks the person first, and keeps them in its own bin for a while.
    /// </summary>
    /// <returns>How many pictures and videos went.</returns>
    /// <exception cref="IOException">None went: the person said no, or the phone could not delete them.</exception>
    public int DeleteSent()
    {
        if (_files is not IPhotoChangesTogether phone) throw new InvalidOperationException("Only a phone deletes what it has sent.");
        List<Offered> going;
        lock (_lock) going = [.. DeletableUnits().SelectMany(unit => _offer.Where(f => f.Unit == unit))];
        if (going.Count == 0) return 0;
        phone.EraseAll([.. going.Select(f => f.Path)]);
        lock (_lock)
        {
            foreach (var file in going) _safe.Remove(file.Id);
            return going.Select(f => f.Unit).Distinct().Count();
        }
    }

    private List<int> DeletableUnits()
        => [.. _offer.GroupBy(f => f.Unit).Where(unit => unit.All(f => _safe.ContainsKey(f.Id))).Select(unit => unit.Key)];

    private void Set(Func<SendState, SendState> change)
    {
        lock (_lock) _state = change(_state);
    }

    private async Task<bool> AskAsync(string code, Device computer, CancellationToken ct)
    {
        var answer = _answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Set(s => s with { Stage = SendStage.Comparing, Code = code, Computer = computer.Name });
        using var registration = ct.Register(() => answer.TrySetResult(false));
        return await answer.Task;
    }

    // One computer at a time; another is told so.
    private async Task ServeAsync(Stream connection, CancellationToken ct)
    {
        var stream = new TimedConnection(connection, _clock);
        bool busy;
        lock (_lock)
        {
            busy = _serving;
            _serving = true;
        }
        if (busy)
        {
            await Wire.SendAsync(stream, new JsonObject { ["t"] = "busy" }, ct);
            return;
        }
        // What is said unless it ends as it should.
        var problem = "Sending stopped. Try again.";
        try
        {
            var linked = await Pairing.PhoneAsync(stream, new Device(await _pairs.SelfIdAsync(ct), _name), _pairs, AskAsync, ct);
            using var channel = linked.Channel;
            Set(s => s with { Stage = SendStage.Sending, Code = null, Computer = linked.Other.Name, Problem = null });
            while (await AnswerAsync(channel, ct)) { }
            Set(s => s with { Stage = SendStage.Sent });
            problem = null;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or UnauthorizedAccessException)
        {
            problem = ex is TransferException ? ex.Message : $"Sending stopped: {ex.Message}";
        }
        finally
        {
            lock (_lock)
            {
                _serving = false;
                // However it ended, sending is no longer said to be going on; a connection ended by stopping is no failure.
                if (problem is not null && _offering) _state = _state with { Stage = SendStage.Failed, Code = null, Problem = problem, Current = null };
            }
        }
    }

    // Answers what the computer asks next; false once it is through.
    private async Task<bool> AnswerAsync(SecureChannel channel, CancellationToken ct)
    {
        var asked = await channel.ReceiveAsync(ct, "offer?", "get", "got", "done");
        switch (Wire.Text(asked, "t"))
        {
            case "offer?":
                List<Offered> offer;
                lock (_lock) offer = _offer;
                await channel.SendAsync(new JsonObject
                {
                    ["t"] = "offer", ["name"] = _name,
                    ["files"] = new JsonArray([.. offer.Select(f => (JsonNode)new JsonObject { ["id"] = f.Id, ["name"] = Path.GetFileName(f.Path), ["size"] = f.Size, ["modified"] = f.ModifiedUtc.ToString("O") })]),
                }, ct);
                return true;
            case "get":
                await SendFileAsync(channel, Find(Wire.Text(asked, "id")), ct);
                return true;
            case "got":
                var id = Wire.Text(asked, "id");
                lock (_lock)
                {
                    // Safe only with the very bytes that were sent.
                    if (_sent.GetValueOrDefault(id) != Wire.Text(asked, "sha256")) throw new TransferException("The computer's check of a file did not match what was sent. Send it again.");
                    _safe[id] = Wire.Text(asked, "where");
                    _state = _state with { Done = _safe.Count, Where = _safe.Values.Distinct(PhotoPath.Comparer).Count() == 1 ? _safe[id] : null };
                }
                return true;
            default:
                await channel.SendAsync(new JsonObject { ["t"] = "bye" }, ct);
                return false;
        }
    }

    private Offered Find(string id)
    {
        lock (_lock) return _offer.FirstOrDefault(f => f.Id == id) ?? throw new TransferException("The computer asked for a file that was not offered.");
    }

    private async Task SendFileAsync(SecureChannel channel, Offered file, CancellationToken ct)
    {
        Set(s => s with { Current = Path.GetFileName(file.Path) });
        // A picture kept only in the cloud is fetched first, which can take a while; the computer is told of it.
        await using var read = await channel.MeanwhileAsync(stop => _files.OpenReadAsync(file.Path, stop), _clock, ct);
        await channel.SendAsync(new JsonObject { ["t"] = "file", ["id"] = file.Id, ["size"] = read.Length }, ct);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var piece = new byte[SecureChannel.PieceSize];
        for (int n; (n = await read.ReadAsync(piece, ct)) > 0;)
        {
            hash.AppendData(piece, 0, n);
            await channel.SendDataAsync(piece.AsMemory(0, n), ct);
            Set(s => s with { Bytes = s.Bytes + n });
        }
        var check = Convert.ToHexStringLower(hash.GetHashAndReset());
        lock (_lock)
        {
            _sent[file.Id] = check;
            _state = _state with { Current = null };
        }
        await channel.SendAsync(new JsonObject { ["t"] = "end", ["id"] = file.Id, ["sha256"] = check }, ct);
    }
}
