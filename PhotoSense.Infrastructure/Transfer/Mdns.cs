using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PhotoSense.Infrastructure.Transfer;

/// <summary>An instance of a service an mDNS answer tells of: its id and name from its text, and where it waits.</summary>
/// <param name="Id">The id its text gives, or its instance name.</param>
/// <param name="Name">The name its text gives, or the first label of its instance name.</param>
/// <param name="Address">Where it is: the address the answer gives, or the one that sent it.</param>
/// <param name="Port">The port it waits on.</param>
public sealed record MdnsInstance(string Id, string Name, IPAddress Address, int Port);

/// <summary>Multicast DNS as PhotoSense uses it: asking for a service's instances, reading the answers, and writing them.</summary>
public static class Mdns
{
    /// <summary>Where everyone on a network listens for multicast DNS.</summary>
    public static readonly IPEndPoint Everyone = new(IPAddress.Parse("224.0.0.251"), 5353);

    /// <summary>A question for the instances of a service, to be answered directly rather than to everyone.</summary>
    public static byte[] Query(string service) => [0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, .. Name(service), 0, 12, 0x80, 1];

    /// <summary>A name as DNS writes it: each label after its length, then a zero.</summary>
    public static byte[] Name(string name) => [.. name.Split('.').SelectMany(label => (byte[])[(byte)Encoding.UTF8.GetByteCount(label), .. Encoding.UTF8.GetBytes(label)]), 0];

    /// <summary>Asks once from one of this device's addresses, and gathers the answers heard until the time is up.</summary>
    /// <param name="from">The address to ask from.</param>
    /// <param name="to">Where to ask; a multicast address asks everyone on the network of <paramref name="from"/>.</param>
    /// <param name="query">The question.</param>
    /// <param name="listen">How long answers are waited for.</param>
    /// <param name="ct">Stops the asking; what was heard by then is not wanted.</param>
    public static async Task<List<(byte[] Answer, IPAddress Sender)>> AskAsync(IPAddress from, IPEndPoint to, byte[] query, TimeSpan listen, CancellationToken ct)
    {
        var heard = new List<(byte[], IPAddress)>();
        try
        {
            using var udp = new UdpClient(new IPEndPoint(from, 0));
            // 224 to 239: an address everyone on the network listens on, asked from one network at a time.
            if ((to.Address.GetAddressBytes()[0] & 0xF0) == 0xE0) udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, from.GetAddressBytes());
            await udp.SendAsync(query, to, ct);
            using var listening = CancellationTokenSource.CreateLinkedTokenSource(ct);
            listening.CancelAfter(listen);
            // Waits for answers until the time is up, which ends the waiting as a cancellation.
            while (true)
            {
                var got = await udp.ReceiveAsync(listening.Token);
                heard.Add((got.Buffer, got.RemoteEndPoint.Address));
            }
        }
        // Listened for long enough: what was heard is all there is.
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return heard; }
        // A network that cannot be asked on tells of nothing beyond what was already heard.
        catch (SocketException) { return heard; }
    }

    /// <summary>The instances of <paramref name="service"/> an answer tells of; none when it is not DNS.</summary>
    public static IReadOnlyList<MdnsInstance> Instances(byte[] answer, IPAddress sender, string service)
    {
        try { return Read(answer, sender, service); }
        // Something on the network answered with what is not DNS: it tells of nothing.
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException) { return []; }
    }

    private static List<MdnsInstance> Read(byte[] answer, IPAddress sender, string service)
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

        var found = new List<MdnsInstance>();
        foreach (var pointer in records)
        {
            if (pointer.Type != 12 || !Same(pointer.Owner, service)) continue;
            var position = pointer.At;
            var instance = ReadName(answer, ref position);
            if (records.FirstOrDefault(r => r.Type == 33 && Same(r.Owner, instance)) is not { Data: { Length: >= 7 } srv } srvRecord) continue;
            var port = BinaryPrimitives.ReadUInt16BigEndian(srv.AsSpan(4));
            var targetAt = srvRecord.At + 6;
            var target = ReadName(answer, ref targetAt);
            var address = records.FirstOrDefault(r => r.Type == 1 && r.Data.Length == 4 && Same(r.Owner, target)).Data is { } ip ? new IPAddress(ip) : sender;
            var text = Text(records.FirstOrDefault(r => r.Type == 16 && Same(r.Owner, instance)).Data);
            found.Add(new MdnsInstance(text.GetValueOrDefault("id") ?? instance, text.GetValueOrDefault("name") ?? instance.Split('.')[0], address, port));
        }
        return found;
    }

    /// <summary>Whether two names are the same, as DNS compares them.</summary>
    public static bool Same(string a, string b) => string.Equals(a.TrimEnd('.'), b.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);

    /// <summary>A name, following the pointers by which DNS says a name, or its end, again.</summary>
    /// <exception cref="ArgumentException">A name that never ends.</exception>
    public static string ReadName(byte[] message, ref int at)
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

    /// <summary>A connection to a port at an address.</summary>
    public static async Task<Stream> ConnectAsync(IPAddress address, int port, CancellationToken ct)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        var handedOver = false;
        try
        {
            await socket.ConnectAsync(address, port, ct);
            var stream = new NetworkStream(socket, ownsSocket: true);
            handedOver = true;
            return stream;
        }
        finally
        {
            // Once handed over the stream owns the socket; a connection that failed lets go of it here.
            if (!handedOver) socket.Dispose();
        }
    }
}
