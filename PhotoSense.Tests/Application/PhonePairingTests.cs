using System.Net;
using System.Net.Sockets;
using PhotoSense.Application.Plans;
using PhotoSense.Application.Transfer;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Transfer;

namespace PhotoSense.Tests.Application;

/// <summary>Pairing a phone with a computer: the phone scans the computer's QR code, or types its code and compares numbers.</summary>
public sealed class PhonePairingTests : IDisposable
{
    private readonly MemoryPairs _computerPairs = new("computer-1");
    private readonly MemoryPairs _phonePairs = new("phone-1");
    private readonly FakeAdvertiser _advertiser = new();
    private readonly FakeCodeScanner _scanner = new();
    private readonly FakeComputerFinder _finder = new();
    private readonly TestClock _clock = new(TestClock.Day);
    private readonly List<ComputerPairing> _computers = [];
    private readonly PhonePairing _phone;
    private readonly MemoryBackupLedger _ledger = new();
    private readonly TermsAcceptance _phoneTerms = TestPlans.Terms();
    // The person's own folder on the computer: only names are worked out from it, and nothing is written there.
    private readonly string _home = Path.Combine(Path.GetTempPath(), "photosense-home");

    public PhonePairingTests()
    {
        _phone = new PhonePairing(_scanner, _finder, new TcpComputerConnector(TimeSpan.FromMilliseconds(300)), _phonePairs, _ledger, "Jamie's iPhone", TimeProvider.System, _phoneTerms);
    }

    public void Dispose() => _computers.ForEach(c => c.StopListening());

    // A computer on this one's own network: unreachable and unreadable addresses first, as a QR code may list them.
    private ComputerPairing Computer(string name = "Studio iMac", MemoryPairs? pairs = null, TermsAcceptance? terms = null)
    {
        var computer = new ComputerPairing(new TcpTransferListener(0), _advertiser, pairs ?? _computerPairs, name, () => ["not-an-address", "203.0.113.7", "127.0.0.1"],
            TestBackups.Computer(Path.Combine(_home, "Pictures", "PhotoSense"), home: _home, terms: terms), _clock);
        _computers.Add(computer);
        return computer;
    }

    private static int PortOf(ComputerPairingState state) => PairingLink.Read(state.Link)!.Port;

    private static FoundComputer Found(string id, int port) => new(id, id, ct => Loopback.ConnectAsync(port, ct));

    private static async Task Until(Func<bool> done, string what)
    {
        for (var waited = 0; !done(); waited++)
        {
            if (waited == 400) throw new TimeoutException($"Not {what} within 20 seconds.");
            await Task.Delay(50);
        }
    }

    [Test]
    public async Task The_Computer_Shows_A_QR_Code_And_A_Code_That_Change_Every_Five_Minutes_And_Says_It_Is_There_Until_It_Stops(CancellationToken ct)
    {
        var computer = Computer();
        await Assert.That(computer.State.Stage).IsEqualTo(ComputerPairingStage.Idle);
        var open = await computer.OpenAsync(ct);

        await Assert.That(open.Stage).IsEqualTo(ComputerPairingStage.Waiting);
        await Assert.That(open.Code!).Matches("^[0-9]{6}$");
        await Assert.That(open.Expires).IsEqualTo(TestClock.Day + ComputerPairing.Lifetime);
        var link = PairingLink.Read(open.Link)!;
        await Assert.That((link.ComputerId, link.ComputerName, link.Hosts.Count, link.Secret.Length)).IsEqualTo(("computer-1", "Studio iMac", 3, 32));
        await Assert.That(_advertiser.Telling).IsEqualTo(("computer-1", "Studio iMac", link.Port));

        await Assert.That(computer.State.Link).IsEqualTo(open.Link);
        _clock.Now = TestClock.Day + ComputerPairing.Lifetime;
        var later = computer.State;
        await Assert.That(later.Link).IsNotEqualTo(open.Link);
        await Assert.That(later.Expires).IsEqualTo(_clock.Now + ComputerPairing.Lifetime);
        // Opened again, it is still the one listener on the same port, with a new code.
        await Assert.That(PortOf(await computer.OpenAsync(ct))).IsEqualTo(link.Port);

        // Closed, it shows no code, but phones it knows can still back up to it until the program stops.
        computer.Close();
        await Assert.That((computer.State.Stage, _advertiser.Telling)).IsEqualTo((ComputerPairingStage.Idle, ((string, string, int)?)("computer-1", "Studio iMac", link.Port)));
        computer.StopListening();
        await Assert.That(_advertiser.Telling).IsNull();
        computer.StopListening();
    }

    [Test]
    public async Task Scanning_The_QR_Code_Pairs_At_Once_And_The_Phone_Says_Where_Its_Backups_Go(CancellationToken ct)
    {
        var computer = Computer();
        _scanner.Sees = (await computer.OpenAsync(ct)).Link;

        var started = await _phone.ScanAsync(ct);
        await Assert.That((started.Stage, started.Computer)).IsEqualTo((PhonePairingStage.Connecting, "Studio iMac"));
        await _phone.Running;

        await Assert.That(_phone.State).IsEqualTo(new PhonePairingState(PhonePairingStage.Paired, "Studio iMac", null, "Pictures/PhotoSense/Jamie's iPhone", null));
        await Until(() => computer.State.Stage == ComputerPairingStage.Paired, "paired on the computer");
        await Assert.That(computer.State.Phone).IsEqualTo("Jamie's iPhone");
        var phoneKept = await _phonePairs.GetAsync("computer-1", ct);
        var computerKept = await _computerPairs.GetAsync("phone-1", ct);
        await Assert.That(Convert.ToHexString(phoneKept!.Key)).IsEqualTo(Convert.ToHexString(computerKept!.Key));
    }

    [Test]
    public async Task The_Note_Accepted_On_The_Computer_Is_Accepted_On_The_Phone_It_Pairs_With(CancellationToken ct)
    {
        var computerTerms = TestPlans.Terms();
        await computerTerms.HeardAsync(new DateOnly(2026, 10, 2), ct);
        var computer = Computer(terms: computerTerms);
        _scanner.Sees = (await computer.OpenAsync(ct)).Link;

        await _phone.ScanAsync(ct);
        await _phone.Running;

        await Assert.That(_phone.State.Stage).IsEqualTo(PhonePairingStage.Paired);
        await Assert.That(await _phoneTerms.AcceptedAsync(ct)).IsEqualTo(new DateOnly(2026, 10, 2));
    }

    [Test]
    public async Task The_Note_Accepted_On_The_Phone_Is_Accepted_On_The_Computer_It_Pairs_With(CancellationToken ct)
    {
        await _phoneTerms.HeardAsync(new DateOnly(2026, 10, 3), ct);
        var computerTerms = TestPlans.Terms();
        var computer = Computer(terms: computerTerms);
        _scanner.Sees = (await computer.OpenAsync(ct)).Link;

        await _phone.ScanAsync(ct);
        await _phone.Running;

        // The phone says it as it says goodbye, which the computer reads after showing it is paired.
        for (var waited = 0; await computerTerms.AcceptedAsync(ct) is null; waited++)
        {
            if (waited == 400) throw new TimeoutException("Not accepted on the computer within 20 seconds.");
            await Task.Delay(50, ct);
        }
        await Assert.That(await computerTerms.AcceptedAsync(ct)).IsEqualTo(new DateOnly(2026, 10, 3));
    }

    [Test]
    public async Task A_QR_Code_From_Before_Is_Not_This_Computers_And_It_Goes_On_Waiting(CancellationToken ct)
    {
        var computer = Computer();
        var old = (await computer.OpenAsync(ct)).Link;
        await computer.OpenAsync(ct);
        _scanner.Sees = old;

        await _phone.ScanAsync(ct);
        await _phone.Running;

        await Assert.That(_phone.State.Problem).IsEqualTo("Studio iMac is not the computer whose code was scanned. Scan the code again.");
        await Assert.That(computer.State.Stage).IsEqualTo(ComputerPairingStage.Waiting);
        await Assert.That(await _computerPairs.GetAsync("phone-1", ct)).IsNull();
    }

    [Test]
    public async Task Putting_The_Camera_Away_Changes_Nothing_And_Another_Code_Is_Said_Not_To_Be_PhotoSenses(CancellationToken ct)
    {
        _phone.Stop();
        await Assert.That((await _phone.ScanAsync(ct)).Stage).IsEqualTo(PhonePairingStage.Idle);
        _scanner.Sees = "https://example.com/menu";
        var other = await _phone.ScanAsync(ct);

        await Assert.That(other.Stage).IsEqualTo(PhonePairingStage.Failed);
        await Assert.That(other.Problem).IsEqualTo("That is not a PhotoSense code. On the computer, open Phones, choose Pair a phone, and scan the code it shows.");
        await Assert.That(_scanner.Opened).IsEqualTo(2);
        _scanner.Broken = "This phone cannot scan codes yet. Choose Enter the code instead.";
        await Assert.That((await _phone.ScanAsync(ct)).Problem).IsEqualTo("This phone cannot scan codes yet. Choose Enter the code instead.");
    }

    [Test]
    public async Task A_Computer_That_Cannot_Be_Reached_Is_Said_So(CancellationToken ct)
    {
        _scanner.Sees = new PairingLink("computer-1", "Studio iMac", ["127.0.0.1"], FreePort(), new byte[32]).Write();

        await _phone.ScanAsync(ct);
        await _phone.Running;

        await Assert.That(_phone.State.Problem).IsEqualTo("The computer could not be reached. Check that both are on the same Wi-Fi, and try again.");
    }

    [Test]
    public async Task Typing_The_Code_Finds_The_Computer_Showing_It_Past_Others_And_Pairs_Once_Both_Say_The_Numbers_Match(CancellationToken ct)
    {
        var elsewhere = Computer("Office PC", new MemoryPairs("computer-2"));
        var other = await elsewhere.OpenAsync(ct);
        var computer = Computer();
        var open = await computer.OpenAsync(ct);
        _finder.Computers.AddRange([Found("Office PC", PortOf(other)), Found("Gone", FreePort()), Found("Studio iMac", PortOf(open))]);

        var finding = _phone.EnterCode($"{open.Code![..3]} {open.Code[3..]}");
        await Assert.That(finding.Stage).IsEqualTo(PhonePairingStage.Finding);
        await Until(() => _phone.State.Stage == PhonePairingStage.Comparing && computer.State.Stage == ComputerPairingStage.Comparing, "comparing");

        await Assert.That(_phone.State.Computer).IsEqualTo("Studio iMac");
        await Assert.That(_phone.State.Check).IsEqualTo(computer.State.Check);
        await Assert.That(computer.State.Phone).IsEqualTo("Jamie's iPhone");
        await Assert.That(elsewhere.State.Stage).IsEqualTo(ComputerPairingStage.Waiting);
        computer.Confirm(true);
        _phone.Confirm(true);
        await _phone.Running;

        await Assert.That((_phone.State.Stage, _phone.State.Folder)).IsEqualTo((PhonePairingStage.Paired, "Pictures/PhotoSense/Jamie's iPhone"));
        await Until(() => computer.State.Stage == ComputerPairingStage.Paired, "paired on the computer");
    }

    [Test]
    public async Task Numbers_Said_Not_To_Match_Stop_Both_And_Nothing_Is_Kept(CancellationToken ct)
    {
        var computer = Computer();
        var open = await computer.OpenAsync(ct);
        _finder.Computers.Add(Found("Studio iMac", PortOf(open)));
        _phone.EnterCode(open.Code!);
        // Both must be asking: an answer given on the computer before it asks is not an answer to anything.
        await Until(() => _phone.State.Stage == PhonePairingStage.Comparing && computer.State.Stage == ComputerPairingStage.Comparing, "comparing");

        // Each side hears the other's answer once its own person has answered.
        _phone.Confirm(false);
        computer.Confirm(true);
        await _phone.Running;
        await Until(() => computer.State.Stage == ComputerPairingStage.Failed, "failed on the computer");

        await Assert.That(_phone.State.Problem).IsEqualTo("The numbers did not match, so nothing was sent. Try again, and look that both show the same number.");
        await Assert.That(computer.State.Problem).IsNotNull();
        await Assert.That(await _phonePairs.GetAsync("computer-1", ct)).IsNull();
    }

    [Test]
    public async Task No_Computer_Showing_The_Code_Is_Said_So_And_A_Code_That_Is_Not_Six_Digits_Is_Refused(CancellationToken ct)
    {
        var computer = Computer();
        var open = await computer.OpenAsync(ct);
        _finder.Computers.Add(Found("Studio iMac", PortOf(open)));
        var wrong = open.Code == "000000" ? "111111" : "000000";

        _phone.EnterCode(wrong);
        await _phone.Running;

        await Assert.That(_phone.State.Problem).IsEqualTo("No computer on this Wi-Fi shows that code. Check the code, and that both are on the same Wi-Fi.");
        await Assert.That(computer.State.Stage).IsEqualTo(ComputerPairingStage.Waiting);
        await Assert.That(Assert.Throws<ArgumentException>(() => _phone.EnterCode("12345"))!.Message).IsEqualTo("Type the six digits the computer shows.");
        await Assert.ThrowsAsync<ArgumentException>(() => Task.FromResult(_phone.EnterCode("12a456")));
    }

    [Test]
    public async Task Stopping_On_The_Phone_Or_Closing_On_The_Computer_Ends_It_With_Nothing_Kept(CancellationToken ct)
    {
        var computer = Computer();
        var open = await computer.OpenAsync(ct);
        _finder.Computers.Add(Found("Studio iMac", PortOf(open)));
        _phone.EnterCode(open.Code!);
        await Until(() => _phone.State.Stage == PhonePairingStage.Comparing && computer.State.Stage == ComputerPairingStage.Comparing, "comparing");

        _phone.Stop();
        await _phone.Running;
        await Assert.That(_phone.State.Stage).IsEqualTo(PhonePairingStage.Idle);

        var again = await computer.OpenAsync(ct);
        _phone.EnterCode(again.Code!);
        await Until(() => computer.State.Stage == ComputerPairingStage.Comparing, "comparing again");
        computer.Close();
        await _phone.Running;
        // The computer says no and stops listening at once: the phone hears the one or the other first.
        await Assert.That(_phone.State.Stage).IsEqualTo(PhonePairingStage.Failed);
        await Assert.That(await _computerPairs.GetAsync("phone-1", ct)).IsNull();
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
