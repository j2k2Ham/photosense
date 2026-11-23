using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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
    private readonly TimeSpan _listen;

    /// <summary>Asks from every network this computer is on, and listens a second and a half for answers.</summary>
    public MdnsPhoneFinder() : this(Networks, Mdns.Everyone, TimeSpan.FromSeconds(1.5)) { }

    /// <summary>Asks from the addresses given, to the address given, and listens as long as given.</summary>
    /// <param name="from">This computer's addresses to ask from, one on each network.</param>
    /// <param name="to">Where to ask: the multicast address everyone listens on.</param>
    /// <param name="listen">How long answers are waited for.</param>
    public MdnsPhoneFinder(Func<IReadOnlyList<IPAddress>> from, IPEndPoint to, TimeSpan listen)
    {
        _from = from;
        _to = to;
        _listen = listen;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FoundPhone>> FindAsync(CancellationToken ct)
    {
        var query = Query(Service);
        var heard = await Task.WhenAll(_from().Select(address => Mdns.AskAsync(address, _to, query, _listen, ct)));
        var found = new Dictionary<string, FoundPhone>();
        foreach (var (answer, sender) in heard.SelectMany(h => h))
            foreach (var phone in Read(answer, sender)) found.TryAdd(phone.Key, phone);
        return [.. found.Values];
    }

    /// <summary>This device's IPv4 addresses on the networks that are up, other than itself.</summary>
    public static IReadOnlyList<IPAddress> Networks()
        => Networks(NetworkInterface.GetAllNetworkInterfaces().Select(n => new Network(n.OperationalStatus, n.NetworkInterfaceType, n.SupportsMulticast, () => [.. n.GetIPProperties().UnicastAddresses.Select(a => a.Address)])));

    /// <summary>A network this device is on, as far as asking on it goes.</summary>
    /// <param name="Status">Whether it is up.</param>
    /// <param name="Type">What kind of network it is; this device's own loopback is not asked on.</param>
    /// <param name="Multicast">Whether it can ask everyone on it at once.</param>
    /// <param name="Addresses">This device's addresses on it.</param>
    public sealed record Network(OperationalStatus Status, NetworkInterfaceType Type, bool Multicast, Func<IReadOnlyList<IPAddress>> Addresses);

    /// <summary>The IPv4 addresses to ask from, on those of the networks that are up and can ask everyone at once, other than loopback.</summary>
    public static IReadOnlyList<IPAddress> Networks(IEnumerable<Network> networks)
        => [.. networks.Where(n => n.Status == OperationalStatus.Up && n.Type != NetworkInterfaceType.Loopback && n.Multicast)
            .SelectMany(n => n.Addresses()).Where(a => a.AddressFamily == AddressFamily.InterNetwork)];

    /// <summary>A question for the instances of a service, to be answered directly rather than to everyone.</summary>
    public static byte[] Query(string service) => Mdns.Query(service);

    /// <summary>The phones an answer tells of: each instance of the service, where it waits, and what it is called.</summary>
    public static IReadOnlyList<FoundPhone> Read(byte[] answer, IPAddress sender)
        => [.. Mdns.Instances(answer, sender, Service).Select(i => new FoundPhone($"wifi:{i.Id}", i.Name, "wifi", ct => Mdns.ConnectAsync(i.Address, i.Port, ct)))];
}

/// <summary>
/// Finds the computers with PhotoSense on the networks this phone is on, as <see cref="MdnsPhoneFinder"/> finds phones:
/// it asks once from each network, and the computers answer it directly. Used where the phone may ask the network
/// itself, as Android may; an iPhone asks through the system's own Bonjour.
/// </summary>
public sealed class MdnsComputerFinder : IComputerFinder
{
    /// <summary>What a computer waiting for phones is known by on the network.</summary>
    public const string Service = "_photosense-pc._tcp.local";

    private readonly Func<IReadOnlyList<IPAddress>> _from;
    private readonly IPEndPoint _to;
    private readonly TimeSpan _listen;

    /// <summary>Asks from every network this phone is on, and listens a second and a half for answers.</summary>
    public MdnsComputerFinder() : this(MdnsPhoneFinder.Networks, Mdns.Everyone, TimeSpan.FromSeconds(1.5)) { }

    /// <summary>Asks from the addresses given, to the address given, and listens as long as given.</summary>
    /// <param name="from">This phone's addresses to ask from, one on each network.</param>
    /// <param name="to">Where to ask: the multicast address everyone listens on.</param>
    /// <param name="listen">How long answers are waited for.</param>
    public MdnsComputerFinder(Func<IReadOnlyList<IPAddress>> from, IPEndPoint to, TimeSpan listen)
    {
        _from = from;
        _to = to;
        _listen = listen;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FoundComputer>> FindAsync(CancellationToken ct)
    {
        var query = Mdns.Query(Service);
        var heard = await Task.WhenAll(_from().Select(address => Mdns.AskAsync(address, _to, query, _listen, ct)));
        var found = new Dictionary<string, FoundComputer>();
        foreach (var (answer, sender) in heard.SelectMany(h => h))
            foreach (var i in Mdns.Instances(answer, sender, Service)) found.TryAdd(i.Id, new FoundComputer(i.Id, i.Name, token => Mdns.ConnectAsync(i.Address, i.Port, token)));
        return [.. found.Values];
    }
}

/// <summary>Connects to a computer at the addresses its QR code gave, trying each for a few seconds.</summary>
/// <param name="wait">How long an address is tried before the next.</param>
public sealed class TcpComputerConnector(TimeSpan wait) : IComputerConnector
{
    /// <summary>Tries each address for five seconds.</summary>
    public TcpComputerConnector() : this(TimeSpan.FromSeconds(5)) { }

    /// <inheritdoc />
    public async Task<Stream> ConnectAsync(IReadOnlyList<string> hosts, int port, CancellationToken ct)
    {
        foreach (var host in hosts)
        {
            if (!IPAddress.TryParse(host, out var address)) continue;
            using var trying = CancellationTokenSource.CreateLinkedTokenSource(ct);
            trying.CancelAfter(wait);
            try { return await Mdns.ConnectAsync(address, port, trying.Token); }
            // That address did not answer in time, or turned the connection away: the next is tried.
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (SocketException) { }
        }
        throw new IOException("The computer could not be reached. Check that both are on the same Wi-Fi, and try again.");
    }
}
