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
    private readonly FakeAdvertiser _telling = new();
    private readonly MemoryPairs _pairs = new("computer-1");
    private readonly ComputerPairing _computerPairing;
    private readonly PairingApi _computer;
    private readonly PairingApi _phone;
    private readonly PairingApi _neither;

    public PairingEndpointsTests()
    {
        _computerPairing = new ComputerPairing(new TcpTransferListener(0), _telling, _pairs, "DESKTOP-4K2J9", () => ["127.0.0.1"], TestBackups.Computer(Path.Combine(Path.GetTempPath(), "photosense-home", "Pictures", "PhotoSense")), new TestClock());
        _computer = new PairingApi(new TestApi(s => s.AddSingleton(_computerPairing)));
        var phone = new PhonePairing(_scanner, new FakeComputerFinder(), new TcpComputerConnector(), new MemoryPairs("phone-1"), new MemoryBackupLedger(), "Jamie's iPhone", new TestClock());
        _phone = new PairingApi(new TestApi(s => s.AddSingleton(phone)));
        _neither = new PairingApi(new TestApi());
    }

    public void Dispose() => _computerPairing.StopListening();

    [Test]
    public async Task The_Computer_Is_Named_By_The_Person_As_Phones_Show_It_On_The_Network_And_In_The_QR_Code(CancellationToken ct)
    {
        // Until named, its own name on the network.
        await Assert.That((await _computer.StateAsync(Http.Get("pairing/state").FromClient(), ct)).Json().GetProperty("name").GetString()).IsEqualTo("DESKTOP-4K2J9");
        var open = (await _computer.OpenAsync(Http.Post("pairing/open").FromClient(), ct)).Json();
        await Assert.That((open.GetProperty("name").GetString(), _telling.Telling!.Value.Name)).IsEqualTo(("DESKTOP-4K2J9", "DESKTOP-4K2J9"));

        var named = (await _computer.NameAsync(Http.Post("pairing/name", "{\"name\":\"  Jamie's laptop \"}").FromClient(), ct)).Json();
        await Assert.That((named.GetProperty("name").GetString(), named.GetProperty("code").GetString())).IsEqualTo(("Jamie's laptop", open.GetProperty("code").GetString()));
        await Assert.That(PairingLink.Read(named.GetProperty("link").GetString()!)!.ComputerName).IsEqualTo("Jamie's laptop");
        await Assert.That((_telling.Telling!.Value.Name, await _pairs.SelfNameAsync(ct))).IsEqualTo(("Jamie's laptop", "Jamie's laptop"));

        // Closed, it is named all the same; started again, it is still so named.
        _computerPairing.StopListening();
        var closed = (await _computer.NameAsync(Http.Post("pairing/name", "{\"name\":\"Office PC\"}").FromClient(), ct)).Json();
        await Assert.That((closed.GetProperty("name").GetString(), closed.GetProperty("link").ValueKind, _telling.Telling)).IsEqualTo(("Office PC", JsonValueKind.Null, ((string, string, int)?)null));
        var again = new ComputerPairing(new TcpTransferListener(0), _telling, _pairs, "DESKTOP-4K2J9", () => ["127.0.0.1"], TestBackups.Computer(Path.Combine(Path.GetTempPath(), "photosense-home", "Pictures", "PhotoSense")), new TestClock());
        await again.ListenAsync(ct);
        await Assert.That((again.State.Name, _telling.Telling!.Value.Name)).IsEqualTo(("Office PC", "Office PC"));
        again.StopListening();

        foreach (var body in new[] { "{\"name\":\"  \"}", "not json", $"{{\"name\":\"{new string('x', ComputerPairing.LongestName + 1)}\"}}" })
        {
            var refused = await _computer.NameAsync(Http.Post("pairing/name", body).FromClient(), ct);
            await Assert.That((refused.StatusCode, refused.Text())).IsEqualTo((HttpStatusCode.BadRequest, "Give this computer a name of 1 to 40 letters."));
        }
        await Assert.That((await _phone.NameAsync(Http.Post("pairing/name", "{\"name\":\"x\"}").FromClient(), ct)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

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
