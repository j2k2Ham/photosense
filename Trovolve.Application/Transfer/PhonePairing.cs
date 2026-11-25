using System.Text.Json.Nodes;
using Trovolve.Application.Plans;
using Trovolve.Domain.Repositories;
using Trovolve.Domain.Services;

namespace Trovolve.Application.Transfer;

/// <summary>How pairing with a computer stands on the phone.</summary>
public enum PhonePairingStage
{
    /// <summary>Not pairing.</summary>
    Idle,
    /// <summary>The camera is looking for the computer's QR code.</summary>
    Scanning,
    /// <summary>Looking on the Wi-Fi for the computer showing the code typed.</summary>
    Finding,
    /// <summary>Connecting to the computer.</summary>
    Connecting,
    /// <summary>The person compares the number on both.</summary>
    Comparing,
    /// <summary>Paired with the computer.</summary>
    Paired,
    /// <summary>Pairing stopped; the problem says why.</summary>
    Failed,
}

/// <summary>Where pairing with a computer has got to on the phone.</summary>
/// <param name="Stage">How it stands.</param>
/// <param name="Computer">The computer being paired with, or just paired with.</param>
/// <param name="Check">The number to compare with the computer's while comparing.</param>
/// <param name="Folder">Where the computer puts what this phone backs up, once paired.</param>
/// <param name="Problem">Why pairing stopped.</param>
public sealed record PhonePairingState(PhonePairingStage Stage, string? Computer, string? Check, string? Folder, string? Problem);

/// <summary>
/// Pairing with a computer, on the phone. Scanning the computer's QR code connects to it and pairs at once, as the code
/// carries a secret only that computer has. Typing its code instead finds it on the Wi-Fi, the one computer showing that
/// code, and the person then says whether the numbers on both match.
/// </summary>
public sealed class PhonePairing
{
    private readonly ICodeScanner _scanner;
    private readonly IComputerFinder _finder;
    private readonly IComputerConnector _connector;
    private readonly IPairingStore _pairs;
    private readonly IBackupLedger _ledger;
    private readonly string _name;
    private readonly TimeProvider _clock;
    private readonly TermsAcceptance _terms;
    private readonly object _lock = new();
    private PhonePairingState _state = new(PhonePairingStage.Idle, null, null, null, null);
    private CancellationTokenSource? _stop;
    private TaskCompletionSource<bool>? _answer;

    /// <summary>Pairing through the phone's scanner, finder and connections given.</summary>
    /// <param name="scanner">The camera, looking for a QR code.</param>
    /// <param name="finder">Finds the computers on the Wi-Fi.</param>
    /// <param name="connector">Connects to a computer at the addresses its QR code gave.</param>
    /// <param name="pairs">The devices this phone has been paired with; a new pairing is kept there.</param>
    /// <param name="ledger">Keeps the computer paired with as the one backups go to.</param>
    /// <param name="name">What this phone is called, as the computer shows it.</param>
    /// <param name="clock">How long a computer may stay silent.</param>
    /// <param name="terms">Whether the person accepted the note asking for a backup: the phone and the computer tell each other as they pair.</param>
    public PhonePairing(ICodeScanner scanner, IComputerFinder finder, IComputerConnector connector, IPairingStore pairs, IBackupLedger ledger, string name, TimeProvider clock, TermsAcceptance terms)
    {
        _terms = terms;
        _ledger = ledger;
        _scanner = scanner;
        _finder = finder;
        _connector = connector;
        _pairs = pairs;
        _name = name;
        _clock = clock;
    }

    /// <summary>How pairing stands now.</summary>
    public PhonePairingState State
    {
        get { lock (_lock) return _state; }
    }

    /// <summary>The pairing going on now, for whoever waits for it to end.</summary>
    public Task Running { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Opens the camera, and once it has seen the computer's QR code starts pairing, which goes on by itself. Put away,
    /// it leaves pairing as it was before; a code that is not Trovolve's, or a camera that cannot be used, says so.
    /// </summary>
    public async Task<PhonePairingState> ScanAsync(CancellationToken ct)
    {
        Set(new PhonePairingState(PhonePairingStage.Scanning, null, null, null, null));
        string? text;
        try { text = await _scanner.ScanAsync(ct); }
        // A camera that cannot be used says why; typing the code still works.
        catch (IOException ex) { return Set(new PhonePairingState(PhonePairingStage.Failed, null, null, null, ex.Message)); }
        if (text is null) return Set(new PhonePairingState(PhonePairingStage.Idle, null, null, null, null));
        if (PairingLink.Read(text) is not { } link)
            return Set(new PhonePairingState(PhonePairingStage.Failed, null, null, null, "That is not a Trovolve code. On the computer, open Phones, choose Pair a phone, and scan the code it shows."));
        var state = Set(new PhonePairingState(PhonePairingStage.Connecting, link.ComputerName, null, null, null));
        Run(async stop => await PairAsync(await _connector.ConnectAsync(link.Hosts, link.Port, stop), new PairingTicket(link.Secret, null), stop));
        return state;
    }

    /// <summary>Finds the computer showing the code typed, and pairs with it once the person has compared the numbers; it goes on by itself.</summary>
    /// <exception cref="ArgumentException">The code is not six digits.</exception>
    public PhonePairingState EnterCode(string code)
    {
        var digits = new string([.. code.Where(char.IsAsciiDigit)]);
        if (digits.Length != 6 || digits.Length != code.Count(c => !char.IsWhiteSpace(c))) throw new ArgumentException("Type the six digits the computer shows.");
        var state = Set(new PhonePairingState(PhonePairingStage.Finding, null, null, null, null));
        Run(async stop =>
        {
            foreach (var computer in await _finder.FindAsync(stop))
            {
                stop.ThrowIfCancellationRequested();
                Set(new PhonePairingState(PhonePairingStage.Connecting, computer.Name, null, null, null));
                Stream connection;
                try { connection = await computer.Connect(stop); }
                // A computer that cannot be reached is not the one: the next is asked.
                catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException) { continue; }
                try
                {
                    await PairAsync(connection, new PairingTicket(null, digits), stop);
                    return;
                }
                // Another computer, showing another code or none: the next is asked.
                catch (TransferException ex) when (ex.Refused) { }
            }
            throw new TransferException("No computer on this Wi-Fi shows that code. Check the code, and that both are on the same Wi-Fi.");
        });
        return state;
    }

    /// <summary>The person's answer to whether the number on the computer is the same.</summary>
    public void Confirm(bool same)
    {
        lock (_lock) _answer?.TrySetResult(same);
    }

    /// <summary>Stops pairing; nothing is kept.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            _stop?.Cancel();
            _state = new PhonePairingState(PhonePairingStage.Idle, null, null, null, null);
        }
    }

    // Goes on by itself until done or stopped; whatever ends it, the state says how.
    private void Run(Func<CancellationToken, Task> pair)
    {
        CancellationToken stop;
        lock (_lock)
        {
            _stop = new CancellationTokenSource();
            stop = _stop.Token;
        }
        Running = Task.Run(async () =>
        {
            try { await pair(stop); }
            // Stopped: whatever the pairing was doing as it ended, Stop has said how it stands.
            catch (Exception ex) when (ex is IOException or OperationCanceledException && stop.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or OperationCanceledException)
            {
                Set(State with { Stage = PhonePairingStage.Failed, Check = null, Problem = ex is TransferException ? ex.Message : "The computer could not be reached. Check that both are on the same Wi-Fi, and try again." });
            }
        }, CancellationToken.None);
    }

    private async Task PairAsync(Stream connection, PairingTicket ticket, CancellationToken ct)
    {
        await using var stream = new TimedConnection(connection, _clock);
        var linked = await Pairing.PhoneAsync(stream, new Device(await _pairs.SelfIdAsync(ct), _name), _pairs, AskAsync, ticket, ct);
        using var channel = linked.Channel;
        var paired = await channel.ReceiveAsync(ct, "paired");
        var folder = Wire.Text(paired, "folder");
        await _ledger.SaveComputerAsync(new BackupComputer(linked.Other.Id, linked.Other.Name, folder, null, null), ct);
        // Accepted on either, the note is not asked on the other.
        await _terms.HeardAsync(TermsAcceptance.Read(paired["accepted"]), ct);
        // Paired only, for now: backing up comes after.
        await channel.SendAsync(new JsonObject { ["t"] = "bye", ["accepted"] = TermsAcceptance.Write(await _terms.AcceptedAsync(ct)) }, ct);
        Set(new PhonePairingState(PhonePairingStage.Paired, linked.Other.Name, null, folder, null));
    }

    // Shows the number, and waits for the person to say whether the computer shows the same.
    private async Task<bool> AskAsync(string check, Device computer, CancellationToken ct)
    {
        TaskCompletionSource<bool> answer;
        lock (_lock)
        {
            answer = _answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _state = _state with { Stage = PhonePairingStage.Comparing, Computer = computer.Name, Check = check };
        }
        return await answer.Task.WaitAsync(ct);
    }

    private PhonePairingState Set(PhonePairingState state)
    {
        lock (_lock) return _state = state;
    }
}
