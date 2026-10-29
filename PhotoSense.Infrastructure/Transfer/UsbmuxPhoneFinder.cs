using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using PhotoSense.Application.Transfer;
using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Transfer;

/// <summary>
/// Finds the iPhones and iPads plugged into this computer with a cable, through Apple's own device service (usbmux):
/// on Windows the Apple Mobile Device Service that iTunes and Apple Devices install, on a Mac part of the system. It
/// connects to a phone through the cable, to the port the phone waits on.
/// </summary>
public sealed class UsbmuxPhoneFinder : IPhoneFinder
{
    private const int Plist = 8;
    private readonly Func<CancellationToken, Task<Stream>> _muxer;
    private readonly int _port;

    public UsbmuxPhoneFinder() : this(ct => OpenMuxerAsync(Muxer(OperatingSystem.IsWindows()), ct), TcpTransferListener.Port) { }

    /// <param name="muxer">A connection to the device service.</param>
    /// <param name="port">The port a phone waits on.</param>
    public UsbmuxPhoneFinder(Func<CancellationToken, Task<Stream>> muxer, int port)
    {
        _muxer = muxer;
        _port = port;
    }

    public async Task<IReadOnlyList<FoundPhone>> FindAsync(CancellationToken ct)
    {
        XElement answer;
        try
        {
            await using var muxer = await _muxer(ct);
            answer = await AskAsync(muxer, "ListDevices", [], ct);
        }
        // No device service on this computer, or one that will not answer: no phone on a cable can be reached.
        catch (Exception ex) when (ex is IOException or SocketException or XmlException or TransferException) { return []; }
        return [.. Entries(answer, "DeviceList")
            .Select(d => Entries(d, "Properties").SingleOrDefault() is { } p ? (Id: Number(Value(p, "DeviceID")), Via: Value(p, "ConnectionType")?.Value) : default)
            .Where(d => d.Via == "USB" && d.Id is not null)
            .Select(d => new FoundPhone($"usb:{d.Id}", "iPhone or iPad on the cable", "usb", ct => ConnectAsync(d.Id!.Value, ct)))];
    }

    private async Task<Stream> ConnectAsync(long device, CancellationToken ct)
    {
        var muxer = await _muxer(ct);
        try
        {
            // The port goes in the order the network writes it.
            var answer = await AskAsync(muxer, "Connect", [("DeviceID", device), ("PortNumber", (long)BinaryPrimitives.ReverseEndianness((ushort)_port))], ct);
            if (Number(Value(answer, "Number")) != 0)
                throw new TransferException("PhotoSense is not offering photos on that phone. On the phone, select photos in Organize and choose Send to computer.");
            // From here on the connection is to the phone itself.
            return muxer;
        }
        catch
        {
            await muxer.DisposeAsync();
            throw;
        }
    }

    /// <summary>Where the device service listens: on Windows this computer's own port 27015, on a Mac a socket of the system's.</summary>
    public static EndPoint Muxer(bool windows) => windows ? new IPEndPoint(IPAddress.Loopback, 27015) : new UnixDomainSocketEndPoint("/var/run/usbmuxd");

    public static async Task<Stream> OpenMuxerAsync(EndPoint at, CancellationToken ct)
    {
        var socket = new Socket(at.AddressFamily, SocketType.Stream, at is UnixDomainSocketEndPoint ? ProtocolType.Unspecified : ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(at, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Sends the service a message, a property list, and returns its answer.</summary>
    public static async Task<XElement> AskAsync(Stream muxer, string kind, IReadOnlyList<(string Key, object Value)> values, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(PropertyList([("MessageType", kind), ("ClientVersionString", "PhotoSense"), ("ProgName", "PhotoSense"), .. values]));
        var header = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)(16 + body.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), Plist);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 1);
        await muxer.WriteAsync(header, ct);
        await muxer.WriteAsync(body, ct);
        await muxer.FlushAsync(ct);

        try
        {
            await muxer.ReadExactlyAsync(header, ct);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
            if (length is < 16 or > 1 << 20) throw new TransferException("Apple's device service answered with what PhotoSense does not understand.");
            var reply = new byte[length - 16];
            await muxer.ReadExactlyAsync(reply, ct);
            using var reader = XmlReader.Create(new MemoryStream(reply), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            return XDocument.Load(reader).Root!.Element("dict") ?? throw new TransferException("Apple's device service answered with what PhotoSense does not understand.");
        }
        catch (EndOfStreamException) { throw new TransferException("Apple's device service stopped answering."); }
    }

    /// <summary>A property list of a dictionary of strings and whole numbers, as the service takes it.</summary>
    public static string PropertyList(IEnumerable<(string Key, object Value)> values)
    {
        var entries = values.Select(v => $"<key>{SecurityElement.Escape(v.Key)}</key>" + (v.Value is long n ? $"<integer>{n}</integer>" : $"<string>{SecurityElement.Escape($"{v.Value}")}</string>"));
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n"
            + $"<plist version=\"1.0\"><dict>{string.Concat(entries)}</dict></plist>";
    }

    // The value under a key of a property-list dictionary: the element after the key.
    private static XElement? Value(XElement dict, string key) => dict.Elements("key").FirstOrDefault(k => k.Value == key)?.ElementsAfterSelf().FirstOrDefault();

    private static long? Number(XElement? value) => long.TryParse(value?.Value, out var n) ? n : null;

    // The dictionaries under a key: the array of them there, or the one dictionary.
    private static IEnumerable<XElement> Entries(XElement dict, string key) => Value(dict, key) switch
    {
        { Name.LocalName: "array" } array => array.Elements("dict"),
        { Name.LocalName: "dict" } one => [one],
        _ => [],
    };
}
