using System.Security.Cryptography;
using System.Text.Json.Nodes;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;

namespace PhotoSense.Application.Transfer;

/// <summary>How pairing a phone stands on the computer.</summary>
public enum ComputerPairingStage
{
    /// <summary>Not pairing.</summary>
    Idle,
    /// <summary>Showing the QR code and the code to type, waiting for a phone.</summary>
    Waiting,
    /// <summary>A phone typed the code: the person compares the number on both.</summary>
    Comparing,
    /// <summary>A phone was paired.</summary>
    Paired,
    /// <summary>Pairing stopped; the problem says why.</summary>
    Failed,
}

/// <summary>Where pairing a phone has got to on the computer.</summary>
/// <param name="Stage">How it stands.</param>
/// <param name="Link">The QR code's text while waiting.</param>
/// <param name="Code">The six digits to type on the phone while waiting.</param>
/// <param name="Expires">When the code and the QR code change.</param>
/// <param name="Check">The number to compare with the phone's while comparing.</param>
/// <param name="Phone">The phone being paired, or just paired.</param>
/// <param name="Problem">Why pairing stopped.</param>
public sealed record ComputerPairingState(ComputerPairingStage Stage, string? Link, string? Code, DateTimeOffset? Expires, string? Check, string? Phone, string? Problem);

/// <summary>How a computer waits for phones and tells the network so: its own port, and multicast DNS.</summary>
/// <param name="Listener">Waits for phones to connect.</param>
/// <param name="Advertiser">Tells the network that this computer is waiting for phones.</param>
public sealed record PhoneDoor(ITransferListener Listener, ITransferAdvertiser Advertiser);

/// <summary>
/// The computer's door for phones: it waits for them on its port and says so on the network while the program runs.
/// A phone it knows is handed on to its backup (<see cref="ComputerBackup"/>); one it does not know is turned away,
/// unless Pair a phone is open. While it is, the computer shows a QR code and a code to type, both changing every five
/// minutes. A phone that scans the QR code is paired at once, as only it has the code's secret; one that typed the code
/// is paired once the person has said the numbers on both match. A phone turned away, or one that fails before the
/// numbers are compared, leaves Pair a phone waiting.
/// </summary>
public sealed class ComputerPairing
{
    /// <summary>How long a code and a QR code stand before they change.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly ITransferListener _listener;
    private readonly ITransferAdvertiser _advertiser;
    private readonly IPairingStore _pairs;
    private readonly string _name;
    private readonly Func<IReadOnlyList<string>> _hosts;
    private readonly ComputerBackup _backup;
    private readonly TimeProvider _clock;
    private readonly object _lock = new();
    private ComputerPairingState _state = new(ComputerPairingStage.Idle, null, null, null, null, null, null);
    private PairingInvitation? _invitation;
    private string? _id;
    private int _port;
    private bool _listening;
    private TaskCompletionSource<bool>? _answer;

    /// <summary>Pairing through the listener and advertiser given.</summary>
    /// <param name="listener">Waits for phones to connect.</param>
    /// <param name="advertiser">Tells the network that this computer is waiting for phones.</param>
    /// <param name="pairs">The devices this computer has been paired with; a new pairing is kept there.</param>
    /// <param name="name">What this computer is called, as the phone shows it.</param>
    /// <param name="hosts">This computer's addresses on its networks, for the QR code.</param>
    /// <param name="backup">Keeps the backups of the phones paired with this computer.</param>
    /// <param name="clock">When the codes change, and how long a phone may stay silent.</param>
    public ComputerPairing(ITransferListener listener, ITransferAdvertiser advertiser, IPairingStore pairs, string name, Func<IReadOnlyList<string>> hosts, ComputerBackup backup, TimeProvider clock)
    {
        _listener = listener;
        _advertiser = advertiser;
        _pairs = pairs;
        _name = name;
        _hosts = hosts;
        _backup = backup;
        _clock = clock;
    }

    /// <summary>How pairing stands now; a code past its time is changed first.</summary>
    public ComputerPairingState State
    {
        get
        {
            lock (_lock)
            {
                if (_state is { Stage: ComputerPairingStage.Waiting, Expires: { } at } && at <= _clock.GetUtcNow()) Invite();
                return _state;
            }
        }
    }

    /// <summary>Starts waiting for phones and saying so on the network, as the program starts; nothing changes when it is already waiting.</summary>
    public async Task ListenAsync(CancellationToken ct)
    {
        var id = await _pairs.SelfIdAsync(ct);
        lock (_lock)
        {
            _id = id;
            if (_listening) return;
            _port = _listener.Start(ServeAsync);
            _advertiser.Start(id, _name, _port);
            _listening = true;
        }
    }

    /// <summary>Stops waiting for phones and saying so, as the program stops.</summary>
    public void StopListening()
    {
        Close();
        lock (_lock)
        {
            if (!_listening) return;
            _listening = false;
        }
        // Stopped outside the lock: stopping waits for the phones being served, which take the lock as they end.
        _advertiser.Stop();
        _listener.Stop();
    }

    /// <summary>Opens Pair a phone: waits for phones if it was not, with a new code and QR code.</summary>
    public async Task<ComputerPairingState> OpenAsync(CancellationToken ct)
    {
        await ListenAsync(ct);
        lock (_lock)
        {
            Invite();
            return _state;
        }
    }

    /// <summary>Closes Pair a phone: phones it does not know are turned away again; a comparison still asked is answered no.</summary>
    public void Close()
    {
        lock (_lock)
        {
            _answer?.TrySetResult(false);
            _invitation = null;
            _state = new ComputerPairingState(ComputerPairingStage.Idle, null, null, null, null, null, null);
        }
    }

    /// <summary>The person's answer to whether the number on the phone is the same.</summary>
    public void Confirm(bool same)
    {
        lock (_lock) _answer?.TrySetResult(same);
    }

    // A new code and QR code, standing for five minutes.
    private void Invite()
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("000000", System.Globalization.CultureInfo.InvariantCulture);
        _invitation = new PairingInvitation(code, RandomNumberGenerator.GetBytes(32));
        var link = new PairingLink(_id!, _name, _hosts(), _port, _invitation.Secret).Write();
        _state = new ComputerPairingState(ComputerPairingStage.Waiting, link, code, _clock.GetUtcNow() + Lifetime, null, null, null);
    }

    private async Task ServeAsync(Stream connection, CancellationToken ct)
    {
        PairingInvitation? invitation;
        lock (_lock) invitation = _invitation;
        var stream = new TimedConnection(connection, _clock);
        var compared = false;
        Linked linked;
        try
        {
            linked = await Pairing.ComputerAsync(stream, new Device(await _pairs.SelfIdAsync(ct), _name), _pairs, async (check, phone, token) =>
            {
                compared = true;
                return await AskAsync(check, phone, token);
            }, invitation, strangers: false, ct);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            // A phone turned away, or one that failed before anyone compared numbers, leaves Pair a phone waiting for the next.
            if (!compared) return;
            lock (_lock) _state = _state with { Stage = ComputerPairingStage.Failed, Check = null, Problem = ex is TransferException ? ex.Message : "Pairing stopped. Try again." };
            return;
        }
        using var channel = linked.Channel;
        try
        {
            if (linked.Paired)
            {
                var folder = await _backup.PairedAsync(linked.Other, ct);
                lock (_lock) _state = new ComputerPairingState(ComputerPairingStage.Paired, null, null, null, null, linked.Other.Name, null);
                // The phone shows where its backups go; the pairing is done whether it hears or not.
                await channel.SendAsync(new JsonObject { ["t"] = "paired", ["folder"] = folder }, ct);
            }
            await _backup.ServeAsync(channel, linked.Other, ct);
        }
        // A phone gone part way through a backup backs up again later; what arrived whole is kept.
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            await _backup.StoppedAsync(linked.Other, ex);
        }
    }

    // Shows the number, and waits for the person to say whether the phone shows the same.
    private async Task<bool> AskAsync(string check, Device phone, CancellationToken ct)
    {
        TaskCompletionSource<bool> answer;
        lock (_lock)
        {
            answer = _answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _state = _state with { Stage = ComputerPairingStage.Comparing, Check = check, Phone = phone.Name };
        }
        return await answer.Task.WaitAsync(ct);
    }
}
