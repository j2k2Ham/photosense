using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using PhotoSense.Domain.Services;

namespace PhotoSense.iOS;

/// <summary>
/// Tells the computers on the Wi-Fi, with Bonjour, that this phone is offering photos and where it waits, through the
/// system's own DNS service. While it does, the screen stays on: a locked phone stops the app, and the sending with it.
/// </summary>
public sealed class BonjourAdvertiser : ITransferAdvertiser
{
    private const string Library = "/usr/lib/libSystem.dylib";
    private readonly object _lock = new();
    private IntPtr _registered;

    public void Start(string id, string name, int port)
    {
        Stop();
        var text = Text(("id", id), ("name", name));
        // With no callback the system registers it by itself, renaming it should another device have the name.
        var error = DNSServiceRegister(out var registered, 0, 0, name, "_photosense._tcp", null, null, (ushort)IPAddress.HostToNetworkOrder((short)port), (ushort)text.Length, text, IntPtr.Zero, IntPtr.Zero);
        if (error != 0) throw new IOException($"The phone could not tell the network it is offering photos (error {error}).");
        lock (_lock) _registered = registered;
        UIApplication.SharedApplication.InvokeOnMainThread(() => UIApplication.SharedApplication.IdleTimerDisabled = true);
    }

    public void Stop()
    {
        IntPtr registered;
        lock (_lock) (registered, _registered) = (_registered, IntPtr.Zero);
        if (registered == IntPtr.Zero) return;
        DNSServiceRefDeallocate(registered);
        UIApplication.SharedApplication.InvokeOnMainThread(() => UIApplication.SharedApplication.IdleTimerDisabled = false);
    }

    // A TXT record: each entry its length in a byte, then "key=value".
    private static byte[] Text(params (string Key, string Value)[] entries)
        => [.. entries.SelectMany(e =>
        {
            var entry = Encoding.UTF8.GetBytes($"{e.Key}={e.Value}");
            return (byte[])[(byte)Math.Min(entry.Length, 255), .. entry.Take(255)];
        })];

    [DllImport(Library)]
    private static extern int DNSServiceRegister(out IntPtr sdRef, uint flags, uint interfaceIndex, string name, string regtype, string? domain, string? host,
        ushort port, ushort txtLen, byte[] txtRecord, IntPtr callBack, IntPtr context);

    [DllImport(Library)]
    private static extern void DNSServiceRefDeallocate(IntPtr sdRef);
}
