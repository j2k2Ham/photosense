using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Application.Transfer;
using PhotoSense.Infrastructure.Transfer;

namespace PhotoSense.Tests.Endpoints;

/// <summary>Pairing through the service's addresses, on a computer and on a phone, and what a scan found.</summary>
public sealed class PairingEndpointsTests : IDisposable
{
    private readonly FakeCodeScanner _scanner = new();
    private readonly ComputerPairing _computerPairing;
    private readonly PairingApi _computer;
    private readonly PairingApi _phone;
    private readonly PairingApi _neither;

    public PairingEndpointsTests()
    {
        _computerPairing = new ComputerPairing(new TcpTransferListener(0), new FakeAdvertiser(), new MemoryPairs("computer-1"), "Studio iMac", () => ["127.0.0.1"], TestBackups.Computer(Path.Combine(Path.GetTempPath(), "photosense-home", "Pictures", "PhotoSense")), new TestClock());
        _computer = new PairingApi(new TestApi(s => s.AddSingleton(_computerPairing)));
        var phone = new PhonePairing(_scanner, new FakeComputerFinder(), new TcpComputerConnector(), new MemoryPairs("phone-1"), new MemoryBackupLedger(), "Jamie's iPhone", new TestClock());
        _phone = new PairingApi(new TestApi(s => s.AddSingleton(phone)));
        _neither = new PairingApi(new TestApi());
    }

    public void Dispose() => _computerPairing.Close();

    [Test]
    public async Task A_Computer_Opens_Pair_A_Phone_With_A_QR_Code_And_A_Code_And_Closes_It(CancellationToken ct)
    {
        var open = (await _computer.OpenAsync(Http.Post("pairing/open").FromClient(), ct)).Json();
        await Assert.That(open.GetProperty("stage").GetString()).IsEqualTo("waiting");
        await Assert.That(open.GetProperty("code").GetString()!).Matches("^[0-9]{6}$");
        await Assert.That(open.GetProperty("link").GetString()!).StartsWith("photosense:pair?v=1&id=computer-1");
        await Assert.That(DateTimeOffset.Parse(open.GetProperty("expires").GetString()!, System.Globalization.CultureInfo.InvariantCulture)).IsEqualTo(TestClock.Day + ComputerPairing.Lifetime);

        var state = (await _computer.StateAsync(Http.Get("pairing/state").FromClient(), ct)).Json();
        await Assert.That(state.GetProperty("code").GetString()).IsEqualTo(open.GetProperty("code").GetString());
        var confirmed = await _computer.ConfirmAsync(Http.Post("pairing/confirm", "{\"same\":true}").FromClient(), ct);
        await Assert.That(confirmed.Json().GetProperty("stage").GetString()).IsEqualTo("waiting");

        await Assert.That((await _computer.CloseAsync(Http.Post("pairing/close").FromClient(), ct)).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That((await _computer.StateAsync(Http.Get("pairing/state").FromClient(), ct)).Json().GetProperty("stage").GetString()).IsEqualTo("idle");
    }

    [Test]
    public async Task A_Phone_Scans_Types_And_Stops(CancellationToken ct)
    {
        var scanned = (await _phone.ScanAsync(Http.Post("pairing/scan").FromClient(), ct)).Json();
        await Assert.That(scanned.GetProperty("stage").GetString()).IsEqualTo("idle");
        await Assert.That(_scanner.Opened).IsEqualTo(1);

        var typed = (await _phone.CodeAsync(Http.Post("pairing/code", "{\"code\":\"482917\"}").FromClient(), ct)).Json();
        await Assert.That(typed.GetProperty("stage").GetString()).IsEqualTo("finding");
        var notSix = await _phone.CodeAsync(Http.Post("pairing/code", "{\"code\":\"4829\"}").FromClient(), ct);
        await Assert.That((notSix.StatusCode, notSix.Text())).IsEqualTo((HttpStatusCode.BadRequest, "Type the six digits the computer shows."));
        var none = await _phone.CodeAsync(Http.Post("pairing/code", "not json").FromClient(), ct);
        await Assert.That(none.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        await Assert.That((await _phone.ConfirmAsync(Http.Post("pairing/confirm", "not json").FromClient(), ct)).StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await _phone.StopAsync(Http.Post("pairing/stop").FromClient(), ct)).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        var state = (await _phone.StateAsync(Http.Get("pairing/state").FromClient(), ct)).Json();
        await Assert.That(state.GetProperty("stage").GetString()).IsEqualTo("idle");
        await Assert.That(state.GetProperty("folder").ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    [Test]
    public async Task Each_Program_Answers_Only_Its_Own_Half(CancellationToken ct)
    {
        var onPhone = await _phone.OpenAsync(Http.Post("pairing/open").FromClient(), ct);
        var onComputer = await _computer.ScanAsync(Http.Post("pairing/scan").FromClient(), ct);

        await Assert.That((onPhone.StatusCode, onPhone.Text())).IsEqualTo((HttpStatusCode.NotFound, "Only a computer shows a code to pair with."));
        await Assert.That((onComputer.StatusCode, onComputer.Text())).IsEqualTo((HttpStatusCode.NotFound, "Only a phone scans or types a computer's code."));
        await Assert.That((await _phone.CloseAsync(Http.Post("pairing/close").FromClient(), ct)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await _computer.CodeAsync(Http.Post("pairing/code", "{\"code\":\"482917\"}").FromClient(), ct)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await _computer.StopAsync(Http.Post("pairing/stop").FromClient(), ct)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await _neither.StateAsync(Http.Get("pairing/state").FromClient(), ct)).Json().GetProperty("stage").GetString()).IsEqualTo("idle");
    }

    [Test]
    public async Task Only_The_Page_Itself_Pairs(CancellationToken ct)
    {
        await Assert.That((await _computer.OpenAsync(Http.Post("pairing/open"), ct)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await _computer.StateAsync(Http.Get("pairing/state"), ct)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await _computer.ConfirmAsync(Http.Post("pairing/confirm"), ct)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await _phone.ScanAsync(Http.Post("pairing/scan"), ct)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(_scanner.Opened).IsEqualTo(0);
    }

    [Test]
    public async Task A_Scan_Says_What_It_Found_By_Kind_With_No_File_Named(CancellationToken ct)
    {
        var found = (await _neither.FoundAsync(Http.Get("scan/found"), ct)).Json().GetProperty("items").EnumerateArray()
            .Select(i => $"{i.GetProperty("kind").GetString()} {i.GetProperty("count").GetInt32()} {i.GetProperty("bytes").GetInt64()}");

        await Assert.That(found).IsEquivalentTo(["duplicates 0 0", "similar 0 0", "largeVideos 0 0"], CollectionOrdering.Matching);
    }
}
