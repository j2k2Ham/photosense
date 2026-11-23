using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using PhotoSense.Infrastructure.Transfer;

namespace PhotoSense.Tests.Infrastructure;

/// <summary>A computer saying on the network that it waits for phones, and a phone finding it there.</summary>
public class MdnsAdvertiserTests
{
    private static readonly IPAddress[] Addresses = [IPAddress.Parse("192.168.1.20"), IPAddress.Parse("fe80::1")];

    /// <summary>A socket of the test's own: what the network asks arrives as the test says, and what is sent is kept.</summary>
    private sealed class FakeSocket : IMdnsSocket
    {
        public Channel<(byte[], IPEndPoint)> Heard { get; } = Channel.CreateUnbounded<(byte[], IPEndPoint)>();
        public Channel<(byte[] Message, IPEndPoint To)> Sent { get; } = Channel.CreateUnbounded<(byte[], IPEndPoint)>();
        public bool Disposed { get; private set; }
        /// <summary>Where nothing can be sent, as to everyone on a machine with no route for multicast.</summary>
        public Func<IPEndPoint, bool> Unreachable { get; init; } = _ => false;

        public async Task<(byte[] Message, IPEndPoint From)> ReceiveAsync(CancellationToken ct) => await Heard.Reader.ReadAsync(ct);

        public async Task SendAsync(byte[] message, IPEndPoint to, CancellationToken ct)
        {
            if (Unreachable(to)) throw new SocketException((int)SocketError.NetworkUnreachable);
            await Sent.Writer.WriteAsync((message, to), ct);
        }

        public void Dispose() => Disposed = true;
    }

    // A question as a phone asks it: for the service, its instance or its host, directly or to everyone.
    private static byte[] Question(string name, bool direct, bool response = false)
        => [0, 0, (byte)(response ? 0x84 : 0), 0, 0, 1, 0, 0, 0, 0, 0, 0, .. Mdns.Name(name), 0, 12, (byte)(direct ? 0x80 : 0), 1];

    [Test]
    public async Task The_Answer_Tells_A_Phone_Where_The_Computer_Waits_And_What_It_Is_Called(CancellationToken ct)
    {
        var answer = MdnsAdvertiser.Answer("abc123", "Studio iMac", 47616, Addresses);

        var found = Mdns.Instances(answer, IPAddress.Parse("10.9.9.9"), MdnsComputerFinder.Service).Single();
        await Assert.That(found).IsEqualTo(new MdnsInstance("abc123", "Studio iMac", IPAddress.Parse("192.168.1.20"), 47616));
        await Assert.That(Mdns.Instances([1, 2, 3], IPAddress.Loopback, MdnsComputerFinder.Service)).IsEmpty();
        // A computer is not a phone offering photos.
        await Assert.That(MdnsPhoneFinder.Read(answer, IPAddress.Loopback)).IsEmpty();
    }

    [Test]
    public async Task A_Name_Too_Long_For_Its_Text_Is_Cut_Short(CancellationToken ct)
    {
        var answer = MdnsAdvertiser.Answer("abc123", new string('x', 300), 47616, []);
        var found = Mdns.Instances(answer, IPAddress.Parse("10.9.9.9"), MdnsComputerFinder.Service).Single();

        await Assert.That(found.Name.Length).IsEqualTo(250);
        await Assert.That(found.Address).IsEqualTo(IPAddress.Parse("10.9.9.9"));
    }

    [Test]
    public async Task Only_Questions_About_The_Computer_Are_Answered_And_Each_Says_How(CancellationToken ct)
    {
        await Assert.That(MdnsAdvertiser.Asks(Question(MdnsComputerFinder.Service, direct: true), "abc123")).IsTrue();
        await Assert.That(MdnsAdvertiser.Asks(Question($"abc123.{MdnsComputerFinder.Service}", direct: false), "abc123")).IsFalse();
        await Assert.That(MdnsAdvertiser.Asks(Question("photosense-abc123.local", direct: false), "abc123")).IsFalse();
        await Assert.That(MdnsAdvertiser.Asks(Question(MdnsPhoneFinder.Service, direct: true), "abc123")).IsNull();
        await Assert.That(MdnsAdvertiser.Asks(Question(MdnsComputerFinder.Service, direct: true, response: true), "abc123")).IsNull();
        await Assert.That(MdnsAdvertiser.Asks([0, 0, 0], "abc123")).IsNull();
        await Assert.That(MdnsAdvertiser.Asks([0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 9, 1], "abc123")).IsNull();
    }

    [Test]
    public async Task It_Says_It_Is_There_Then_Answers_Each_Question_To_The_Asker_Or_To_Everyone_Until_Stopped(CancellationToken ct)
    {
        var socket = new FakeSocket();
        var advertiser = new MdnsAdvertiser(() => socket, () => Addresses);
        advertiser.Start("abc123", "Studio iMac", 47616);
        var told = MdnsAdvertiser.Answer("abc123", "Studio iMac", 47616, Addresses);

        var said = await socket.Sent.Reader.ReadAsync(ct);
        await Assert.That(said.Message).IsEquivalentTo(told, CollectionOrdering.Matching);
        await Assert.That(said.To.ToString()).IsEqualTo(Mdns.Everyone.ToString());
        var asker = new IPEndPoint(IPAddress.Parse("192.168.1.30"), 5353);
        var oneShot = new IPEndPoint(IPAddress.Parse("192.168.1.31"), 61000);
        await socket.Heard.Writer.WriteAsync((Question(MdnsPhoneFinder.Service, direct: true), asker), ct);
        await socket.Heard.Writer.WriteAsync((Question(MdnsComputerFinder.Service, direct: true), asker), ct);
        await socket.Heard.Writer.WriteAsync((Question(MdnsComputerFinder.Service, direct: false), asker), ct);
        await socket.Heard.Writer.WriteAsync((Question(MdnsComputerFinder.Service, direct: false), oneShot), ct);

        await Assert.That((await socket.Sent.Reader.ReadAsync(ct)).To).IsEqualTo(asker);
        await Assert.That((await socket.Sent.Reader.ReadAsync(ct)).To).IsEqualTo(Mdns.Everyone);
        await Assert.That((await socket.Sent.Reader.ReadAsync(ct)).To).IsEqualTo(oneShot);

        advertiser.Stop();
        await advertiser.Answering;
        await Assert.That(socket.Disposed).IsTrue();
        advertiser.Stop();
    }

    [Test]
    public async Task Starting_Again_Stops_What_Was_Said_Before(CancellationToken ct)
    {
        var sockets = new List<FakeSocket>();
        var advertiser = new MdnsAdvertiser(() => { var s = new FakeSocket(); sockets.Add(s); return s; }, () => Addresses);
        advertiser.Start("abc123", "Studio iMac", 1);
        var first = advertiser.Answering;
        advertiser.Start("abc123", "Studio iMac", 2);
        await first;

        await Assert.That(sockets[0].Disposed).IsTrue();
        await Assert.That(sockets[1].Disposed).IsFalse();
        advertiser.Stop();
        await Assert.That(new MdnsAdvertiser()).IsNotNull();
    }

    [Test]
    public async Task A_Port_That_Cannot_Be_Had_Leaves_The_Computer_Unannounced_But_Running(CancellationToken ct)
    {
        var advertiser = new MdnsAdvertiser(() => throw new SocketException((int)SocketError.AddressAlreadyInUse), () => Addresses);
        advertiser.Start("abc123", "Studio iMac", 47616);
        await advertiser.Answering;
        advertiser.Stop();
        await Assert.That(advertiser.Answering.IsCompletedSuccessfully).IsTrue();
    }

    [Test]
    public async Task With_No_Route_To_Everyone_It_Still_Answers_Each_Phone_That_Asks_Directly(CancellationToken ct)
    {
        var socket = new FakeSocket { Unreachable = to => to.Equals(Mdns.Everyone) };
        var advertiser = new MdnsAdvertiser(() => socket, () => Addresses);
        advertiser.Start("abc123", "Studio iMac", 47616);
        var phone = new IPEndPoint(IPAddress.Parse("192.168.1.30"), 5353);
        // Asked to everyone, the answer cannot go; asked directly, it goes to the phone.
        await socket.Heard.Writer.WriteAsync((Question(MdnsComputerFinder.Service, direct: false), phone), ct);
        await socket.Heard.Writer.WriteAsync((Question(MdnsComputerFinder.Service, direct: true), phone), ct);
        var (_, to) = await socket.Sent.Reader.ReadAsync(ct);
        await Assert.That(to).IsEqualTo(phone);
        advertiser.Stop();
        await advertiser.Answering;
    }

    [Test]
    public async Task A_Socket_That_Fails_Once_Stopped_Ends_Quietly(CancellationToken ct)
    {
        foreach (Exception failure in new Exception[] { new ObjectDisposedException("socket"), new SocketException() })
        {
            var advertiser = new MdnsAdvertiser(() => new FailingSocket(failure), () => []);
            advertiser.Start("abc123", "Studio iMac", 1);
            advertiser.Stop();
            await advertiser.Answering;
        }
    }

    /// <summary>A socket that sends, then fails to hear once the advertiser has stopped, as a closed one does.</summary>
    private sealed class FailingSocket(Exception failure) : IMdnsSocket
    {
        private readonly TaskCompletionSource _closed = new();

        public async Task<(byte[] Message, IPEndPoint From)> ReceiveAsync(CancellationToken ct)
        {
            await _closed.Task;
            throw failure;
        }

        public Task SendAsync(byte[] message, IPEndPoint to, CancellationToken ct) => Task.CompletedTask;

        public void Dispose() => _closed.TrySetResult();
    }

    [Test]
    public async Task On_The_Network_A_Phone_Finds_The_Computer_Once_And_Can_Connect_To_It(CancellationToken ct)
    {
        var waiting = new TcpListener(IPAddress.Loopback, 0);
        waiting.Start();
        var port = ((IPEndPoint)waiting.LocalEndpoint).Port;
        // The computer's own socket, on a port of the test's, answering what it hears directly.
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var mdnsPort = ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
        probe.Dispose();
        var advertiser = new MdnsAdvertiser(() => new UdpMdnsSocket(mdnsPort, [IPAddress.Loopback, IPAddress.Parse("203.0.113.7")]), () => [IPAddress.Loopback]);
        try
        {
            advertiser.Start("abc123", "Studio iMac", port);
            var finder = new MdnsComputerFinder(() => [IPAddress.Loopback], new IPEndPoint(IPAddress.Loopback, mdnsPort), TimeSpan.FromMilliseconds(500));
            var found = (await finder.FindAsync(ct)).Single();
            await Assert.That((found.Id, found.Name)).IsEqualTo(("abc123", "Studio iMac"));

            var accepting = waiting.AcceptTcpClientAsync(ct);
            await using (var stream = await found.Connect(ct)) await stream.WriteAsync("hi"u8.ToArray(), ct);
            using var computer = await accepting;
            var heard = new byte[2];
            await computer.GetStream().ReadExactlyAsync(heard, ct);
            await Assert.That(System.Text.Encoding.ASCII.GetString(heard)).IsEqualTo("hi");
        }
        finally
        {
            advertiser.Stop();
            waiting.Stop();
        }
        await Assert.That(new MdnsComputerFinder()).IsNotNull();
    }

    [Test]
    public async Task A_Computer_No_Address_Of_Which_Answers_Cannot_Be_Reached(CancellationToken ct)
    {
        var connector = new TcpComputerConnector(TimeSpan.FromMilliseconds(200));
        var failure = await Assert.ThrowsAsync<IOException>(() => connector.ConnectAsync(["not-an-address", "203.0.113.7"], 47616, ct));

        await Assert.That(failure!.Message).IsEqualTo("The computer could not be reached. Check that both are on the same Wi-Fi, and try again.");
        // An address that turns the connection away, as one with nothing waiting does, is passed over too.
        var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        await Assert.ThrowsAsync<IOException>(() => new TcpComputerConnector(TimeSpan.FromSeconds(10)).ConnectAsync(["127.0.0.1"], port, ct));
        await Assert.ThrowsAsync<OperationCanceledException>(() => connector.ConnectAsync(["203.0.113.7"], 47616, new CancellationToken(canceled: true)));
        await Assert.That(new TcpComputerConnector()).IsNotNull();
    }
}
