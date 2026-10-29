namespace PhotoSense.Domain.Services;

/// <summary>
/// A phone's end of sending photos to a computer: it waits for a computer to connect, over the network and over a
/// cable alike, and hands each connection on. Only while the person has the phone offering photos.
/// </summary>
public interface ITransferListener
{
    /// <summary>Starts waiting; <paramref name="serve"/> is given each connection. Returns the port it waits on.</summary>
    int Start(Func<Stream, CancellationToken, Task> serve);

    /// <summary>Stops waiting, and ends the connections it handed on.</summary>
    void Stop();
}

/// <summary>Tells the computers on the network that this phone is offering photos, and stops telling them.</summary>
public interface ITransferAdvertiser
{
    void Start(string id, string name, int port);

    void Stop();
}

/// <summary>A phone a computer found offering photos.</summary>
/// <param name="Key">Tells this phone, found this way, from any other.</param>
/// <param name="Via">"wifi" or "usb".</param>
public sealed record FoundPhone(string Key, string Name, string Via, Func<CancellationToken, Task<Stream>> Connect);

/// <summary>Finds the phones offering photos: on the network, or plugged in with a cable.</summary>
public interface IPhoneFinder
{
    Task<IReadOnlyList<FoundPhone>> FindAsync(CancellationToken ct);
}
