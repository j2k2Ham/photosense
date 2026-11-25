using System.Net;
using Trovolve.Domain.Services;
using Trovolve.Infrastructure.Transfer;

namespace Trovolve.iOS;

/// <summary>
/// Finds the computers with Trovolve on the iPhone's Wi-Fi through the system's own Bonjour, as an app may not send
/// multicast DNS itself: it browses for a second and a half, then asks each computer found where it waits, and its id
/// and name, for up to three seconds.
/// </summary>
public sealed class BonjourComputerFinder : IComputerFinder
{
    private static readonly TimeSpan Browse = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan Resolve = TimeSpan.FromSeconds(3);

    /// <inheritdoc />
    public async Task<IReadOnlyList<FoundComputer>> FindAsync(CancellationToken ct)
    {
        var services = new List<NSNetService>();
        NSNetServiceBrowser? browser = null;
        // Bonjour's browser and services answer on the main thread's run loop.
        UIApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            browser = new NSNetServiceBrowser();
            browser.FoundService += (_, e) => services.Add(e.Service);
            browser.SearchForServices("_trovolve-pc._tcp.", "local.");
        });
        try { await Task.Delay(Browse, ct); }
        finally { UIApplication.SharedApplication.InvokeOnMainThread(() => browser?.Stop()); }

        var found = await Task.WhenAll(services.Select(s => ResolveAsync(s, ct)));
        return [.. found.OfType<FoundComputer>().DistinctBy(c => c.Id)];
    }

    // Where a computer waits, and its id and name from its text; none when it does not say in time.
    private static async Task<FoundComputer?> ResolveAsync(NSNetService service, CancellationToken ct)
    {
        var resolved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        UIApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            service.AddressResolved += (_, _) => resolved.TrySetResult(true);
            service.ResolveFailure += (_, _) => resolved.TrySetResult(false);
            service.Resolve(Resolve.TotalSeconds);
        });
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(Resolve);
        using (stop.Token.Register(() => resolved.TrySetResult(false)))
            if (!await resolved.Task) return null;
        ct.ThrowIfCancellationRequested();

        var address = service.Addresses?.Select(IPv4).FirstOrDefault(a => a is not null);
        if (address is null) return null;
        var text = service.GetTxtRecordData() is { } data ? NSNetService.DictionaryFromTxtRecord(data) : null;
        string? Value(string key) => text?[key] is NSData value ? value.ToString(NSStringEncoding.UTF8) : null;
        var port = (int)service.Port;
        return new FoundComputer(Value("id") ?? service.Name, Value("name") ?? service.Name, token => Mdns.ConnectAsync(address, port, token));
    }

    // An IPv4 address from a socket address as Bonjour gives it (sockaddr_in: length, family 2, port, then the address).
    private static IPAddress? IPv4(NSData sockaddr)
    {
        var bytes = sockaddr.ToArray();
        return bytes.Length >= 8 && bytes[1] == 2 ? new IPAddress(bytes[4..8]) : null;
    }
}
