using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using PhotoSense.Application.Transfer;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Transfer;

namespace PhotoSense.Tests.Infrastructure;

/// <summary>How a phone waits for computers, and how a computer finds the phones on its networks and on a cable.</summary>
public sealed class TransferLinkTests
{
    // ---- the phone waiting

    [Test]
    public async Task The_Phone_Waits_On_Its_Own_Port_Or_Another_When_That_One_Is_Taken()
    {
        var taken = new TcpListener(IPAddress.Any, 0);
        taken.Start();
        var port = ((IPEndPoint)taken.LocalEndpoint).Port;
        var listener = new TcpTransferListener(port);
        try
        {
            var waiting = listener.Start((_, _) => Task.CompletedTask);
            await Assert.That(waiting).IsNotEqualTo(port);
            // Started again, it stops waiting where it was first.
            var again = listener.Start((_, _) => Task.CompletedTask);
            await Assert.That(again).IsNotEqualTo(port);
        }
        finally
        {
            listener.Stop();
            taken.Stop();
        }
    }

    [Test]
    public async Task Each_Computer_Is_Served_What_Goes_Wrong_With_One_Leaves_The_Next_And_Stopping_Ends_Those_Still_Open()
    {
        var listener = new TcpTransferListener(0);
        listener.Stop();
        var served = 0;
        var holding = new TaskCompletionSource();
        var ended = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception[] failures = [new IOException("cut"), new OperationCanceledException(), new ObjectDisposedException("stream"), new InvalidOperationException("odd"), new FormatException("not caught")];
        var port = listener.Start(async (stream, ct) =>
        {
            var which = Interlocked.Increment(ref served);
            if (which <= failures.Length) throw failures[which - 1];
            await stream.WriteAsync("hi"u8.ToArray(), ct);
            holding.SetResult();
            try { await stream.ReadExactlyAsync(new byte[1], ct); }
            catch (Exception ex)
            {
                ended.SetResult(ex);
                throw;
            }
        });
        try
        {
            for (var i = 0; i < failures.Length; i++)
            {
                await using var failing = await Loopback.ConnectAsync(port, CancellationToken.None);
                await Assert.That(await failing.ReadAsync(new byte[1])).IsEqualTo(0);
            }
            await using var open = await Loopback.ConnectAsync(port, CancellationToken.None);
            var greeting = new byte[2];
            await open.ReadExactlyAsync(greeting);
            await Assert.That(Encoding.ASCII.GetString(greeting)).IsEqualTo("hi");
            await holding.Task;
            listener.Stop();
            await Assert.That(await ended.Task.WaitAsync(TimeSpan.FromSeconds(10))).IsNotNull();
        }
        finally { listener.Stop(); }
    }

    // ---- finding phones on the network

    private static byte[] Name(params string[] labels) => [.. labels.SelectMany(l => (byte[])[(byte)Encoding.UTF8.GetByteCount(l), .. Encoding.UTF8.GetBytes(l)]), 0];

    private static byte[] Pointer(int at) => [(byte)(0xC0 | (at >> 8)), (byte)at];

    private static byte[] Number(int value, int size)
    {
        var bytes = new byte[size];
        if (size == 2) BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
        else BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return bytes;
    }

    /// <summary>A DNS message being written, which knows where each part of it begins for names that point back.</summary>
    private sealed class Answer
    {
        private readonly List<byte> _bytes = [];
        private int _records;
        private readonly int _questions;

        public Answer(int questions = 0)
        {
            _questions = questions;
            _bytes.AddRange(new byte[12]);
        }

        public int At => _bytes.Count;

        public Answer Question(byte[] name)
        {
            _bytes.AddRange([.. name, 0, 12, 0, 1]);
            return this;
        }

        /// <summary>A record; <paramref name="data"/> is told where it begins, for names inside it.</summary>
        public int Record(byte[] owner, int type, Func<int, byte[]> data)
        {
            _bytes.AddRange([.. owner, .. Number(type, 2), 0x80, 1, .. Number(120, 4)]);
            var begins = At + 2;
            var body = data(begins);
            _bytes.AddRange([.. Number(body.Length, 2), .. body]);
            _records++;
            return begins;
        }

        public byte[] Bytes()
        {
            var bytes = _bytes.ToArray();
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), (ushort)_questions);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), (ushort)_records);
            return bytes;
        }
    }

    private static byte[] Txt(params string[] entries) => [.. entries.SelectMany(e => (byte[])[(byte)Encoding.UTF8.GetByteCount(e), .. Encoding.UTF8.GetBytes(e)])];

    private static byte[] Srv(int port, byte[] target) => [0, 0, 0, 0, .. Number(port, 2), .. target];

    // As an iPhone answers: the service's name once, and the names after it pointing back into it.
    private static byte[] IPhoneAnswer(int port, string id = "phone-1", string name = "Jamie's iPhone", byte[]? address = null)
    {
        var answer = new Answer();
        var serviceAt = answer.At;
        var instanceAt = answer.Record(Name("_photosense", "_tcp", "local"), 12, _ => [.. Name("Jamie's iPhone")[..^1], .. Pointer(serviceAt)]);
        var targetAt = 0;
        answer.Record(Pointer(instanceAt), 33, at => { targetAt = at + 6; return Srv(port, [.. Name("Jamies-iPhone")[..^1], .. Pointer(serviceAt + 1 + "_photosense".Length + 1 + "_tcp".Length)]); });
        answer.Record(Pointer(instanceAt), 16, _ => Txt($"id={id}", $"name={name}", "noequals", "=empty"));
        answer.Record(Pointer(targetAt), 1, _ => [1, 2, 3, 4, 5]);
        answer.Record(Name("Other-Device", "local"), 1, _ => [10, 0, 0, 9]);
        answer.Record(Pointer(targetAt), 1, _ => address ?? [192, 168, 1, 20]);
        return answer.Bytes();
    }

    [Test]
    public async Task An_iPhone_Answering_Is_Found_By_Its_Id_And_Name_Where_It_Said_It_Waits()
    {
        var found = MdnsPhoneFinder.Read(IPhoneAnswer(47615), IPAddress.Parse("192.168.1.99")).Single();
        await Assert.That((found.Key, found.Name, found.Via)).IsEqualTo(("wifi:phone-1", "Jamie's iPhone", "wifi"));
    }

    [Test]
    public async Task An_Answer_That_Tells_Less_Is_Read_For_What_It_Tells()
    {
        var answer = new Answer(questions: 1).Question(Name("_photosense", "_tcp", "local"));
        // Another service, an instance with no place to wait, one with too little of one, and one with all but the rest.
        answer.Record(Name("_printer", "_tcp", "local"), 12, _ => Name("Printer", "_printer", "_tcp", "local"));
        answer.Record(Name("_photosense", "_tcp", "local"), 12, _ => Name("Nowhere", "_photosense", "_tcp", "local"));
        answer.Record(Name("_photosense", "_tcp", "local"), 12, _ => Name("Short", "_photosense", "_tcp", "local"));
        answer.Record(Name("Short", "_photosense", "_tcp", "local"), 33, _ => [0, 0, 0]);
        answer.Record(Name("_photosense", "_tcp", "local"), 12, _ => Name("Plain iPad", "_photosense", "_tcp", "local"));
        answer.Record(Name("Someone", "_photosense", "_tcp", "local"), 33, _ => Srv(1, Name("x", "local")));
        answer.Record(Name("Plain iPad", "_photosense", "_tcp", "local"), 33, _ => Srv(47615, Name("ipad", "local")));
        answer.Record(Name("Someone", "_photosense", "_tcp", "local"), 16, _ => Txt("id=someone"));

        var found = MdnsPhoneFinder.Read(answer.Bytes(), IPAddress.Parse("192.168.1.42")).Single();
        await Assert.That((found.Key, found.Name)).IsEqualTo(("wifi:Plain iPad._photosense._tcp.local", "Plain iPad"));
    }

    [Test]
    public async Task What_Is_Not_DNS_Tells_Of_No_Phone()
    {
        await Assert.That(MdnsPhoneFinder.Read([1, 2, 3], IPAddress.Loopback)).IsEmpty();
        // A name that points at itself never ends.
        var looping = new Answer();
        var at = looping.At;
        looping.Record(Pointer(at), 12, _ => []);
        await Assert.That(MdnsPhoneFinder.Read(looping.Bytes(), IPAddress.Loopback)).IsEmpty();
        // A count of records that are not there.
        byte[] cut = [.. new Answer().Bytes()];
        cut[7] = 3;
        await Assert.That(MdnsPhoneFinder.Read(cut, IPAddress.Loopback)).IsEmpty();
    }

    [Test]
    public async Task The_Question_Asks_For_The_Service_To_Be_Answered_Directly()
    {
        byte[] expected = [0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, .. Name("_photosense", "_tcp", "local"), 0, 12, 0x80, 1];
        await Assert.That(MdnsPhoneFinder.Query(MdnsPhoneFinder.Service)).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Phones_Answering_On_The_Network_Are_Found_Once_Each_And_Can_Be_Connected_To()
    {
        var waiting = new TcpListener(IPAddress.Loopback, 0);
        waiting.Start();
        var port = ((IPEndPoint)waiting.LocalEndpoint).Port;
        using var responder = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var asked = new TaskCompletionSource<byte[]>();
        _ = Task.Run(async () =>
        {
            var question = await responder.ReceiveAsync();
            asked.SetResult(question.Buffer);
            var answer = IPhoneAnswer(port, address: [127, 0, 0, 1]);
            // Answered twice, as phones do: it is still one phone.
            await responder.SendAsync(answer, question.RemoteEndPoint);
            await responder.SendAsync(answer, question.RemoteEndPoint);
        });
        try
        {
            var finder = new MdnsPhoneFinder(() => [IPAddress.Loopback, IPAddress.Parse("203.0.113.7")], (IPEndPoint)responder.Client.LocalEndPoint!, TimeSpan.FromMilliseconds(500));
            var found = (await finder.FindAsync(CancellationToken.None)).Single();
            await Assert.That(await asked.Task).IsEquivalentTo(MdnsPhoneFinder.Query(MdnsPhoneFinder.Service), CollectionOrdering.Matching);
            await Assert.That(found.Name).IsEqualTo("Jamie's iPhone");

            var accepting = waiting.AcceptTcpClientAsync();
            await using (var stream = await found.Connect(CancellationToken.None))
                await stream.WriteAsync("hi"u8.ToArray());
            using var phone = await accepting;
            var said = new byte[2];
            await phone.GetStream().ReadExactlyAsync(said);
            await Assert.That(Encoding.ASCII.GetString(said)).IsEqualTo("hi");
        }
        finally { waiting.Stop(); }

        // The phone stopped waiting.
        var gone = MdnsPhoneFinder.Read(IPhoneAnswer(port, address: [127, 0, 0, 1]), IPAddress.Loopback).Single();
        await Assert.ThrowsAsync<SocketException>(() => gone.Connect(CancellationToken.None));
    }

    [Test]
    public async Task Asking_Everyone_Waits_Only_So_Long_And_Stops_When_The_Page_Goes()
    {
        var everyone = new MdnsPhoneFinder(() => [IPAddress.Loopback], new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353), TimeSpan.FromMilliseconds(200));
        await Assert.That((await everyone.FindAsync(CancellationToken.None)).All(p => p.Via == "wifi")).IsTrue();
        var one = new MdnsPhoneFinder(() => [IPAddress.Loopback], new IPEndPoint(IPAddress.Loopback, 9), TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<OperationCanceledException>(() => one.FindAsync(new CancellationToken(true)));
        await Assert.That(new MdnsPhoneFinder()).IsNotNull();
    }

    [Test]
    public async Task It_Asks_From_The_Networks_That_Are_Up_Other_Than_Itself_That_Take_Multicast()
    {
        var home = IPAddress.Parse("192.168.1.10");
        IReadOnlyList<IPAddress> Addresses() => [home, IPAddress.Parse("fe80::1")];
        MdnsPhoneFinder.Network[] networks =
        [
            new(OperationalStatus.Up, NetworkInterfaceType.Wireless80211, true, Addresses),
            new(OperationalStatus.Down, NetworkInterfaceType.Ethernet, true, Addresses),
            new(OperationalStatus.Up, NetworkInterfaceType.Loopback, true, Addresses),
            new(OperationalStatus.Up, NetworkInterfaceType.Tunnel, false, Addresses),
        ];
        await Assert.That(MdnsPhoneFinder.Networks(networks).Select(a => a.ToString())).IsEquivalentTo(["192.168.1.10"], CollectionOrdering.Matching);
        await Assert.That(MdnsPhoneFinder.Networks().All(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))).IsTrue();
    }

    // ---- finding phones on a cable

    private static byte[] Reply(string plist, int? length = null)
    {
        var body = Encoding.UTF8.GetBytes(plist);
        var header = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)(length ?? 16 + body.Length));
        return [.. header, .. body];
    }

    private static string Dict(string inside) => $"<?xml version=\"1.0\"?><!DOCTYPE plist SYSTEM \"x.dtd\"><plist version=\"1.0\"><dict>{inside}</dict></plist>";

    private static string Device(string? id, string? via) => "<dict><key>Properties</key><dict>"
        + (id is null ? "" : $"<key>DeviceID</key><integer>{id}</integer>")
        + (via is null ? "" : $"<key>ConnectionType</key><string>{via}</string>")
        + "</dict></dict>";

    private static UsbmuxPhoneFinder Muxer(params ScriptedStream[] connections)
    {
        var next = 0;
        return new UsbmuxPhoneFinder(_ => Task.FromResult<Stream>(connections[next++]), 47615);
    }

    [Test]
    public async Task Phones_On_A_Cable_Are_Found_Through_Apples_Device_Service_And_Others_Are_Not()
    {
        var list = new ScriptedStream(Reply(Dict("<key>DeviceList</key><array>" + Device("3", "USB") + Device("4", "Network") + Device("x", "USB") + Device("5", null)
            + "<dict><key>DeviceID</key><integer>6</integer></dict></array>")));
        var found = (await Muxer(list).FindAsync(CancellationToken.None)).Single();
        await Assert.That((found.Key, found.Via, found.Name)).IsEqualTo(("usb:3", "usb", "iPhone or iPad on the cable"));
        var asked = list.Written.ToArray();
        await Assert.That((BinaryPrimitives.ReadUInt32LittleEndian(asked), BinaryPrimitives.ReadUInt32LittleEndian(asked.AsSpan(8)))).IsEqualTo(((uint)asked.Length, 8u));
        await Assert.That(Encoding.UTF8.GetString(asked, 16, asked.Length - 16)).Contains("<key>MessageType</key><string>ListDevices</string>");
        await Assert.That(list.Disposed).IsTrue();

        var one = new ScriptedStream(Reply(Dict("<key>DeviceList</key>" + Device("7", "USB"))));
        await Assert.That((await Muxer(one).FindAsync(CancellationToken.None)).Single().Key).IsEqualTo("usb:7");
        var none = new ScriptedStream(Reply(Dict("<key>DeviceList</key><string>none</string>")));
        await Assert.That(await Muxer(none).FindAsync(CancellationToken.None)).IsEmpty();
    }

    [Test]
    public async Task Connecting_Asks_For_The_Phones_Port_And_Is_Then_The_Phone_Itself()
    {
        var connect = new ScriptedStream([.. Reply(Dict("<key>MessageType</key><string>Result</string><key>Number</key><integer>0</integer>")), .. "phone"u8]);
        var found = (await Muxer(new ScriptedStream(Reply(Dict("<key>DeviceList</key>" + Device("3", "USB")))), connect).FindAsync(CancellationToken.None)).Single();
        var stream = await found.Connect(CancellationToken.None);
        await Assert.That(stream).IsSameReferenceAs(connect);
        var said = Encoding.UTF8.GetString(connect.Written.ToArray());
        // The port goes in the order the network writes it.
        await Assert.That(said).Contains($"<key>DeviceID</key><integer>3</integer><key>PortNumber</key><integer>{BinaryPrimitives.ReverseEndianness((ushort)47615)}</integer>");
        var first = new byte[5];
        await stream.ReadExactlyAsync(first);
        await Assert.That(Encoding.ASCII.GetString(first)).IsEqualTo("phone");
    }

    [Test]
    [Arguments("<key>Number</key><integer>3</integer>")]
    [Arguments("<key>MessageType</key><string>Result</string>")]
    public async Task A_Phone_Not_Offering_Photos_Says_What_To_Do(string answer)
    {
        var connect = new ScriptedStream(Reply(Dict(answer)));
        var found = (await Muxer(new ScriptedStream(Reply(Dict("<key>DeviceList</key>" + Device("3", "USB")))), connect).FindAsync(CancellationToken.None)).Single();
        var refused = await Assert.ThrowsAsync<TransferException>(() => found.Connect(CancellationToken.None));
        await Assert.That(refused!.Message).IsEqualTo("PhotoSense is not offering photos on that phone. On the phone, select photos in Organize and choose Send to computer.");
        await Assert.That(connect.Disposed).IsTrue();
    }

    public static IEnumerable<Func<Func<CancellationToken, Task<Stream>>>> DeviceServicesThatDoNotAnswer()
    {
        yield return () => _ => throw new SocketException((int)SocketError.ConnectionRefused);
        yield return () => _ => throw new IOException("The service went away.");
        yield return () => _ => Task.FromResult<Stream>(new ScriptedStream([]));
        yield return () => _ => Task.FromResult<Stream>(new ScriptedStream(Reply("", length: 8)));
        yield return () => _ => Task.FromResult<Stream>(new ScriptedStream(Reply("", length: (1 << 20) + 1)));
        yield return () => _ => Task.FromResult<Stream>(new ScriptedStream(Reply("<not xml")));
        yield return () => _ => Task.FromResult<Stream>(new ScriptedStream(Reply("<plist version=\"1.0\"><array/></plist>")));
    }

    [Test]
    [MethodDataSource(nameof(DeviceServicesThatDoNotAnswer))]
    public async Task No_Device_Service_Or_One_That_Does_Not_Answer_Finds_No_Phone(Func<CancellationToken, Task<Stream>> muxer)
        => await Assert.That(await new UsbmuxPhoneFinder(muxer, 47615).FindAsync(CancellationToken.None)).IsEmpty();

    [Test]
    public async Task The_Device_Service_Is_Where_Apple_Puts_It_On_Each_System()
    {
        await Assert.That(UsbmuxPhoneFinder.Muxer(windows: true)).IsEqualTo(new IPEndPoint(IPAddress.Loopback, 27015));
        await Assert.That(UsbmuxPhoneFinder.Muxer(windows: false).ToString()).IsEqualTo("/var/run/usbmuxd");

        var service = new TcpListener(IPAddress.Loopback, 0);
        service.Start();
        try
        {
            await using var opened = await UsbmuxPhoneFinder.OpenMuxerAsync(service.LocalEndpoint, CancellationToken.None);
            await Assert.That(opened.CanWrite).IsTrue();
        }
        finally { service.Stop(); }
        var nowhere = new UnixDomainSocketEndPoint(Path.Combine(Path.GetTempPath(), "photosense-no-usbmuxd-" + Guid.NewGuid().ToString("N")));
        await Assert.ThrowsAsync<SocketException>(() => UsbmuxPhoneFinder.OpenMuxerAsync(nowhere, CancellationToken.None));

        // This computer's own: whatever is plugged in, only phones on a cable are found.
        await Assert.That((await new UsbmuxPhoneFinder().FindAsync(CancellationToken.None)).All(p => p.Via == "usb")).IsTrue();
    }

    [Test]
    public async Task Every_Way_Of_Finding_Phones_Is_Asked_At_Once()
    {
        var wifi = new FoundPhone("wifi:1", "Phone", "wifi", _ => Task.FromResult<Stream>(new MemoryStream()));
        var usb = new FoundPhone("usb:3", "iPhone or iPad on the cable", "usb", _ => Task.FromResult<Stream>(new MemoryStream()));
        var finders = new PhoneFinders(new Finding(wifi), new Finding(usb), new Finding());
        await Assert.That((await finders.FindAsync(CancellationToken.None)).Select(p => p.Key)).IsEquivalentTo(["wifi:1", "usb:3"], CollectionOrdering.Matching);
    }

    private sealed class Finding(params FoundPhone[] phones) : IPhoneFinder
    {
        public Task<IReadOnlyList<FoundPhone>> FindAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<FoundPhone>>(phones);
    }
}
