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
    /// <summary>Starts telling the network that this phone offers photos on a port.</summary>
    /// <param name="id">The phone's own id, which tells it from any other.</param>
    /// <param name="name">What the phone is called, as computers show it.</param>
    /// <param name="port">The port the phone waits on.</param>
    void Start(string id, string name, int port);

    /// <summary>Stops telling the network; nothing changes when it was not telling it.</summary>
    void Stop();
}

/// <summary>A phone a computer found offering photos.</summary>
/// <param name="Key">Tells this phone, found this way, from any other.</param>
/// <param name="Name">What the phone is called.</param>
/// <param name="Via">"wifi" or "usb".</param>
/// <param name="Connect">Opens a connection to the phone.</param>
public sealed record FoundPhone(string Key, string Name, string Via, Func<CancellationToken, Task<Stream>> Connect);

/// <summary>Finds the phones offering photos: on the network, or plugged in with a cable.</summary>
public interface IPhoneFinder
{
    /// <summary>The phones offering photos now; none when there are none, or none can be reached this way.</summary>
    Task<IReadOnlyList<FoundPhone>> FindAsync(CancellationToken ct);
}
