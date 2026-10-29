using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Transfer;

/// <summary>
/// Finds the phones offering photos on the networks this computer is on, as Bonjour does (multicast DNS): it asks
/// once, from each network, for the PhotoSense service, and the phones answer it directly. Nothing has to be
/// installed for this, and a phone is found only while it is offering photos.
/// </summary>
public sealed class MdnsPhoneFinder : IPhoneFinder
{
    /// <summary>What a phone offering photos is known by on the network.</summary>
    public const string Service = "_photosense._tcp.local";

    private readonly Func<IReadOnlyList<IPAddress>> _from;
    private readonly IPEndPoint _to;
    private readonly bool _everyone;
    private readonly TimeSpan _listen;

    public MdnsPhoneFinder() : this(Networks, new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353), TimeSpan.FromSeconds(1.5)) { }

    /// <param name="from">This computer's addresses to ask from, one on each network.</param>
    /// <param name="to">Where to ask: the multicast address everyone listens on.</param>
    /// <param name="listen">How long answers are waited for.</param>
    public MdnsPhoneFinder(Func<IReadOnlyList<IPAddress>> from, IPEndPoint to, TimeSpan listen)
    {
        _from = from;
        _to = to;
        // 224 to 239: an address everyone on the network listens on, asked from one network at a time.
        _everyone = (to.Address.GetAddressBytes()[0] & 0xF0) == 0xE0;
        _listen = listen;
    }

    public async Task<IReadOnlyList<FoundPhone>> FindAsync(CancellationToken ct)
    {
        var query = Query(Service);
        var heard = await Task.WhenAll(_from().Select(address => AskAsync(address, query, ct)));
        var found = new Dictionary<string, FoundPhone>();
        foreach (var (answer, sender) in heard.SelectMany(h => h))
            foreach (var phone in Read(answer, sender)) found.TryAdd(phone.Key, phone);
        return [.. found.Values];
    }

    /// <summary>This computer's IPv4 addresses on the networks that are up, other than itself.</summary>
    public static IReadOnlyList<IPAddress> Networks()
        => Networks(NetworkInterface.GetAllNetworkInterfaces().Select(n => new Network(n.OperationalStatus, n.NetworkInterfaceType, n.SupportsMulticast, () => [.. n.GetIPProperties().UnicastAddresses.Select(a => a.Address)])));

    /// <summary>A network this computer is on, as far as asking on it goes.</summary>
    public sealed record Network(OperationalStatus Status, NetworkInterfaceType Type, bool Multicast, Func<IReadOnlyList<IPAddress>> Addresses);

    public static IReadOnlyList<IPAddress> Networks(IEnumerable<Network> networks)
        => [.. networks.Where(n => n.Status == OperationalStatus.Up && n.Type != NetworkInterfaceType.Loopback && n.Multicast)
            .SelectMany(n => n.Addresses()).Where(a => a.AddressFamily == AddressFamily.InterNetwork)];

    private async Task<List<(byte[] Answer, IPAddress Sender)>> AskAsync(IPAddress from, byte[] query, CancellationToken ct)
    {
        var heard = new List<(byte[], IPAddress)>();
        try
        {
            using var udp = new UdpClient(new IPEndPoint(from, 0));
            if (_everyone) udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, from.GetAddressBytes());
            await udp.SendAsync(query, _to, ct);
            using var listening = CancellationTokenSource.CreateLinkedTokenSource(ct);
            listening.CancelAfter(_listen);
            while (true)
            {
                var got = await udp.ReceiveAsync(listening.Token);
                heard.Add((got.Buffer, got.RemoteEndPoint.Address));
            }
        }
        // Listened for long enough; or a network that cannot be asked on.
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        catch (SocketException) { }
        return heard;
    }

    /// <summary>A question for the instances of a service, to be answered directly rather than to everyone.</summary>
    public static byte[] Query(string service) => [0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, .. Name(service), 0, 12, 0x80, 1];

    private static byte[] Name(string name) => [.. name.Split('.').SelectMany(label => (byte[])[(byte)Encoding.UTF8.GetByteCount(label), .. Encoding.UTF8.GetBytes(label)]), 0];

    /// <summary>The phones an answer tells of: each instance of the service, where it waits, and what it is called.</summary>
    public static IReadOnlyList<FoundPhone> Read(byte[] answer, IPAddress sender)
    {
        try { return Phones(answer, sender); }
        // Something on the network answered with what is not DNS: it tells of no phone.
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException) { return []; }
    }

    private static List<FoundPhone> Phones(byte[] answer, IPAddress sender)
    {
        var records = new List<(string Owner, int Type, byte[] Data, int At)>();
        var questions = BinaryPrimitives.ReadUInt16BigEndian(answer.AsSpan(4));
        var count = BinaryPrimitives.ReadUInt16BigEndian(answer.AsSpan(6)) + BinaryPrimitives.ReadUInt16BigEndian(answer.AsSpan(8)) + BinaryPrimitives.ReadUInt16BigEndian(answer.AsSpan(10));
        var at = 12;
        for (var i = 0; i < questions; i++) { ReadName(answer, ref at); at += 4; }
        for (var i = 0; i < count; i++)
        {
            var owner = ReadName(answer, ref at);
            var type = BinaryPrimitives.ReadUInt16BigEndian(answer.AsSpan(at));
            var length = BinaryPrimitives.ReadUInt16BigEndian(answer.AsSpan(at + 8));
            records.Add((owner, type, answer[(at + 10)..(at + 10 + length)], at + 10));
            at += 10 + length;
        }

        var phones = new List<FoundPhone>();
        foreach (var (_, _, _, ptrAt) in records.Where(r => r.Type == 12 && Same(r.Owner, Service)))
        {
            var position = ptrAt;
            var instance = ReadName(answer, ref position);
            if (records.FirstOrDefault(r => r.Type == 33 && Same(r.Owner, instance)) is not { Data: { Length: >= 7 } srv } service) continue;
            var port = BinaryPrimitives.ReadUInt16BigEndian(srv.AsSpan(4));
            var targetAt = service.At + 6;
            var target = ReadName(answer, ref targetAt);
            var address = records.FirstOrDefault(r => r.Type == 1 && r.Data.Length == 4 && Same(r.Owner, target)).Data is { } ip ? new IPAddress(ip) : sender;
            var text = Text(records.FirstOrDefault(r => r.Type == 16 && Same(r.Owner, instance)).Data);
            var name = text.GetValueOrDefault("name") ?? instance.Split('.')[0];
            phones.Add(new FoundPhone($"wifi:{text.GetValueOrDefault("id") ?? instance}", name, "wifi", ct => ConnectAsync(address, port, ct)));
        }
        return phones;
    }

    private static async Task<Stream> ConnectAsync(IPAddress address, int port, CancellationToken ct)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(address, port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static bool Same(string a, string b) => string.Equals(a.TrimEnd('.'), b.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);

    // A name, following the pointers by which DNS says a name, or its end, again.
    private static string ReadName(byte[] message, ref int at)
    {
        var labels = new List<string>();
        var position = at;
        var jumped = false;
        for (var hops = 0; ; hops++)
        {
            if (hops > 64) throw new ArgumentException("A name that never ends.");
            var length = message[position];
            if (length == 0)
            {
                if (!jumped) at = position + 1;
                return string.Join('.', labels);
            }
            if ((length & 0xC0) == 0xC0)
            {
                if (!jumped) at = position + 2;
                jumped = true;
                position = ((length & 0x3F) << 8) | message[position + 1];
                continue;
            }
            labels.Add(Encoding.UTF8.GetString(message, position + 1, length));
            position += 1 + length;
        }
    }

    private static Dictionary<string, string> Text(byte[]? data)
    {
        var text = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var at = 0; data is not null && at < data.Length; at += 1 + data[at])
        {
            var entry = Encoding.UTF8.GetString(data, at + 1, Math.Min(data[at], data.Length - at - 1));
            var equals = entry.IndexOf('=');
            if (equals > 0) text[entry[..equals]] = entry[(equals + 1)..];
        }
        return text;
    }
}
