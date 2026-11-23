using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Transfer;

/// <summary>Where multicast DNS is heard and answered: questions from the network, and answers back.</summary>
public interface IMdnsSocket : IDisposable
{
    /// <summary>The next message heard, and who sent it.</summary>
    Task<(byte[] Message, IPEndPoint From)> ReceiveAsync(CancellationToken ct);

    /// <summary>Sends a message to an address: the asker, or everyone.</summary>
    Task SendAsync(byte[] message, IPEndPoint to, CancellationToken ct);
}

/// <summary>
/// Multicast DNS on the port everyone uses, shared with the system's own responder, listening on every network this
/// computer is on. A network that cannot be listened on is left out; the others still are.
/// </summary>
public sealed class UdpMdnsSocket : IMdnsSocket
{
    private readonly UdpClient _udp;

    /// <summary>Listens on <paramref name="port"/>, 5353 unless a test gives its own, on the networks of <paramref name="addresses"/>.</summary>
    public UdpMdnsSocket(int port, IReadOnlyList<IPAddress> addresses)
    {
        _udp = new UdpClient(AddressFamily.InterNetwork);
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
        foreach (var address in addresses)
        {
            try { _udp.JoinMulticastGroup(Mdns.Everyone.Address, address); }
            // A network that cannot be joined is not listened on; questions asked directly still arrive.
            catch (SocketException) { }
        }
    }

    /// <inheritdoc />
    public async Task<(byte[] Message, IPEndPoint From)> ReceiveAsync(CancellationToken ct)
    {
        var got = await _udp.ReceiveAsync(ct);
        return (got.Buffer, got.RemoteEndPoint);
    }

    /// <inheritdoc />
    public async Task SendAsync(byte[] message, IPEndPoint to, CancellationToken ct) => await _udp.SendAsync(message, to, ct);

    /// <inheritdoc />
    public void Dispose() => _udp.Dispose();
}

/// <summary>
/// Tells the phones on the network that this computer is waiting for them, as Bonjour does: it answers the questions
/// for <see cref="MdnsComputerFinder.Service"/>, with where the computer waits and its id and name, and says so once as
/// it starts. The computer answers for itself, so nothing has to be installed for it.
/// </summary>
public sealed class MdnsAdvertiser : ITransferAdvertiser
{
    private readonly Func<IMdnsSocket> _open;
    private readonly Func<IReadOnlyList<IPAddress>> _addresses;
    private readonly object _lock = new();
    private CancellationTokenSource? _stop;
    private IMdnsSocket? _socket;

    /// <summary>Answers on port 5353 on every network this computer is on.</summary>
    public MdnsAdvertiser() : this(() => new UdpMdnsSocket(Mdns.Everyone.Port, MdnsPhoneFinder.Networks()), MdnsPhoneFinder.Networks) { }

    /// <summary>Answers through the socket <paramref name="open"/> makes, giving the addresses <paramref name="addresses"/> lists.</summary>
    /// <param name="open">Makes the socket questions are heard and answered on.</param>
    /// <param name="addresses">This computer's addresses, which the answers give.</param>
    public MdnsAdvertiser(Func<IMdnsSocket> open, Func<IReadOnlyList<IPAddress>> addresses)
    {
        _open = open;
        _addresses = addresses;
    }

    /// <summary>The task answering questions, for whoever waits for it to end.</summary>
    public Task Answering { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Starts telling the network that this computer waits for phones. Where the port cannot be had, as when the system's
    /// own responder holds it and will not share, nothing is said: the computer still waits, and a phone that scans its QR
    /// code still finds it by the addresses the code carries.
    /// </summary>
    public void Start(string id, string name, int port)
    {
        lock (_lock)
        {
            Stop();
            if (!TryOpen(out var socket)) return;
            var stop = new CancellationTokenSource();
            _socket = socket;
            _stop = stop;
            var answer = Answer(id, name, port, _addresses());
            Answering = Task.Run(() => AnswerAsync(socket, answer, id, stop.Token), CancellationToken.None);
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (_lock)
        {
            _stop?.Cancel();
            _socket?.Dispose();
            _stop = null;
            _socket = null;
        }
    }

    private bool TryOpen(out IMdnsSocket socket)
    {
        try
        {
            socket = _open();
            return true;
        }
        catch (SocketException)
        {
            socket = null!;
            return false;
        }
    }

    // Sends one message; one that cannot go, as to everyone on a machine with no route for it, is no reason to stop answering.
    private static async Task<bool> TrySendAsync(IMdnsSocket socket, byte[] message, IPEndPoint to, CancellationToken stop)
    {
        try
        {
            await socket.SendAsync(message, to, stop);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    // Says it is there once, then answers each question about it until stopped.
    private static async Task AnswerAsync(IMdnsSocket socket, byte[] answer, string id, CancellationToken stop)
    {
        try
        {
            await TrySendAsync(socket, answer, Mdns.Everyone, stop);
            while (true)
            {
                var (question, from) = await socket.ReceiveAsync(stop);
                if (Asks(question, id) is not { } direct) continue;
                // Asked to be answered directly, or from a port other than mDNS's own, it is answered to the asker; otherwise to everyone.
                await TrySendAsync(socket, answer, direct || from.Port != Mdns.Everyone.Port ? from : Mdns.Everyone, stop);
            }
        }
        // Stopped, or the socket was closed by Stop: there is no one left to answer.
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (stop.IsCancellationRequested) { }
        catch (SocketException) when (stop.IsCancellationRequested) { }
    }

    /// <summary>
    /// Whether a message asks about this computer, by the service, its instance or its host's name: null when it does
    /// not, true when the asker wants the answer sent to it directly. A message that is not DNS asks nothing.
    /// </summary>
    public static bool? Asks(byte[] message, string id)
    {
        try
        {
            if (message.Length < 12 || (message[2] & 0x80) != 0) return null;
            var questions = BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(4));
            var at = 12;
            bool? asked = null;
            for (var i = 0; i < questions; i++)
            {
                var name = Mdns.ReadName(message, ref at);
                var direct = (message[at + 2] & 0x80) != 0;
                at += 4;
                if (Mdns.Same(name, MdnsComputerFinder.Service) || Mdns.Same(name, Instance(id)) || Mdns.Same(name, Host(id))) asked = (asked ?? false) || direct;
            }
            return asked;
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException) { return null; }
    }

    private static string Instance(string id) => $"{id}.{MdnsComputerFinder.Service}";

    private static string Host(string id) => $"photosense-{id}.local";

    /// <summary>
    /// The answer about this computer: the service's instance, where it waits (port and host), its id and name, and the
    /// host's addresses, each kept for two minutes.
    /// </summary>
    public static byte[] Answer(string id, string name, int port, IReadOnlyList<IPAddress> addresses)
    {
        var records = new List<byte[]>
        {
            Record(MdnsComputerFinder.Service, 12, Mdns.Name(Instance(id))),
            Record(Instance(id), 33, [0, 0, 0, 0, (byte)(port >> 8), (byte)port, .. Mdns.Name(Host(id))]),
            Record(Instance(id), 16, [.. Text($"id={id}"), .. Text($"name={name}")]),
        };
        records.AddRange(addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => Record(Host(id), 1, a.GetAddressBytes())));
        var count = (ushort)records.Count;
        return [0, 0, 0x84, 0, 0, 0, (byte)(count >> 8), (byte)count, 0, 0, 0, 0, .. records.SelectMany(r => r)];
    }

    private static byte[] Text(string entry)
    {
        var bytes = Encoding.UTF8.GetBytes(entry);
        return [(byte)Math.Min(bytes.Length, 255), .. bytes.Take(255)];
    }

    // A record of the answer: its owner, type, class (flushing what was kept before) and time to keep it, then its data.
    private static byte[] Record(string owner, ushort type, byte[] data)
        => [.. Mdns.Name(owner), (byte)(type >> 8), (byte)type, 0x80, 1, 0, 0, 0, 120, (byte)(data.Length >> 8), (byte)data.Length, .. data];
}
